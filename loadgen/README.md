# loadgen/

C# .NET 8 **daemon** that executes one scenario × one backend per process and emits metrics for Grafana.

## Responsibilities

- Long-running daemon (systemd unit or `dotnet run`) controlled by CLI flags and a scenario JSON.
- Async worker pool (no thread-per-task):
  - Bounded `Channel<TaskDescriptor>` (capacity = 2 × workers).
  - Job dispatcher generates Poisson-arrival jobs → enqueues K task descriptors per job.
  - N worker consumers run the task sequence: `find` → `Task.Delay(thinkMs)` → `insertOne` → `deleteOne`.
- Mongo driver settings:
  - `MaxConnectionPoolSize` = workers, `MinConnectionPoolSize` = 50.
  - `WriteConcern = w:1`, `ReadConcern = local`, `ReadPreference = primary`.
  - `RetryReads = true`, `RetryWrites = true`.
  - Timeouts: connect 10 s, socket 30 s, server-selection 15 s, waitQueue 10 s.
- Metrics:
  - Prometheus `/metrics` endpoint on `:9100` via `prometheus-net`.
  - Histograms per op (`find`, `insert`, `delete`) with buckets `[0.5,1,2,5,10,25,50,100,250,500,1000,2500,5000] ms`, labeled `backend`, `scenario`.
  - Counters: ops_total, errors_total, retries_total, pool_exhaustion_total, dispatcher_backpressure_total.
  - Gauges: in_flight, worker_queue_depth.
- Raw sample CSV: 10 % sampling of all ops to `runs/<run-id>/raw-sample.csv` (`ts_us,op,latency_us,success,err`).
- Structured logs (Serilog → file + stdout) at `runs/<run-id>/loadgen.log`.
- Graceful shutdown on SIGINT/SIGTERM: drain in-flight, flush CSV, write `summary.json`.
- Pre-compute all `_id`s for the run from `Ids` sampler (uniform or Zipfian) into an in-memory array.

## CLI

```
loadgen --backend {mongo|cosmosru|docdb}
        --conn "<connection-string>"
        --scenario scenarios/S2.json
        --run-id <id>
        [--workers N] [--pool-size N]            # override scenario
        [--duration-min N]
        [--distribution {uniform|zipfian}]
        [--seed <int>]
        [--metrics-port 9100]
```

## Inputs

- Connection string (env `BMT_CONN` or `--conn`).
- Scenario JSON from `scenarios/`.
- `contracts/` models.

## Outputs

- `runs/<run-id>/config.json` — frozen effective config.
- `runs/<run-id>/raw-sample.csv` — 10 % op samples.
- `runs/<run-id>/loadgen.log` — structured logs.
- `runs/<run-id>/summary.json` — totals, error counts, run window, host info.
- `/metrics` HTTP endpoint scraped by Prometheus.

## Dependencies

- `contracts/` (project reference).
- A seeded target backend (`seeder/` must have run first).
- `observability/` Prometheus must be configured to scrape `vm1:9100`.

## To be added

- `LoadGen.csproj` (.NET 8, `Microsoft.Extensions.Hosting`, `MongoDB.Driver`, `prometheus-net`, `Serilog`, `System.CommandLine`).
- `Program.cs` (host bootstrap, CLI parsing, signal handling).
- `Dispatcher.cs` (Poisson job generator).
- `Worker.cs` (task execution loop).
- `MetricsRegistry.cs` (Prom histograms/counters/gauges).
- `RawSampleWriter.cs` (lock-free batched CSV writer).
- `BackendFactory.cs` (one `IMongoClient` builder for all three backends).
- `loadgen.service` systemd unit template.
- Smoke test (10-second tiny scenario) and an integration test using `Mongo2Go` or a real local mongod.
