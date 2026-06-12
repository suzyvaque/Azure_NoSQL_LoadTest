# runs/

Per-run artifacts. Contents are **git-ignored** (large, machine-generated); only this README is tracked.

## Layout

```
runs/
  <run-id>/
    config.json        # frozen effective config (from loadgen)
    loadgen.log        # structured logs
    raw-sample.csv     # 10% op samples
    summary.json       # totals, error counts, host info
    prom-snapshot.txt  # /metrics snapshot at end of run
    report.md          # produced by analysis/
    charts/*.png       # produced by analysis/
  _compare/
    <scenario>.md      # cross-backend comparison
```

## Run ID convention

`YYYY-MM-DD_<scenario>_<backend>[_<variant>]`
Examples: `2026-06-13_S2_mongo`, `2026-06-13_S2_cosmosru_zipf`.

## Responsibilities

- Storage only. No code lives here.
- Each producer (`loadgen/`, `analysis/`) writes into its run-id subfolder.

## Inputs / Outputs

- Written by `loadgen/` (live) and `analysis/` (post).
- Read by `analysis/` and humans.

## Dependencies

- None. Pure artifact store.
