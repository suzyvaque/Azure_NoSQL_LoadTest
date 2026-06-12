# mongo-setup/

Install and configure MongoDB 7 on VM2 and create required indexes on all three backends.

## Responsibilities

- Install MongoDB 7.0 Community on VM2 (Ubuntu 22.04 assumed).
- Place `dbPath` on the 512 GB data disk (`/mnt/data/mongo`), journal on same disk.
- Single-node replica set (`rs0`) so the driver uses replica-set semantics like prod.
- WiredTiger cache: default (~50 % RAM = ~128 GB). Do not override.
- `net.maxIncomingConnections = 5000`, `net.bindIp = 0.0.0.0`, auth enabled with a bench user.
- Slow query profiler enabled: `db.setProfilingLevel(1, { slowms: 50 })`.
- Create indexes on `bmt_db.calc_output`:
  - default `_id`
  - secondary single-field ascending on `ReqId` (non-unique)
- Verify indexes on `calc_input` is `_id` only.
- Idempotent: re-running must not break an existing install.

## Inputs

- VM2 SSH access from `infra/`.
- Bench user credentials (env or vault).

## Outputs

- Running `mongod` 7.0 on VM2:27017 as `rs0`.
- Indexes created on `bmt_db.calc_output` for all three backends (script accepts any Mongo-compatible connection string).
- Connection string emitted/confirmed for `loadgen/`.

## Dependencies

- `infra/` (VM2 must exist; data disk mounted).
- Indirectly used by `seeder/` and `loadgen/` (they assume indexes exist).

## To be added

- `install-mongo.sh` (apt repo, install, systemd, RS initiate).
- `mongod.conf` template.
- `create-indexes.js` runnable via `mongosh --file` against any backend conn string.
- `verify.sh` that prints version, RS status, index list, profiler level.
