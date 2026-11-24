"""Match Editor-connected Unity GPU Usage frames to independent PresentMon presents."""
import bisect
import csv
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "Benchmarks" / "Issue3" / "connected" / "reference"
METRICS = ("profiler_playerloop_ms", "profiler_shadow_ms", "pm_gpu_busy_ms", "pm_gpu_time_ms")


def read_csv(path):
    with path.open(newline="", encoding="utf-8-sig") as stream:
        return list(csv.DictReader(stream))


def write_csv(path, rows):
    with path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=rows[0].keys())
        writer.writeheader()
        writer.writerows(rows)


def valid(value):
    try:
        value = float(value)
        return value if math.isfinite(value) and value > 0 else None
    except (TypeError, ValueError):
        return None


def percentile(values, p):
    values = sorted(values)
    if not values:
        return ""
    position = (len(values) - 1) * p
    lo, hi = math.floor(position), math.ceil(position)
    return values[lo] + (values[hi] - values[lo]) * (position - lo)


def main():
    profiler = read_csv(DATA / "repeat-frame-times.csv")
    pm = sorted(read_csv(DATA / "presentmon.csv"), key=lambda row: float(row["CPUStartQPCTime"]))
    pm_times = [float(row["CPUStartQPCTime"]) for row in pm]
    if pm_times != sorted(pm_times):
        raise ValueError("PresentMon QPC is not sorted")
    stderr = (DATA / "presentmon.stderr.txt").read_text(encoding="utf-16")
    if "overflowed present events" in stderr:
        raise ValueError("ETW present overflow")
    summary = []
    for run in (1, 2, 3):
        source = [row for row in profiler if int(row["run"]) == run]
        if len(source) != 1000 or any("Gathered" not in row["state"] for row in source):
            raise ValueError(f"Incomplete GPU Usage: run {run}")
        times = [float(row["frame_start_ms"]) for row in source]
        if times != sorted(times):
            raise ValueError(f"Profiler QPC is not sorted: run {run}")
        rows = []
        for sample, profile in enumerate(source):
            start = times[sample]
            end = times[sample + 1] if sample + 1 < 1000 else start + 20
            lo = bisect.bisect_left(pm_times, start)
            hi = bisect.bisect_left(pm_times, end)
            present = pm[lo] if hi - lo == 1 else None
            rows.append(dict(run=run, sample=sample, profiler_frame=profile["frame"],
                             profiler_qpc_ms=start, present_count=hi - lo,
                             present_qpc_ms=present["CPUStartQPCTime"] if present else "",
                             profiler_state=profile["state"],
                             profiler_playerloop_ms=profile["PlayerLoop"],
                             profiler_shadow_ms=profile["Draw Main Light Shadowmap"],
                             pm_gpu_busy_ms=present["GPUBusy"] if present else "",
                             pm_gpu_time_ms=present["GPUTime"] if present else ""))
        write_csv(DATA / f"matched-run-{run}.csv", rows)
        for metric in METRICS:
            values = [value for row in rows if (value := valid(row[metric])) is not None]
            summary.append(dict(condition="reference", run=run, metric=metric, requested=1000,
                                valid=len(values), missing=1000-len(values),
                                median_ms=percentile(values, .5), p95_ms=percentile(values, .95),
                                over_3_ms=sum(value > 3 for value in values),
                                ambiguous_presents=sum(row["present_count"] != 1 for row in rows),
                                both_over_3_ms=sum(valid(row["profiler_playerloop_ms"]) is not None
                                                   and valid(row["profiler_playerloop_ms"]) > 3
                                                   and valid(row["pm_gpu_busy_ms"]) is not None
                                                   and valid(row["pm_gpu_busy_ms"]) > 3 for row in rows)))
    write_csv(DATA / "summary.csv", summary)


if __name__ == "__main__":
    main()
