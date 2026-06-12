# analysis/

Post-run analysis: precise tail latencies (p99.9) from raw CSV samples and cross-backend comparison reports.

## Responsibilities

- Parse `runs/<run-id>/raw-sample.csv` and compute:
  - p50, p90, p95, p99, p99.9, max per op type
  - ops/sec time series (1 s buckets)
  - error breakdown by code
- Cross-run comparison: given a set of `run-id`s, produce a single table + chart per scenario across backends.
- Emit a markdown report into `runs/<run-id>/report.md` and a combined `runs/_compare/<scenario>.md`.
- Optional: render PNG charts (matplotlib) for inclusion in the final report.

## Inputs

- `runs/<run-id>/raw-sample.csv` and `summary.json` from one or more runs.

## Outputs

- `runs/<run-id>/report.md`
- `runs/_compare/<scenario>.md`
- PNG charts under `runs/<run-id>/charts/`

## Dependencies

- Python 3.11 + `pandas`, `numpy`, `matplotlib` (declared in `requirements.txt`).
- Output of `loadgen/` runs.

## To be added

- `requirements.txt`
- `summarize.py` (single run)
- `compare.py` (multi-run, one scenario, many backends)
- `report_template.md.j2` (Jinja2 template)
- A small CLI: `python -m analysis summarize <run-dir>` / `python -m analysis compare <run-dirs...>`
