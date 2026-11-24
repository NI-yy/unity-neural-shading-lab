"""Recompute Issue #2 automatic GPU summaries; keep zero timings as missing."""
import csv
import json
import math
from pathlib import Path

DATA = Path(__file__).resolve().parents[1] / "Benchmarks" / "Issue2"
CONDITIONS = ("reference-before", "shadow4096", "reference-after")


def percentile(values, p):
    values = sorted(values)
    if not values:
        return None
    x = (len(values) - 1) * p
    lo, hi = math.floor(x), math.ceil(x)
    return values[lo] + (values[hi] - values[lo]) * (x - lo)


def summarize():
    result = []
    for condition in CONDITIONS:
        for run in range(1, 4):
            directory = DATA / condition / f"run-{run}"
            report = json.loads((directory / "summary.json").read_text(encoding="utf-8-sig"))
            with (directory / "frames.csv").open(newline="", encoding="utf-8-sig") as handle:
                frames = list(csv.DictReader(handle))
            if len(frames) != report["collectedSamples"]:
                raise ValueError(f"{directory}: collected count differs from summary.json")
            raw = [float(frame["gpu_ms"]) for frame in frames]
            valid = [ms for ms in raw if math.isfinite(ms) and ms > 0]
            if len(valid) != report["validGpuSamples"]:
                raise ValueError(f"{directory}: valid count differs from summary.json")
            if len(frames) != 1000 or report["status"] != "incomplete":
                raise ValueError(f"{directory}: unexpected capture length or status")
            result.append({
                "condition": condition,
                "run": run,
                "run_id": report["runId"],
                "status": report["status"],
                "requested": report["requestedSamples"],
                "collected": len(frames),
                "valid": len(valid),
                "missing": len(raw) - len(valid),
                "median_valid_ms": f"{percentile(valid, .5):.6f}",
                "p95_valid_ms": f"{percentile(valid, .95):.6f}",
                "valid_over_3ms": sum(ms > 3 for ms in valid),
            })
    return result


if __name__ == "__main__":
    rows = summarize()
    output = DATA / "automatic-summary.csv"
    with output.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=rows[0].keys(), lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)
    print(output)
