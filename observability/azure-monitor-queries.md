# Azure Monitor queries — Cosmos DB (RU) and DocumentDB (vCore)

Client-side latency/throughput come from the loadgen Prometheus metrics. The **server-side**
view for the managed backends comes from Azure Monitor. Add them to Grafana via the
**Azure Monitor** data source (see `provisioning/datasources.yml`) or query them in the Azure
portal / `az monitor metrics list`.

## Cosmos DB for MongoDB (RU)

| Signal | Metric (namespace `Microsoft.DocumentDB/databaseAccounts`) | Aggregation | Notes |
|---|---|---|---|
| RU/s consumed | `TotalRequestUnits` | Total (per minute) | Divide by 60 for RU/s. Split by `CollectionName`. |
| Normalized RU consumption | `NormalizedRUConsumption` | Max | % of provisioned RU; 100% ⇒ throttling imminent. |
| Throttled requests (429) | `TotalRequests` filtered `StatusCode == 429` | Count | The throttle KPI. |
| Total requests | `TotalRequests` | Count | Split by `StatusCode` for error breakdown. |
| Server latency | `ServerSideLatency` | Average / P99 | Per operation type where available. |

Example (Azure CLI):

```bash
az monitor metrics list \
  --resource "<cosmos-account-resource-id>" \
  --metric "TotalRequestUnits" "NormalizedRUConsumption" \
  --interval PT1M --aggregation Total Maximum \
  --start-time 2026-06-13T00:00:00Z --end-time 2026-06-13T01:00:00Z
```

KQL (Log Analytics, if diagnostic settings route to a workspace):

```kusto
AzureDiagnostics
| where ResourceProvider == "MICROSOFT.DOCUMENTDB"
| where Category == "MongoRequests"
| summarize
    requests = count(),
    throttled = countif(statusCode_s == "429"),
    p99_ms = percentile(duration_s, 99)
  by bin(TimeGenerated, 1m), collectionName_s
```

## DocumentDB (vCore, M80)

| Signal | Metric (namespace `Microsoft.DocumentDB/mongoClusters`) | Aggregation | Notes |
|---|---|---|---|
| CPU | `CPUPercent` | Average / Max | Saturation indicator. |
| Memory | `MemoryPercent` | Average / Max | |
| IOPS | `IOPS` | Average | Disk operations/sec. |
| Storage | `StoragePercent` | Max | |
| Connections | `ActiveConnections` | Max | Compare to pool size. |

Example (Azure CLI):

```bash
az monitor metrics list \
  --resource "<documentdb-cluster-resource-id>" \
  --metric "CPUPercent" "IOPS" "ActiveConnections" \
  --interval PT1M --aggregation Average Maximum \
  --start-time 2026-06-13T00:00:00Z --end-time 2026-06-13T01:00:00Z
```

## Correlating with runs

- Align Azure Monitor time ranges to the run window in `runs/<run-id>/summary.json`
  (`startedAtUtc` / `endedAtUtc`).
- For Cosmos RU, capture peak `NormalizedRUConsumption` and total 429s per run to compute the
  effective RU headroom and cost per 1M tasks.
- For DocumentDB, capture peak `CPUPercent` and `IOPS` to locate the saturation point in
  `S2-stress`.
