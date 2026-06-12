"""Post-run analysis for the Azure NoSQL BMT.

Computes tail latencies from the 10% raw sample CSV and renders per-run and cross-run
comparison reports.

CLI:
    python -m analysis summarize <run-dir>
    python -m analysis compare <run-dir> [<run-dir> ...]
"""

__all__ = ["summarize", "compare"]
