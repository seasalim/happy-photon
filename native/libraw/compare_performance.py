#!/usr/bin/env python3
"""Compare paired isolated LibRaw harness reports and enforce the 10% rule."""

from __future__ import annotations

import argparse
import json
import statistics
from pathlib import Path


LIMIT = 1.10


def by_configuration(report: dict) -> dict[str, list[dict]]:
    grouped: dict[str, list[dict]] = {}
    for measurement in report["Measurements"]:
        grouped.setdefault(measurement["Configuration"], []).append(measurement)
    for values in grouped.values():
        values.sort(key=lambda item: item["Sample"])
    return grouped


def native_by_configuration(report: dict) -> dict[str, list[dict]]:
    native = report.get("NativeMemory")
    if not native or not native.get("AcceptedGate"):
        raise RuntimeError("accepted native memory measurements are absent")
    return by_configuration({"Measurements": native["Measurements"]})


def metric(baseline: list[dict], candidate: list[dict], name: str) -> dict:
    pairs = []
    for before, after in zip(baseline, candidate, strict=True):
        original = before[name]
        ratio = after[name] / original if original > 0 else None
        pairs.append({"sample": before["Sample"], "baseline": original,
                      "candidate": after[name], "ratio": ratio})
    comparable = [pair["ratio"] for pair in pairs if pair["ratio"] is not None]
    median_ratio = statistics.median(comparable) if comparable else None
    repeatable = (median_ratio is not None and median_ratio > LIMIT and
                  sum(ratio > LIMIT for ratio in comparable) >= 2)
    return {"pairs": pairs, "median_ratio": median_ratio,
            "regression_percent": None if median_ratio is None else (median_ratio - 1) * 100,
            "repeatable_regression": repeatable}


def percent(value: float | None) -> str:
    return "n/a" if value is None else f"{value:+.3f}%"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--baseline", required=True, type=Path)
    parser.add_argument("--candidate", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    baseline_report = json.loads(args.baseline.read_text(encoding="utf-8-sig"))
    candidate_report = json.loads(args.candidate.read_text(encoding="utf-8-sig"))
    for field in ("Schema", "Rid", "Fixture", "SamplingIntervalMilliseconds"):
        if baseline_report[field] != candidate_report[field]:
            raise RuntimeError(f"paired harness {field} mismatch")
    if baseline_report["Runtime"] != "baseline" or candidate_report["Runtime"] != "candidate":
        raise RuntimeError("paired harness runtime identities are invalid")
    if baseline_report["Schema"] != 2:
        raise RuntimeError("paired harness schema does not include native memory evidence")
    baseline_metric = baseline_report["NativeMemory"]["Metric"]
    if candidate_report["NativeMemory"]["Metric"] != baseline_metric:
        raise RuntimeError("native memory metric mismatch")
    baseline = by_configuration(baseline_report)
    candidate = by_configuration(candidate_report)
    native_baseline = native_by_configuration(baseline_report)
    native_candidate = native_by_configuration(candidate_report)
    if baseline.keys() != candidate.keys():
        raise RuntimeError("paired harness configuration mismatch")
    if native_baseline.keys() != native_candidate.keys() or baseline.keys() != native_baseline.keys():
        raise RuntimeError("native memory configuration mismatch")
    comparisons = []
    memory_blocked = False
    elapsed_flagged = False
    for configuration in sorted(baseline):
        before, after = baseline[configuration], candidate[configuration]
        if len(before) != 3 or len(after) != 3:
            raise RuntimeError("each paired configuration requires exactly three samples")
        for left, right in zip(before, after, strict=True):
            identity = ("Width", "Height", "Bits", "Channels", "Bytes")
            if any(left[field] != right[field] for field in identity):
                raise RuntimeError(f"decoded output shape mismatch for {configuration}")
        elapsed = metric(before, after, "ElapsedMilliseconds")
        managed_delta = metric(before, after, "PeakPrivateDeltaBytes")
        managed_absolute = metric(before, after, "PeakPrivateBytes")
        native_before, native_after = native_baseline[configuration], native_candidate[configuration]
        if len(native_before) != 3 or len(native_after) != 3:
            raise RuntimeError("each native configuration requires exactly three samples")
        for left, right in zip(native_before, native_after, strict=True):
            identity = ("Width", "Height", "Bits", "Channels", "Bytes")
            if any(left[field] != right[field] for field in identity):
                raise RuntimeError(f"native decoded output shape mismatch for {configuration}")
        native_memory = metric(native_before, native_after, "PeakProcessBytes")
        elapsed_flagged |= elapsed["repeatable_regression"]
        memory_blocked |= native_memory["repeatable_regression"]
        comparisons.append({"configuration": configuration, "elapsed": elapsed,
                            "native_peak_memory": {"metric": baseline_metric,
                                **native_memory},
                            "managed_host_memory_context": {
                                "accepted_gate": False,
                                "peak_private_delta": managed_delta,
                                "peak_private_absolute": managed_absolute}})
    status = ("investigation-required" if memory_blocked else
              "accepted-elapsed-flagged" if elapsed_flagged else "accepted")
    result = {"schema": 2, "rid": baseline_report["Rid"], "threshold_percent": 10,
              "baseline_version": baseline_report["Version"],
              "candidate_version": candidate_report["Version"],
              "gates": {
                  "native_peak_memory": {
                      "fatal": True,
                      "repeatable_regression": memory_blocked,
                  },
                  "elapsed": {
                      "fatal": False,
                      "flagged_for_review": elapsed_flagged,
                  },
              },
              "comparisons": comparisons,
              "status": status}
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"Performance comparison: rid={result['rid']} status={status} "
          f"threshold={result['threshold_percent']}%")
    for comparison in comparisons:
        elapsed = comparison["elapsed"]
        memory = comparison["native_peak_memory"]
        print(f"  {comparison['configuration']}: "
              f"elapsed={percent(elapsed['regression_percent'])} "
              f"flagged_for_review={str(elapsed['repeatable_regression']).lower()}; "
              f"native_peak_memory={percent(memory['regression_percent'])} "
              f"fatal_regression={str(memory['repeatable_regression']).lower()}")
    print(f"Gates: native_peak_memory_fatal_regression="
          f"{str(memory_blocked).lower()} "
          f"elapsed_flagged_for_review={str(elapsed_flagged).lower()}")
    if memory_blocked:
        raise SystemExit("repeatable native peak-memory regression exceeds 10%; investigate")


if __name__ == "__main__":
    main()
