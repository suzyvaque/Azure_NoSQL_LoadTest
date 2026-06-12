using System.Threading.Channels;
using Bmt.Contracts;
using Serilog;

namespace Bmt.LoadGen;

/// <summary>
/// Generates jobs as a Poisson arrival process and enqueues their task descriptors onto the
/// bounded worker channel. Supports a linear ramp of the arrival rate over the run.
/// </summary>
public sealed class Dispatcher
{
    private readonly ScenarioConfig _scenario;
    private readonly ChannelWriter<TaskDescriptor> _writer;
    private readonly IIdSampler _idSampler;
    private readonly MetricsRegistry _metrics;
    private readonly RunCounters _counters;
    private readonly Random _arrivalRng;
    private readonly ILogger _log;

    public Dispatcher(
        ScenarioConfig scenario,
        ChannelWriter<TaskDescriptor> writer,
        IIdSampler idSampler,
        MetricsRegistry metrics,
        RunCounters counters,
        int? seed,
        ILogger log)
    {
        _scenario = scenario;
        _writer = writer;
        _idSampler = idSampler;
        _metrics = metrics;
        _counters = counters;
        _arrivalRng = seed is { } s ? new Random(s ^ 0x5151) : new Random();
        _log = log;
    }

    /// <summary>
    /// Runs until the configured duration elapses or cancellation is requested, then completes
    /// the channel so workers drain and exit.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var duration = TimeSpan.FromMinutes(_scenario.DurationMinutes);
        var start = DateTime.UtcNow;
        double startRate = _scenario.JobsPerHour / 3600.0;
        double endRate = (_scenario.RampToJobsPerHour ?? _scenario.JobsPerHour) / 3600.0;

        _log.Information(
            "dispatcher start: {Jobs}/hr{Ramp}, {Tasks} tasks/job, duration {Min} min",
            _scenario.JobsPerHour,
            _scenario.RampToJobsPerHour is { } r ? $" -> {r}/hr ramp" : string.Empty,
            _scenario.TasksPerJob,
            _scenario.DurationMinutes);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var elapsed = DateTime.UtcNow - start;
                if (elapsed >= duration)
                {
                    break;
                }

                double frac = duration.TotalSeconds > 0 ? elapsed.TotalSeconds / duration.TotalSeconds : 0;
                double rate = startRate + (endRate - startRate) * Math.Clamp(frac, 0, 1);
                if (rate <= 0)
                {
                    rate = double.Epsilon;
                }

                // Exponential inter-arrival time for a Poisson process.
                double u = 1.0 - _arrivalRng.NextDouble();
                double waitSeconds = -Math.Log(u) / rate;
                var waitMs = (int)Math.Min(waitSeconds * 1000.0, int.MaxValue);
                if (waitMs > 0)
                {
                    await Task.Delay(waitMs, ct).ConfigureAwait(false);
                }

                await DispatchJobAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _log.Information("dispatcher cancelled.");
        }
        finally
        {
            _writer.TryComplete();
            var snap = _counters.Snapshot();
            _log.Information(
                "dispatcher done: {Jobs} jobs / {Tasks} tasks enqueued.",
                snap.JobsDispatched, snap.TasksDispatched);
        }
    }

    private async Task DispatchJobAsync(CancellationToken ct)
    {
        _counters.IncJob();
        int tasks = _scenario.TasksPerJob;
        _counters.AddTasks(tasks);

        for (int i = 0; i < tasks; i++)
        {
            var descriptor = new TaskDescriptor(_idSampler.Next());

            if (!_writer.TryWrite(descriptor))
            {
                // Channel is full: record backpressure, then block until space frees up.
                _counters.IncBackpressure();
                _metrics.IncDispatcherBackpressure();
                await _writer.WriteAsync(descriptor, ct).ConfigureAwait(false);
            }

            _counters.IncQueue();
        }
    }
}
