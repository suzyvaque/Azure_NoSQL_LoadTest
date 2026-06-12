# infra/

Azure Infrastructure as Code for the BMT environment.

## Responsibilities

- Provision VM1 (load generator + Grafana host): 8 vCPU / 128 GB RAM, Linux or Windows.
- Provision VM2 (MongoDB target): 32 vCPU / 256 GB RAM, 512 GB premium SSD data disk.
- Provision Azure Cosmos DB for MongoDB (RU, fixed throughput, zone-redundant, API 7).
- Provision Azure DocumentDB (vCore, M80, HA on, Server 7).
- VNet + NSG so VM1 reaches VM2/Cosmos/DocumentDB privately on Mongo wire port.
- Output a `connection-strings.json` (git-ignored) consumed by `seeder/` and `loadgen/`.

## Inputs

- Subscription, region, resource group name, admin SSH key, RU/s tier.

## Outputs

- Deployed resources.
- `connection-strings.json` with keys: `mongoVm`, `cosmosRu`, `documentDb`.
- VM public IPs / DNS for ops access.

## Dependencies

- None inside the repo. Consumed by `mongo-setup/`, `seeder/`, `loadgen/`, `observability/`.

## To be added

- Bicep or Terraform templates (provided by infra owner later).
- Deployment script (`deploy.sh` / `deploy.ps1`) that emits `connection-strings.json`.
- Teardown script.
- Cost-tagging policy on every resource (`bmt-run=true`).
