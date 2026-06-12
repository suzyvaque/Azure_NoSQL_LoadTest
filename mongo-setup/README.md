# mongo-setup/

Install and configure MongoDB 7 on VM2 and create required indexes on all three backends.

## Responsibilities

- Install MongoDB 7.0 Community on VM2 (Ubuntu 22.04 assumed).
- Place `dbPath` on the 512 GB data disk (`/mnt/data/mongo`), journal on same disk.
- Single-node replica set (`rs0`) so the driver uses replica-set semantics like prod.
- WiredTiger cache: default (~50 % RAM = ~128 GB). Do not override.
- `net.maxIncomingConnections = 5000`, `net.bindIp = 0.0.0.0`, auth enabled with a bench user.
- Slow query profiler enabled: `db.setProfilingLevel(1, { slowms: 50 })`.
- Create database `bmt_db` with collections `calc_input` and `calc_output`.
- Indexes: **default `_id` only** on both collections. The workload uses a single sequential
  numeric-string request id `n` as `_id`, and every operation (`find`/`insert`/`remove`) keys
  on `_id` (IDHACK). No secondary index is needed. A `ReqId` field equal to `_id` is stored for
  production-schema parity but is intentionally **not** indexed.
- `create-indexes.js` is idempotent and drops any stray secondary index left by an older plan.

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

- `install-mongo.sh` (apt repo, install, systemd, RS initiate). _(deferred — Mongo VM is a later task)_
- `mongod.conf` template. _(deferred)_
- `create-indexes.js` runnable via `mongosh --file` against any backend conn string. ✅
- `verify.sh` that prints version, RS status, index list, profiler level. ✅

## Usage (DocumentDB / Cosmos / Mongo VM)

```
# create db + collections and enforce _id-only index layout
mongosh "<connection-string>" --file create-indexes.js

# verify version + indexes (exit 0 = _id indexes only)
./verify.sh "<connection-string>" [bmt_db]
```

The database name defaults to `bmt_db`; override with
`--eval "var DB_NAME='other'"` before `--file`, or pass it as the second arg to `verify.sh`.
