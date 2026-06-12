"""CLI: python -m analysis {summarize|compare} ..."""

from __future__ import annotations

import argparse
import sys

from .compare import compare
from .summarize import summarize


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="analysis", description="BMT post-run analysis")
    sub = parser.add_subparsers(dest="command", required=True)

    p_sum = sub.add_parser("summarize", help="Analyze a single run directory.")
    p_sum.add_argument("run_dir", help="Path to runs/<run-id>/")

    p_cmp = sub.add_parser("compare", help="Compare multiple run directories.")
    p_cmp.add_argument("run_dirs", nargs="+", help="Paths to runs/<run-id>/ ...")

    args = parser.parse_args(argv)

    if args.command == "summarize":
        summarize(args.run_dir)
        return 0
    if args.command == "compare":
        compare(args.run_dirs)
        return 0

    parser.print_help()
    return 2


if __name__ == "__main__":
    sys.exit(main())
