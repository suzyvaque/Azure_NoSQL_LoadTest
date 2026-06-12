using System.Diagnostics;
using System.Threading.Channels;
using Bmt.Contracts;
using Bmt.Contracts.Models;
using MongoDB.Driver;

namespace Bmt.LoadGen;

/// <summary>
/// Consumes task descriptors and runs the workload sequence per task:
/// <c>find({_id})</c> on calc_input → think delay → <c>insertOne</c> into calc_output →
/// <c>deleteOne</c> from calc_output. Records latency, counters, and 10% raw samples.
/// </summary>
public sealed class Worker
{
    private const double SampleRate = 0.10;
    private static readonly long TicksPerMicrosecond = Stopwatch.Frequency / 1_000_000L;
    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly int _index;
    private readonly ChannelReader<TaskDescriptor> _reader;
    private readonly IMongoCollection<CalcInputDoc> _input;
    private readonly IMongoCollection<CalcOutputDoc> _output;
    private readonly ScenarioConfig _scenario;
    private readonly MetricsRegistry _metrics;
    private readonly RunCounters _counters;
    private readonly RawSampleWriter _samples;
    private readonly Random _rng;
    private readonly string _outputPayload;

    public Worker(
        int index,
        ChannelReader<TaskDescriptor> reader,
        IMongoCollection<CalcInputDoc> input,
        IMongoCollection<CalcOutputDoc> output,
        ScenarioConfig scenario,
        MetricsRegistry metrics,
        RunCounters counters,
        RawSampleWriter samples,
        int? seed)
    {
        _index = index;
        _reader = reader;
        _input = input;
        _output = output;
        _scenario = scenario;
        _metrics = metrics;
        _counters = counters;
        _samples = samples;
        _rng = seed is { } s ? new Random(s + index) : new Random();

        // Small fixed result payload (mirrors the tiny insert in the reference profile).
        var bytes = new byte[192];
        _rng.NextBytes(bytes);
        _outputPayload = Convert.ToBase64String(bytes);
    }

    /// <summary>Runs until the channel is completed and drained.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (await _reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_reader.TryRead(out var task))
                {
                    _counters.DecQueue();
                    await ExecuteTaskAsync(task, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown.
        }
    }

    private async Task ExecuteTaskAsync(TaskDescriptor task, CancellationToken ct)
    {
        string id = Ids.Format(task.Id);

        await RunFindAsync(id, ct).ConfigureAwait(false);

        // Pseudo-calculation think time.
        int thinkMs = _scenario.ThinkMsMin == _scenario.ThinkMsMax
            ? _scenario.ThinkMsMin
            : _rng.Next(_scenario.ThinkMsMin, _scenario.ThinkMsMax + 1);
        if (thinkMs > 0)
        {
            await Task.Delay(thinkMs, ct).ConfigureAwait(false);
        }

        await RunInsertAsync(id, ct).ConfigureAwait(false);
        await RunDeleteAsync(id, ct).ConfigureAwait(false);
    }

    private async Task RunFindAsync(string id, CancellationToken ct)
    {
        await RunOpAsync(OpKind.Find, "find", async () =>
        {
            var filter = Builders<CalcInputDoc>.Filter.Eq(d => d.Id, id);
            using var cursor = await _input.FindAsync(filter, new FindOptions<CalcInputDoc> { Limit = 1 }, ct)
                .ConfigureAwait(false);
            await cursor.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private async Task RunInsertAsync(string id, CancellationToken ct)
    {
        var doc = new CalcOutputDoc
        {
            Id = id,
            ReqId = id,
            Output = _outputPayload,
            ExitCode = 0,
            CalculatorFileNm = "PricingEngine.dll",
            CalculatorVersion = "1.0.0",
            CompletedAtUtc = DateTime.UtcNow,
        };

        await RunOpAsync(OpKind.Insert, "insert",
            () => _output.InsertOneAsync(doc, cancellationToken: ct), ct).ConfigureAwait(false);
    }

    private async Task RunDeleteAsync(string id, CancellationToken ct)
    {
        await RunOpAsync(OpKind.Delete, "delete", () =>
        {
            var filter = Builders<CalcOutputDoc>.Filter.Eq(d => d.Id, id);
            return _output.DeleteOneAsync(filter, ct);
        }, ct).ConfigureAwait(false);
    }

    private async Task RunOpAsync(OpKind kind, string opLabel, Func<Task> op, CancellationToken ct)
    {
        long startTicks = Stopwatch.GetTimestamp();
        bool success = false;
        string? err = null;

        using (_metrics.TrackInFlight())
        {
            try
            {
                await op().ConfigureAwait(false);
                success = true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                err = ClassifyError(ex);
                _counters.IncError(kind);
                _metrics.IncError(opLabel, err);
            }
        }

        long elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
        long latencyUs = TicksPerMicrosecond > 0 ? elapsedTicks / TicksPerMicrosecond : 0;
        double latencyMs = latencyUs / 1000.0;

        _counters.IncOp(kind);
        _metrics.IncOp(opLabel);
        _metrics.ObserveLatency(opLabel, latencyMs);

        if (_rng.NextDouble() < SampleRate)
        {
            long tsUs = (long)((DateTime.UtcNow - UnixEpoch).TotalMilliseconds * 1000.0);
            _samples.Write(tsUs, opLabel, latencyUs, success, err);
        }
    }

    private string ClassifyError(Exception ex)
    {
        switch (ex)
        {
            case MongoWaitQueueFullException:
                _counters.IncPoolExhaustion();
                _metrics.IncPoolExhaustion();
                return "pool_exhausted";
            case MongoConnectionException:
                return "connection";
            case MongoExecutionTimeoutException:
                return "timeout";
            case MongoCommandException cmd:
                return $"cmd_{cmd.Code}";
            case MongoWriteException we:
                return $"write_{(int?)we.WriteError?.Code}";
            case TimeoutException:
                return "server_selection_timeout";
            default:
                return ex.GetType().Name;
        }
    }
}
