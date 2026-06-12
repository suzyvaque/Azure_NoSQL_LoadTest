# Azure NoSQL Load Test (BMT)

Performance benchmark comparing three MongoDB-API backends on Azure under a recreated production workload:

| Backend | Tier | Notes |
|---|---|---|
| MongoDB on Azure VM | Server 7.0 | Single-node replica set on VM2 (HA off for now; prod is HA) |
| Azure Cosmos DB for MongoDB (RU) | API 7, Zone Redundant | Fixed provisioned RU/s |
| Azure DocumentDB (vCore) | M80, HA on, Server 7 | Managed |

## Objectives

1. Reproduce on-prem prod **peak hour** behavior (S2: 1,099 jobs / 484,755 tasks ≈ **135 tasks/sec ≈ 405 DB ops/sec**).
2. Measure sustained throughput, p50/p95/p99/p99.9 latency, error/throttle rate, and cost per 1 M tasks for each backend.
3. Provide a fair, reproducible, scenario-driven comparison.

## Workload model

One **task** = `find({_id})` on `calc_input` → pseudo-calc sleep → `insertOne` on `calc_output` → `deleteOne` on `calc_output`. Tasks are grouped into **jobs** that arrive as a Poisson process. Five scenarios (S1 small, **S2 prod-peak**, S3 fat, S2-burst ×3, S2-stress ramp). Write concern fixed at `w:1`.

See `docs/test-plan.md` for full design.

## Architecture

```
                       ┌──────────────── VM1 (8c/128GB) ────────────────┐
                       │                                                │
  scenarios/*.json ──► │  loadgen (C# .NET 8 daemon)                    │
  contracts/ ─────────►│    └─ Prometheus /metrics  ──┐                 │
                       │  seeder (one-off CLI)        │                 │
                       │  Prometheus + Grafana ◄──────┘ (pre-installed) │
                       │  analysis/ (post-run scripts)                  │
                       └───────────────┬────────────────────────────────┘
                                       │ wire protocol (w:1)
                       ┌───────────────┼────────────────┬───────────────┐
                       ▼               ▼                ▼               
              ┌────────────────┐ ┌──────────────┐ ┌────────────────────┐
              │ VM2 (32c/256GB │ │ Cosmos DB    │ │ Azure DocumentDB   │
              │ + 512GB disk)  │ │ for MongoDB  │ │ (vCore) M80 HA     │
              │ MongoDB 7      │ │ RU, fixed    │ │ Server 7           │
              └───────┬────────┘ └──────────────┘ └────────────────────┘
                      │
            node_exporter + mongodb_exporter ──► VM1 Prometheus
```

## Repository layout

| Folder | Purpose |
|---|---|
| `infra/` | Azure IaC (Bicep/Terraform). VMs, networking, Cosmos, DocumentDB. |
| `mongo-setup/` | Install + configure MongoDB 7 on VM2; index creation scripts. |
| `seeder/` | C# CLI that seeds `calc_input` with 1 M docs at the prescribed size mix. |
| `contracts/` | Shared C# models, schema constants, ID format, document size buckets. |
| `loadgen/` | C# .NET 8 daemon that runs scenarios against any backend. |
| `scenarios/` | Declarative scenario JSON files (S1, S2, S3, S2-burst, S2-stress, warmup). |
| `observability/` | Prometheus config, exporters, Grafana dashboards, alerting rules. |
| `analysis/` | Post-run aggregation: tail latency from raw CSV, comparison reports. |
| `runs/` | Artifacts per run (config, summary, raw samples, prom snapshots). Git-ignored. |
| `docs/` | Test plan, runbook, results template. |

## Quick start (once all components are built)

```
# 1. Deploy infra
cd infra && <provision commands>

# 2. Install Mongo on VM2
cd mongo-setup && ./install.sh

# 3. Seed calc_input on each backend
seeder --backend mongo   --conn "<conn>" --docs 1000000
seeder --backend cosmosru --conn "<conn>" --docs 1000000
seeder --backend docdb   --conn "<conn>" --docs 1000000

# 4. Start observability stack on VM1
cd observability && docker compose up -d

# 5. Run scenario (example)
loadgen --backend mongo --conn "<conn>" --scenario scenarios/S2.json \
        --run-id 2026-06-13_S2_mongo

# 6. Analyze
cd analysis && python summarize.py runs/2026-06-13_S2_mongo
```

## Conventions

- All timestamps UTC, ISO-8601.
- All latencies in microseconds in raw records; milliseconds in Prom histograms.
- Run IDs: `YYYY-MM-DD_<scenario>_<backend>[_<variant>]`.
- One run = one process; never multiplex backends or scenarios.
