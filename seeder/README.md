# seeder/

C# .NET 8 CLI that seeds `bmt_db.calc_input` with 1,000,000 documents matching the prod size distribution.

## Responsibilities

- Idempotent seed: skip if target count already present (`--force` to rewrite).
- Document shape (from `contracts/`):
  - `_id`: zero-padded 7-digit string, `"0000001"`..`"1000000"`.
  - Fields: `ReqId`, `CalculatorFileNm`, `CalculatorVersion`, `SkipCalculation`, `Input`, `SuccessExitCodeList`, `ReqClass`.
  - `Input` size buckets: 5 KB (10 %), 16 KB (30 %), 44 KB (40 %), 58 KB (20 %) — random base64 so compression is realistic.
- Bulk insert with ordered=false batches of 1,000.
- Progress log every 10,000 docs; final summary with count, total bytes, elapsed.
- Works against any backend (Mongo VM, Cosmos RU, DocumentDB) via connection string.

## Inputs

- Connection string, target doc count (default 1,000,000), batch size, RNG seed.

## Outputs

- Populated `bmt_db.calc_input` collection.
- `seeder/runs/<backend>-seed-summary.json` with count, size distribution, duration.

## Dependencies

- `contracts/` for document model and size-bucket constants.
- `mongo-setup/` (target backend must be reachable and indexes created).

## To be added

- `Seeder.csproj` (.NET 8 console app).
- `Program.cs` with `System.CommandLine` parsing.
- `InputDocFactory.cs` (size bucket sampling, random payload).
- `BulkSeeder.cs` (batched inserts with retry on transient errors).
- `README` usage examples once implemented.
