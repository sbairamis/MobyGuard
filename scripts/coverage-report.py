#!/usr/bin/env python3
"""Parse Cobertura XML coverage output and print a summary table (plus GitHub Actions Job Summary)."""

import argparse
import os
import sys
import xml.etree.ElementTree as ET


def badge_color(pct: float) -> str:
    if pct >= 85:
        return "brightgreen"
    if pct >= 75:
        return "green"
    if pct >= 60:
        return "yellow"
    return "red"


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate code coverage summary from Cobertura XML.")
    parser.add_argument(
        "--cobertura",
        default="TestResults/coverage.cobertura.xml",
        help="Path to coverage.cobertura.xml",
    )
    parser.add_argument(
        "--min-line-coverage",
        type=float,
        default=0.0,
        help="Minimum required line coverage percentage (0-100).",
    )
    args = parser.parse_args()

    if not os.path.isfile(args.cobertura):
        print(f"Error: Cobertura file not found at '{args.cobertura}'", file=sys.stderr)
        return 1

    tree = ET.parse(args.cobertura)
    root = tree.getroot()

    lines_covered = int(root.attrib.get("lines-covered", "0"))
    lines_valid = int(root.attrib.get("lines-valid", "0"))
    branches_covered = int(root.attrib.get("branches-covered", "0"))
    branches_valid = int(root.attrib.get("branches-valid", "0"))

    line_pct = (lines_covered / lines_valid * 100.0) if lines_valid else 100.0
    branch_pct = (branches_covered / branches_valid * 100.0) if branches_valid else 100.0

    by_file: dict[str, list[int]] = {}
    for cls in root.findall(".//class"):
        filename = cls.attrib.get("filename", "")
        if "/MobyGuard/" in filename:
            rel_name = filename.split("/MobyGuard/", 1)[1]
        elif "\\MobyGuard\\" in filename:
            rel_name = filename.split("\\MobyGuard\\", 1)[1].replace("\\", "/")
        else:
            rel_name = os.path.basename(filename)

        lines = cls.findall(".//line")
        cov = sum(1 for line in lines if int(line.attrib.get("hits", "0")) > 0)
        tot = len(lines)
        entry = by_file.setdefault(rel_name, [0, 0])
        entry[0] += cov
        entry[1] += tot

    print("=" * 78)
    print(f"CODE COVERAGE SUMMARY")
    print("=" * 78)
    print(f"  Line Coverage   : {line_pct:6.2f}%  ({lines_covered}/{lines_valid} lines)")
    print(f"  Branch Coverage : {branch_pct:6.2f}%  ({branches_covered}/{branches_valid} branches)")
    print("-" * 78)
    print(f"  {'File':54s} {'Lines':>11s} {'Coverage':>9s}")
    print("-" * 78)

    for rel_name, (cov, tot) in sorted(by_file.items()):
        pct = (cov / tot * 100.0) if tot else 100.0
        print(f"  {rel_name:54s} {cov:5d}/{tot:<5d} {pct:8.1f}%")

    print("=" * 78)

    summary_path = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary_path:
        color = badge_color(line_pct)
        badge_url = f"https://img.shields.io/badge/coverage-{line_pct:.1f}%25-{color}"
        with open(summary_path, "a", encoding="utf-8") as sf:
            sf.write("## Code Coverage Summary\n\n")
            sf.write(f"![Code Coverage]({badge_url})\n\n")
            sf.write("| Metric | Coverage | Covered / Total |\n")
            sf.write("| :--- | :--- | :--- |\n")
            sf.write(f"| **Line Coverage** | **{line_pct:.2f}%** | `{lines_covered} / {lines_valid}` |\n")
            sf.write(f"| **Branch Coverage** | **{branch_pct:.2f}%** | `{branches_covered} / {branches_valid}` |\n\n")
            sf.write("<details>\n<summary>Coverage Breakdown by File</summary>\n\n")
            sf.write("| File | Lines | Coverage |\n")
            sf.write("| :--- | ---: | ---: |\n")
            for rel_name, (cov, tot) in sorted(by_file.items()):
                pct = (cov / tot * 100.0) if tot else 100.0
                sf.write(f"| `{rel_name}` | `{cov}/{tot}` | `{pct:.1f}%` |\n")
            sf.write("\n</details>\n")

    if args.min_line_coverage > 0 and line_pct < args.min_line_coverage:
        print(
            f"Error: Line coverage {line_pct:.2f}% is below minimum threshold {args.min_line_coverage:.2f}%",
            file=sys.stderr,
        )
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
