"""Single-run analysis: tail latencies, throughput time series, and a markdown report."""

from __future__ import annotations

import json
import math
from dataclasses import dataclass, field
from pathlib import Path

import numpy as np
import pandas as pd

# The loadgen samples 10% of operations into raw-sample.csv.
SAMPLE_RATE = 0.10
OPS = ("find", "insert", "delete")
PERCENTILES = (50, 90, 95, 99, 99.9)


@dataclass
class OpStats:
    op: str
    count: int
    errors: int
    percentiles_ms: dict[float, float]
    max_ms: float


@dataclass
class RunReport:
    run_id: str
    backend: str
    scenario: str
    summary: dict
    op_stats: list[OpStats] = field(default_factory=list)
    error_breakdown: dict[str, int] = field(default_factory=dict)
    throughput_csv: str | None = None


def load_raw(run_dir: Path) -> pd.DataFrame:
    """Loads runs/<run-id>/raw-sample.csv into a typed DataFrame."""
    csv_path = run_dir / "raw-sample.csv"
    if not csv_path.exists():
        raise FileNotFoundError(f"raw-sample.csv not found in {run_dir}")

    df = pd.read_csv(
        csv_path,
        dtype={"op": "category", "success": "int8", "err": "string"},
    )
    # Guard against partial final lines from an interrupted run.
    df = df.dropna(subset=["ts_us", "latency_us"])
    df["ts_us"] = df["ts_us"].astype("int64")
    df["latency_us"] = df["latency_us"].astype("int64")
    return df


def load_summary(run_dir: Path) -> dict:
    path = run_dir / "summary.json"
    if not path.exists():
        return {}
    return json.loads(path.read_text(encoding="utf-8"))


def _op_stats(df: pd.DataFrame, op: str) -> OpStats:
    sub = df[df["op"] == op]
    if sub.empty:
        return OpStats(op, 0, 0, {p: math.nan for p in PERCENTILES}, math.nan)

    latencies_ms = sub["latency_us"].to_numpy() / 1000.0
    pcts = {p: float(np.percentile(latencies_ms, p)) for p in PERCENTILES}
    errors = int((sub["success"] == 0).sum())
    return OpStats(
        op=op,
        count=int(len(sub)),
        errors=errors,
        percentiles_ms=pcts,
        max_ms=float(latencies_ms.max()),
    )


def _error_breakdown(df: pd.DataFrame) -> dict[str, int]:
    failed = df[df["success"] == 0]
    if failed.empty:
        return {}
    codes = failed["err"].fillna("unknown").replace("", "unknown")
    return codes.value_counts().to_dict()


def _throughput_series(df: pd.DataFrame) -> pd.DataFrame:
    """Estimated ops/sec in 1-second buckets (sample count / SAMPLE_RATE)."""
    if df.empty:
        return pd.DataFrame(columns=["second", "op", "ops_per_sec"])

    t0 = df["ts_us"].min()
    sec = ((df["ts_us"] - t0) // 1_000_000).astype("int64")
    grouped = (
        df.assign(second=sec)
        .groupby(["second", "op"], observed=True)
        .size()
        .reset_index(name="sampled")
    )
    grouped["ops_per_sec"] = grouped["sampled"] / SAMPLE_RATE
    return grouped[["second", "op", "ops_per_sec"]]


def analyze(run_dir: Path) -> RunReport:
    run_dir = Path(run_dir)
    df = load_raw(run_dir)
    summary = load_summary(run_dir)

    report = RunReport(
        run_id=summary.get("RunId", run_dir.name),
        backend=summary.get("Backend", "unknown"),
        scenario=summary.get("ScenarioName", "unknown"),
        summary=summary,
        op_stats=[_op_stats(df, op) for op in OPS],
        error_breakdown=_error_breakdown(df),
    )

    # Persist throughput time series next to the report for charting / inspection.
    ts = _throughput_series(df)
    ts_path = run_dir / "throughput.csv"
    ts.to_csv(ts_path, index=False)
    report.throughput_csv = str(ts_path)

    _render_report(run_dir, report)
    _render_charts(run_dir, df, ts)
    return report


def _render_report(run_dir: Path, report: RunReport) -> None:
    try:
        from jinja2 import Environment, FileSystemLoader, select_autoescape

        env = Environment(
            loader=FileSystemLoader(str(Path(__file__).parent)),
            autoescape=select_autoescape(enabled_extensions=()),
            trim_blocks=True,
            lstrip_blocks=True,
        )
        template = env.get_template("report_template.md.j2")
        md = template.render(report=report, percentiles=PERCENTILES)
    except Exception:
        md = _render_report_fallback(report)

    (run_dir / "report.md").write_text(md, encoding="utf-8")


def _render_report_fallback(report: RunReport) -> str:
    lines = [
        f"# BMT run report — {report.run_id}",
        "",
        f"- Backend: **{report.backend}**",
        f"- Scenario: **{report.scenario}**",
        "",
        "## Latency (ms)",
        "",
        "| op | count | errors | " + " | ".join(f"p{p}" for p in PERCENTILES) + " | max |",
        "|---|---:|---:|" + "---:|" * (len(PERCENTILES) + 1),
    ]
    for s in report.op_stats:
        pcts = " | ".join(f"{s.percentiles_ms[p]:.2f}" for p in PERCENTILES)
        lines.append(f"| {s.op} | {s.count} | {s.errors} | {pcts} | {s.max_ms:.2f} |")
    if report.error_breakdown:
        lines += ["", "## Errors by code", ""]
        for code, n in report.error_breakdown.items():
            lines.append(f"- `{code}`: {n}")
    return "\n".join(lines) + "\n"


def _render_charts(run_dir: Path, df: pd.DataFrame, ts: pd.DataFrame) -> None:
    try:
        import matplotlib

        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
    except Exception:
        return

    charts_dir = run_dir / "charts"
    charts_dir.mkdir(parents=True, exist_ok=True)

    # Throughput over time.
    if not ts.empty:
        fig, ax = plt.subplots(figsize=(10, 4))
        for op, g in ts.groupby("op", observed=True):
            ax.plot(g["second"], g["ops_per_sec"], label=op)
        ax.set_xlabel("second")
        ax.set_ylabel("ops/sec (est.)")
        ax.set_title("Throughput")
        ax.legend()
        fig.tight_layout()
        fig.savefig(charts_dir / "throughput.png", dpi=120)
        plt.close(fig)

    # Latency distribution per op.
    if not df.empty:
        fig, ax = plt.subplots(figsize=(10, 4))
        for op in OPS:
            sub = df[df["op"] == op]
            if not sub.empty:
                ax.hist(sub["latency_us"] / 1000.0, bins=100, histtype="step", label=op)
        ax.set_xlabel("latency (ms)")
        ax.set_ylabel("count (sampled)")
        ax.set_title("Latency distribution")
        ax.legend()
        fig.tight_layout()
        fig.savefig(charts_dir / "latency_hist.png", dpi=120)
        plt.close(fig)


def summarize(run_dir: str) -> RunReport:
    """Public entry: analyze one run directory and write report.md + charts."""
    report = analyze(Path(run_dir))
    print(f"report written: {Path(run_dir) / 'report.md'}")
    for s in report.op_stats:
        p99 = s.percentiles_ms.get(99, float('nan'))
        p999 = s.percentiles_ms.get(99.9, float('nan'))
        print(f"  {s.op:7s} n={s.count:>8d} errors={s.errors:>6d} "
              f"p99={p99:7.2f}ms p99.9={p999:7.2f}ms max={s.max_ms:7.2f}ms")
    return report
