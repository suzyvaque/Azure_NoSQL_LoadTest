using System.Text.Json;
using System.Threading.Channels;
using Bmt.Contracts;
using MongoDB.Driver;
using Serilog;

namespace Bmt.LoadGen;

/// <summary>
/// Orchestrates one scenario × one backend run: wires up metrics, the worker pool, the Poisson
/// dispatcher, and artifact writers, then runs to completion or graceful shutdown.
/// </summary>
public sealed class LoadRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly EffectiveConfig _config;
    private readonly ScenarioConfig _scenario;
    private readonly RunArtifacts _artifacts;
    private readonly string _connectionString;
    private readonly ILogger _log;

    public LoadRunner(
        EffectiveConfig config,
        ScenarioConfig scenario,
        RunArtifacts artifacts,
        string connectionString,
        ILogger log)
    {
        _config = config;
        _scenario = scenario;
        _artifacts = artifacts;
        _connectionString = connectionString;
        _log = log;
    }

    /// <summary>
    /// Runs the scenario. <paramref name="gracefulStop"/> halts the dispatcher and drains
    /// in-flight work; <paramref name="forceStop"/> aborts workers immediately.
    /// </summary>
    public async Task<int> RunAsync(CancellationToken gracefulStop, CancellationToken forceStop)
    {
        _artifacts.EnsureRunDir();
        WriteConfig();

        var metrics = new MetricsRegistry(_config.Backend, _config.ScenarioName);
        var counters = new RunCounters();

        await using var samples = new RawSampleWriter(_artifacts.RawSampleCsv);

        var client = BackendFactory.Create(_connectionString, _config.PoolSize, _config.Backend);
        var (input, output) = BackendFactory.GetCollections(client);

        await PreflightAsync(client, gracefulStop).ConfigureAwait(false);

        int capacity = Math.Max(2 * _config.Workers, 2);
        var channel = Channel.CreateBounded<TaskDescriptor>(new BoundedChannelOptions(capacity)
        {
            SingleReader = false,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

        var idSampler = Ids.CreateSampler(
            _scenario.Distribution,
            _scenario.IdMax,
            _scenario.ZipfExponent,
            _config.Seed);

        var startUtc = DateTime.UtcNow;

        using var depthTimer = new Timer(
            _ => metrics.SetQueueDepth(counters.ReadQueue()), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

        var workers = new Task[_config.Workers];
        for (int i = 0; i < _config.Workers; i++)
        {
            var worker = new Worker(
                i, channel.Reader, input, output, _scenario, metrics, counters, samples, _config.Seed);
            workers[i] = Task.Run(() => worker.RunAsync(forceStop), forceStop);
        }

        var dispatcher = new Dispatcher(
            _scenario, channel.Writer, idSampler, metrics, counters, _config.Seed, _log);

        _log.Information("run {RunId} started: {Workers} workers, pool {Pool}, metrics :{Port}",
            _config.RunId, _config.Workers, _config.PoolSize, _config.MetricsPort);

        await dispatcher.RunAsync(gracefulStop).ConfigureAwait(false);
        _log.Information("dispatcher complete; draining workers...");

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _log.Warning("workers force-stopped before fully draining.");
        }

        var endUtc = DateTime.UtcNow;
        await samples.DisposeAsync().ConfigureAwait(false);

        var summary = BuildSummary(counters.Snapshot(), startUtc, endUtc);
        WriteSummary(summary);

        _log.Information(
            "run {RunId} done: {Ops:N0} ops, {Errors:N0} errors ({Rate:P3}), {Ops_s:N0} ops/s over {Sec:N0}s.",
            _config.RunId, summary.TotalOps, summary.TotalErrors, summary.ErrorRate,
            summary.OpsPerSecond, summary.DurationSeconds);

        return summary.TotalErrors == 0 ? 0 : 1;
    }

    private async Task PreflightAsync(IMongoClient client, CancellationToken ct)
    {
        try
        {
            var db = client.GetDatabase(Names.Database);
            await db.RunCommandAsync<MongoDB.Bson.BsonDocument>(
                new MongoDB.Bson.BsonDocument("ping", 1), cancellationToken: ct).ConfigureAwait(false);
            _log.Information("preflight: connection to {Backend} OK.", _config.Backend);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "preflight: failed to reach backend {Backend}.", _config.Backend);
            throw;
        }
    }

    private void WriteConfig()
    {
        File.WriteAllText(_artifacts.ConfigJson, JsonSerializer.Serialize(_config, JsonOptions));
    }

    private RunSummary BuildSummary(CountersSnapshot snap, DateTime startUtc, DateTime endUtc)
    {
        double seconds = Math.Max((endUtc - startUtc).TotalSeconds, 0.001);
        long totalOps = snap.Finds + snap.Inserts + snap.Deletes;
        long totalErrors = snap.FindErrors + snap.InsertErrors + snap.DeleteErrors;

        return new RunSummary
        {
            RunId = _config.RunId,
            Backend = _config.Backend,
            ScenarioName = _config.ScenarioName,
            StartedAtUtc = startUtc,
            EndedAtUtc = endUtc,
            DurationSeconds = Math.Round(seconds, 3),
            TotalOps = totalOps,
            TotalErrors = totalErrors,
            OpsPerSecond = Math.Round(totalOps / seconds, 2),
            TasksPerSecond = Math.Round(snap.Finds / seconds, 2),
            ErrorRate = totalOps > 0 ? (double)totalErrors / totalOps : 0,
            Counters = snap,
            Host = _config.Host,
            ProcessorCount = _config.ProcessorCount,
        };
    }

    private void WriteSummary(RunSummary summary)
    {
        File.WriteAllText(_artifacts.SummaryJson, JsonSerializer.Serialize(summary, JsonOptions));
    }
}
