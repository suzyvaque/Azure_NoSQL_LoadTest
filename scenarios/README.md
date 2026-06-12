# scenarios/

Declarative scenario definitions consumed by `loadgen/`. One JSON file per scenario.

## Scenario schema

```json
{
  "name": "S2",
  "description": "Prod peak hour: 1099 jobs / 484755 tasks",
  "durationMinutes": 60,
  "jobsPerHour": 1099,
  "tasksPerJob": 441,
  "arrival": "poisson",
  "workers": 500,
  "poolSize": 500,
  "distribution": "uniform",
  "idRange": [1, 1000000],
  "thinkMsMin": 50,
  "thinkMsMax": 250,
  "writeConcern": "w1",
  "warmupMinutes": 5,
  "warmupFractionOfTarget": 0.10
}
```

## Required files

| File | Jobs/hr | Tasks/job | Workers | Notes |
|---|---:|---:|---:|---|
| `warmup.json` | — | — | 50 | 5 min @ 10 % of S2 |
| `S1.json` | 2,062 | 97 | 200 | many small jobs |
| `S2.json` | 1,099 | 441 | 500 | **prod peak; primary KPI** |
| `S2-zipf.json` | 1,099 | 441 | 500 | `distribution: zipfian`, `s=1.1` |
| `S3.json` | 89 | 491 | 100 | fat jobs |
| `S2-burst.json` | 3,300 | 441 | 1000 | 30 min |
| `S2-stress.json` | ramp | 441 | 2000 | ramp until saturation |

## Inputs / Outputs

- **In**: human edits.
- **Out**: read by `loadgen/` at start; copied into `runs/<run-id>/config.json` (frozen).

## Dependencies

- `contracts/ScenarioConfig.cs` must deserialize these files.

## To be added

- The seven JSON files listed above.
- `schema.json` (JSON Schema) for editor validation and CI checking.
- A `validate.sh` that runs the JSON Schema check across all files.
