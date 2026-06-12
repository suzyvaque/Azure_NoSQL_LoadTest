# observability/

Prometheus, exporters, and Grafana dashboards for VM1. Grafana is already installed on VM1.

## Responsibilities

- Prometheus (container or systemd on VM1) scraping:
  - `loadgen` on `vm1:9100/metrics`
  - `node_exporter` on VM2:9100
  - `mongodb_exporter` on VM2:9216
- Pre-built Grafana dashboards (provisioned as JSON):
  - **Client view**: ops/sec by op, latency p50/p95/p99/p99.9, error & retry rate, in-flight, worker queue depth, dispatcher backpressure.
  - **Mongo VM view**: CPU, mem, disk IOPS/throughput/latency, network, WT cache dirty %, connections, opcounters.
  - **Backend comparison**: same panels with `backend` selector (`mongo|cosmosru|docdb`).
- Azure Monitor side: documented PromQL/Azure-Monitor queries for Cosmos RU (RU/s consumed, 429 count, normalized RU) and DocumentDB (CPU, IOPS, connections) — link panels via Azure Monitor data source.
- Alerting rules: error rate > 0.1 %, p99 find > 50 ms, p99 insert/delete > 100 ms, throttle > 0.

## Inputs

- VM1 has Grafana installed (already done).
- `loadgen/` exposes `/metrics`.
- VM2 reachable on exporter ports.
- Azure Monitor read access for Cosmos/DocumentDB metrics.

## Outputs

- Running Prometheus scraping all three layers.
- Provisioned Grafana dashboards (JSON in `dashboards/`).
- Alert rules in `alerts/`.

## Dependencies

- `infra/` (VM1, VM2 networking).
- `loadgen/` (metrics endpoint contract).
- `mongo-setup/` (exporters configured on VM2).

## To be added

- `docker-compose.yml` (Prometheus + optional mongodb_exporter sidecar).
- `prometheus.yml` (scrape configs).
- `dashboards/client.json`, `dashboards/mongo-vm.json`, `dashboards/comparison.json`.
- `provisioning/datasources.yml`, `provisioning/dashboards.yml` for Grafana.
- `alerts/rules.yml`.
- `azure-monitor-queries.md` documenting Cosmos / DocumentDB queries.
