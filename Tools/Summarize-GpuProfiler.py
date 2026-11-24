"""Summarize Unity GPU Usage captures without treating missing frames as zero."""
import csv
import math
import statistics
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
INPUT = ROOT / "Benchmarks" / "GpuProfiler" / "repeat-frame-times.csv"
OUTPUT = ROOT / "Benchmarks" / "GpuProfiler" / "repeat-summary.csv"


def percentile(values, fraction):
    values = sorted(values)
    offset = (len(values) - 1) * fraction
    lower = math.floor(offset)
    upper = math.ceil(offset)
    return values[lower] + (values[upper] - values[lower]) * (offset - lower)


with INPUT.open(newline="", encoding="utf-8-sig") as handle:
    frames = list(csv.DictReader(handle))
markers = [key for key in frames[0] if key not in ("run", "frame", "state")]
summary = []
for run in ("1", "2", "3"):
    rows = [row for row in frames if row["run"] == run]
    if len(rows) != 1000 or [int(row["frame"]) for row in rows] != list(range(300, 1300)):
        raise ValueError(f"run {run}: expected frames 300..1299 exactly once")
    gathered = sum("Gathered" in row["state"] for row in rows)
    for marker in markers:
        values = [float(row[marker]) for row in rows if "Gathered" in row["state"] and row[marker]]
        valid = len(values)
        if not all(math.isfinite(value) and value > 0 for value in values):
            raise ValueError(f"run {run}: invalid GPU value for {marker}")
        summary.append({
            "run": run,
            "marker": marker,
            "requested_frames": len(rows),
            "gathered_frames": gathered,
            "valid_marker_frames": valid,
            "missing_marker_frames": len(rows) - valid,
            "status": "complete" if valid == len(rows) else "incomplete",
            "median_gpu_ms": f"{statistics.median(values):.6f}" if values else "",
            "p95_gpu_ms": f"{percentile(values, 0.95):.6f}" if values else "",
        })
with OUTPUT.open("w", newline="", encoding="utf-8") as handle:
    writer = csv.DictWriter(handle, fieldnames=summary[0].keys(), lineterminator="\n")
    writer.writeheader()
    writer.writerows(summary)
print(OUTPUT)
