# docs/

Human-facing documentation: test plan, runbook, and results template.

## Responsibilities

- `test-plan.md`: full workload design, scenario matrix, SLO gates, derivation of targets from prod numbers (135 tasks/s S2 peak, ×3 burst, stress).
- `runbook.md`: step-by-step procedure for an operator to execute the 21-run matrix (warmup, R1–R7 × 3 backends), including pre-flight checks and abort criteria.
- `results-template.md`: structure of the final comparison deliverable (headline R3 number per backend, headroom from R6/R7, cost per 1 M tasks).
- `decisions.md`: log of design decisions (w:1 only, single-node Mongo, fixed RU/s, C# daemon).

## Inputs

- Prod workload numbers (S1/S2/S3), BMT reference observations, environment specs.

## Outputs

- The four markdown files above.

## Dependencies

- None at runtime. Should stay in sync with `scenarios/` and `loadgen/` behavior.

## To be added

- `test-plan.md`
- `runbook.md`
- `results-template.md`
- `decisions.md`
