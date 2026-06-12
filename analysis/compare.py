"""Cross-run comparison: one scenario across multiple backends."""

from __future__ import annotations

from collections import defaultdict
from pathlib import Path

from .summarize import PERCENTILES, analyze


def compare(run_dirs: list[str]) -> dict[str, str]:
    """Builds a comparison markdown table per scenario across the given runs.

    Writes runs/_compare/<scenario>.md for each scenario present and returns a
    map of scenario -> output path.
    """
    if not run_dirs:
        raise ValueError("compare requires at least one run directory")

    reports = [analyze(Path(d)) for d in run_dirs]

    by_scenario: dict[str, list] = defaultdict(list)
    for r in reports:
        by_scenario[r.scenario].append(r)

    # _compare lives under the common runs/ root of the first run dir.
    runs_root = Path(run_dirs[0]).resolve().parent
    compare_dir = runs_root / "_compare"
    compare_dir.mkdir(parents=True, exist_ok=True)

    outputs: dict[str, str] = {}
    for scenario, group in by_scenario.items():
        md = _render_scenario_table(scenario, group)
        out = compare_dir / f"{scenario}.md"
        out.write_text(md, encoding="utf-8")
        outputs[scenario] = str(out)
        print(f"comparison written: {out}")

    return outputs


def _render_scenario_table(scenario: str, group: list) -> str:
    lines = [
        f"# Backend comparison — scenario {scenario}",
        "",
    ]

    for op in ("find", "insert", "delete"):
        lines += [
            f"## {op}",
            "",
            "| backend | count | errors | "
            + " | ".join(f"p{p}" for p in PERCENTILES)
            + " | max | ops/s |",
            "|---|---:|---:|" + "---:|" * (len(PERCENTILES) + 2),
        ]
        for r in sorted(group, key=lambda x: x.backend):
            stat = next((s for s in r.op_stats if s.op == op), None)
            if stat is None:
                continue
            pcts = " | ".join(f"{stat.percentiles_ms[p]:.2f}" for p in PERCENTILES)
            ops_s = r.summary.get("OpsPerSecond", "")
            lines.append(
                f"| {r.backend} | {stat.count} | {stat.errors} | {pcts} | {stat.max_ms:.2f} | {ops_s} |"
            )
        lines.append("")

    lines += ["## Headline", "", "| backend | ops/s | tasks/s | error rate |", "|---|---:|---:|---:|"]
    for r in sorted(group, key=lambda x: x.backend):
        s = r.summary
        lines.append(
            f"| {r.backend} | {s.get('OpsPerSecond', '')} | "
            f"{s.get('TasksPerSecond', '')} | {s.get('ErrorRate', '')} |"
        )

    return "\n".join(lines) + "\n"
