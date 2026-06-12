using Prometheus;

namespace Bmt.LoadGen;

/// <summary>
/// Prometheus metrics for the load generator, scraped at <c>/metrics</c>. Latency histograms use
/// fixed millisecond buckets; counters and gauges track throughput, errors, and backpressure.
/// </summary>
public sealed class MetricsRegistry
{
    private static readonly double[] LatencyBucketsMs =
        { 0.5, 1, 2, 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000 };

    private readonly string _backend;
    private readonly string _scenario;

    private readonly Histogram _opLatency;
    private readonly Counter _opsTotal;
    private readonly Counter _errorsTotal;
    private readonly Counter _retriesTotal;
    private readonly Counter _poolExhaustionTotal;
    private readonly Counter _dispatcherBackpressureTotal;
    private readonly Gauge _inFlight;
    private readonly Gauge _workerQueueDepth;

    public MetricsRegistry(string backend, string scenario)
    {
        _backend = backend;
        _scenario = scenario;

        _opLatency = Metrics.CreateHistogram(
            "bmt_op_latency_ms",
            "Operation latency in milliseconds.",
            new HistogramConfiguration
            {
                Buckets = LatencyBucketsMs,
                LabelNames = new[] { "op", "backend", "scenario" },
            });

        _opsTotal = Metrics.CreateCounter(
            "bmt_ops_total", "Total operations attempted.",
            new CounterConfiguration { LabelNames = new[] { "op", "backend", "scenario" } });

        _errorsTotal = Metrics.CreateCounter(
            "bmt_errors_total", "Total failed operations.",
            new CounterConfiguration { LabelNames = new[] { "op", "backend", "scenario", "code" } });

        _retriesTotal = Metrics.CreateCounter(
            "bmt_retries_total", "Total operation retries.",
            new CounterConfiguration { LabelNames = new[] { "op", "backend", "scenario" } });

        _poolExhaustionTotal = Metrics.CreateCounter(
            "bmt_pool_exhaustion_total", "Connection-pool wait-queue-full events.",
            new CounterConfiguration { LabelNames = new[] { "backend", "scenario" } });

        _dispatcherBackpressureTotal = Metrics.CreateCounter(
            "bmt_dispatcher_backpressure_total", "Times the dispatcher blocked on a full task channel.",
            new CounterConfiguration { LabelNames = new[] { "backend", "scenario" } });

        _inFlight = Metrics.CreateGauge(
            "bmt_in_flight", "Operations currently in flight.",
            new GaugeConfiguration { LabelNames = new[] { "backend", "scenario" } });

        _workerQueueDepth = Metrics.CreateGauge(
            "bmt_worker_queue_depth", "Task descriptors buffered in the worker channel.",
            new GaugeConfiguration { LabelNames = new[] { "backend", "scenario" } });
    }

    public void ObserveLatency(string op, double ms) =>
        _opLatency.WithLabels(op, _backend, _scenario).Observe(ms);

    public void IncOp(string op) => _opsTotal.WithLabels(op, _backend, _scenario).Inc();

    public void IncError(string op, string code) =>
        _errorsTotal.WithLabels(op, _backend, _scenario, code).Inc();

    public void IncRetry(string op) => _retriesTotal.WithLabels(op, _backend, _scenario).Inc();

    public void IncPoolExhaustion() => _poolExhaustionTotal.WithLabels(_backend, _scenario).Inc();

    public void IncDispatcherBackpressure() =>
        _dispatcherBackpressureTotal.WithLabels(_backend, _scenario).Inc();

    public IDisposable TrackInFlight()
    {
        var g = _inFlight.WithLabels(_backend, _scenario);
        g.Inc();
        return new InFlightScope(g);
    }

    public void SetQueueDepth(double depth) =>
        _workerQueueDepth.WithLabels(_backend, _scenario).Set(depth);

    private sealed class InFlightScope : IDisposable
    {
        private readonly Gauge.Child _gauge;
        private int _disposed;

        public InFlightScope(Gauge.Child gauge) => _gauge = gauge;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _gauge.Dec();
            }
        }
    }
}
