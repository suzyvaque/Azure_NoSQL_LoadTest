using System.CommandLine;
using System.Runtime.InteropServices;
using Bmt.Contracts;
using Prometheus;
using Serilog;
using Serilog.Events;

namespace Bmt.LoadGen;

/// <summary>
/// CLI entry point and host bootstrap for the load generator daemon. Parses flags, loads a
/// scenario, starts the Prometheus endpoint, wires graceful shutdown, and runs the scenario.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var backendOption = new Option<Backend>("--backend", "Target backend: Mongo | CosmosRu | DocDb.") { IsRequired = true };
        var connOption = new Option<string?>("--conn", "Connection string (falls back to BMT_CONN).");
        var scenarioOption = new Option<string>("--scenario", "Path to a scenario JSON file.") { IsRequired = true };
        var runIdOption = new Option<string?>("--run-id", "Run id; auto-generated from date/scenario/backend if omitted.");
        var runsRootOption = new Option<string>("--runs-root", () => "runs", "Root directory for run artifacts.");
        var workersOption = new Option<int?>("--workers", "Override scenario worker count.");
        var poolOption = new Option<int?>("--pool-size", "Override connection pool size.");
        var durationOption = new Option<int?>("--duration-min", "Override scenario duration (minutes).");
        var distributionOption = new Option<IdDistribution?>("--distribution", "Override id distribution: Uniform | Zipfian.");
        var seedOption = new Option<int?>("--seed", "RNG seed for reproducible runs.");
        var metricsPortOption = new Option<int>("--metrics-port", () => 9100, "Prometheus /metrics port.");

        var root = new RootCommand("BMT load generator daemon")
        {
            backendOption, connOption, scenarioOption, runIdOption, runsRootOption,
            workersOption, poolOption, durationOption, distributionOption, seedOption, metricsPortOption,
        };

        root.SetHandler(async context =>
        {
            var p = context.ParseResult;
            context.ExitCode = await RunAsync(
                p.GetValueForOption(backendOption),
                p.GetValueForOption(connOption),
                p.GetValueForOption(scenarioOption)!,
                p.GetValueForOption(runIdOption),
                p.GetValueForOption(runsRootOption)!,
                p.GetValueForOption(workersOption),
                p.GetValueForOption(poolOption),
                p.GetValueForOption(durationOption),
                p.GetValueForOption(distributionOption),
                p.GetValueForOption(seedOption),
                p.GetValueForOption(metricsPortOption)).ConfigureAwait(false);
        });

        return await root.InvokeAsync(args).ConfigureAwait(false);
    }

    private static async Task<int> RunAsync(
        Backend backend, string? conn, string scenarioPath, string? runId, string runsRoot,
        int? workers, int? poolSize, int? durationMin, IdDistribution? distribution, int? seed, int metricsPort)
    {
        conn ??= Environment.GetEnvironmentVariable("BMT_CONN");
        if (string.IsNullOrWhiteSpace(conn))
        {
            Console.Error.WriteLine("ERROR: provide --conn or set BMT_CONN.");
            return 2;
        }

        if (!File.Exists(scenarioPath))
        {
            Console.Error.WriteLine($"ERROR: scenario file not found: {scenarioPath}");
            return 2;
        }

        ScenarioConfig scenario;
        try
        {
            scenario = ScenarioConfig.Load(scenarioPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: invalid scenario: {ex.Message}");
            return 2;
        }

        // Apply CLI overrides.
        if (workers is { } w) scenario.Workers = w;
        if (poolSize is { } ps) scenario.PoolSize = ps;
        if (durationMin is { } d) scenario.DurationMinutes = d;
        if (distribution is { } dist) scenario.Distribution = dist;

        runId ??= RunArtifacts.BuildRunId(DateOnly.FromDateTime(DateTime.UtcNow), scenario.Name, backend);
        var artifacts = new RunArtifacts(runsRoot, runId);
        artifacts.EnsureRunDir();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("MongoDB", LogEventLevel.Warning)
            .WriteTo.Console()
            .WriteTo.File(artifacts.LoadGenLog, rollingInterval: RollingInterval.Infinite, shared: true)
            .CreateLogger();

        var config = new EffectiveConfig
        {
            RunId = runId,
            Backend = RunArtifacts.BackendToken(backend),
            ScenarioName = scenario.Name,
            ScenarioFile = Path.GetFullPath(scenarioPath),
            Workers = scenario.Workers,
            PoolSize = scenario.PoolSize,
            DurationMinutes = scenario.DurationMinutes,
            JobsPerHour = scenario.JobsPerHour,
            RampToJobsPerHour = scenario.RampToJobsPerHour,
            TasksPerJob = scenario.TasksPerJob,
            Distribution = scenario.Distribution.ToString(),
            ZipfExponent = scenario.ZipfExponent,
            IdRange = scenario.IdRange,
            ThinkMsMin = scenario.ThinkMsMin,
            ThinkMsMax = scenario.ThinkMsMax,
            WriteConcern = scenario.WriteConcern,
            Seed = seed,
            MetricsPort = metricsPort,
            Host = Environment.MachineName,
            ProcessorCount = Environment.ProcessorCount,
            StartedAtUtc = DateTime.UtcNow,
        };

        using var gracefulCts = new CancellationTokenSource();
        using var forceCts = new CancellationTokenSource();
        WireSignals(gracefulCts, forceCts);

        MetricServer? metricServer = null;
        try
        {
            metricServer = new MetricServer(port: metricsPort);
            metricServer.Start();
            Log.Information("Prometheus metrics on http://0.0.0.0:{Port}/metrics", metricsPort);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "could not start metrics server on port {Port}; continuing without it.", metricsPort);
        }

        try
        {
            var runner = new LoadRunner(config, scenario, artifacts, conn, Log.Logger);
            return await runner.RunAsync(gracefulCts.Token, forceCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "run failed.");
            return 1;
        }
        finally
        {
            if (metricServer is not null)
            {
                await metricServer.StopAsync().ConfigureAwait(false);
            }

            await Log.CloseAndFlushAsync().ConfigureAwait(false);
        }
    }

    private static void WireSignals(CancellationTokenSource gracefulCts, CancellationTokenSource forceCts)
    {
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            if (!gracefulCts.IsCancellationRequested)
            {
                Log.Information("SIGINT received: stopping dispatcher, draining in-flight work. Press Ctrl+C again to force.");
                gracefulCts.Cancel();
            }
            else
            {
                Log.Warning("second SIGINT: forcing shutdown.");
                forceCts.Cancel();
            }
        };

        // SIGTERM (systemd stop) -> graceful.
        PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
        {
            ctx.Cancel = true;
            Log.Information("SIGTERM received: graceful shutdown.");
            if (!gracefulCts.IsCancellationRequested)
            {
                gracefulCts.Cancel();
            }
        });
    }
}
