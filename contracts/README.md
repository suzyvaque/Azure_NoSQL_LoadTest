# contracts/

Shared C# class library: data models, constants, and conventions used by `seeder/` and `loadgen/`.

## Responsibilities

- POCOs for `CalcInputDoc` and `CalcOutputDoc` matching the prod schema.
- `Ids` helper: sequential numeric-string `_id` formatter (non-padded, e.g. `"1653"`); uniform and Zipfian samplers over `[1, N]`.
- `SizeBuckets`: enum + probability table (5K/16K/44K/58K @ 10/30/40/20 %).
- `ScenarioConfig`: deserialization model for files in `scenarios/`.
- `RunArtifacts`: paths and file-name conventions for `runs/<run-id>/`.
- Mongo collection/database name constants (`bmt_db`, `calc_input`, `calc_output`).

## Inputs

None at runtime. Consumed at compile time.

## Outputs

- NuGet-style class library (`Contracts.csproj`) referenced by `seeder/` and `loadgen/`.

## Dependencies

- `MongoDB.Bson` only (for `[BsonId]` attributes). No driver, no I/O.

## To be added

- `Contracts.csproj`. ✅
- `Models/CalcInputDoc.cs`, `Models/CalcOutputDoc.cs`. ✅
- `Ids.cs`, `SizeBuckets.cs`, `ScenarioConfig.cs`, `RunArtifacts.cs`, `Names.cs`. ✅
- Unit tests for samplers and ID formatting (xUnit). ✅ (`Contracts.Tests/`)

## Build & test

```
cd contracts/Contracts.Tests && dotnet test
```
