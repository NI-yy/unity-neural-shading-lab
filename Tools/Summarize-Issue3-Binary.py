"""Align Unity GPU Usage binary logs, FrameTimingManager and PresentMon by QPC."""
import bisect
import csv
import glob
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "Benchmarks" / "Issue3" / "binary"
METRICS = ("unity_gpu_ms", "profiler_playerloop_ms", "profiler_shadow_ms", "pm_gpu_busy_ms", "pm_gpu_time_ms")


def positive(value):
    try:
        number = float(value)
        return number if math.isfinite(number) and number > 0 else None
    except (TypeError, ValueError):
        return None


def percentile(values, fraction):
    values = sorted(values)
    if not values:
        return ""
    point = (len(values) - 1) * fraction
    a, b = math.floor(point), math.ceil(point)
    return values[a] + (values[b] - values[a]) * (point - a)


def read_csv(path):
    with path.open(newline="", encoding="utf-8-sig") as file:
        return list(csv.DictReader(file))


def write_csv(path, rows):
    with path.open("w", newline="", encoding="utf-8") as file:
        writer = csv.DictWriter(file, fieldnames=rows[0].keys())
        writer.writeheader()
        writer.writerows(rows)


def main():
    summary = []
    for condition in ("reference", "shadow4096"):
        folder = DATA / condition
        profiler = read_csv(folder / "repeat-frame-times.csv")
        for run in (1, 2, 3):
            run_dir = folder / f"run-{run}"
            frame_paths = list((run_dir / "unity").glob("*/frames.csv"))
            if len(frame_paths) != 1:
                raise ValueError(f"Expected one Unity frame series in {run_dir}")
            ftm = read_csv(frame_paths[0])
            pm = read_csv(run_dir / "presentmon.csv")
            source = [row for row in profiler if int(row["run"]) == run and positive(row["frame_start_ms"]) is not None]
            if len(ftm) != 1000 or len(source) < 1000:
                raise ValueError(f"Short trace: {run_dir}")
            if "overflowed present events" in (run_dir / "presentmon.stderr.txt").read_text(encoding="utf-16"):
                raise ValueError(f"PresentMon overflow: {run_dir}")
            report = json.loads(next((run_dir / "unity").glob("*/summary.json")).read_text(encoding="utf-8-sig"))
            expected = "89d118024ad1b7b67baa1c4e88e53ad7" if condition == "reference" else "8f85983f8df686e99e506509fde1d48e"
            if f"pipeline_dependency_hash={expected}" not in report["buildInfo"] or report["graphicsApi"] != "Direct3D12" or not report["developmentBuild"] or (report["width"], report["height"]) != (1920, 1080):
                raise ValueError(f"Condition mismatch: {run_dir}")
            if "NEURAL_LAB_BINARY_PROFILER" not in (run_dir / "player.log").read_text(encoding="utf-8"):
                raise ValueError(f"Profiler binary logging not enabled: {run_dir}")
            profiler_times = [float(row["frame_start_ms"]) for row in source]
            pm_times = [float(row["CPUStartQPCTime"]) for row in pm]
            ftm_times = [int(row["frame_start_timestamp"]) / 10000 for row in ftm]
            rows = []
            for i, row in enumerate(ftm):
                start = ftm_times[i]
                idx = bisect.bisect_left(profiler_times, start)
                candidates = [j for j in (idx - 1, idx) if 0 <= j < len(source)]
                best = min(candidates, key=lambda j: abs(profiler_times[j] - start))
                error_ms = abs(profiler_times[best] - start)
                if error_ms > .01:
                    raise ValueError(f"Profiler/FTM QPC mismatch {error_ms} ms: {run_dir} sample {i}")
                profile = source[best]
                if "Gathered" not in profile["state"]:
                    raise ValueError(f"GPU hierarchy not gathered: {run_dir} sample {i}")
                end = ftm_times[i + 1] if i < 999 else start + 20
                a, b = bisect.bisect_left(pm_times, start), bisect.bisect_left(pm_times, end)
                present = pm[a] if b - a == 1 else None
                rows.append(dict(sample=i, ftm_qpc_ms=start, profiler_qpc_ms=profiler_times[best], qpc_error_ms=error_ms,
                                 profiler_state=profile["state"], present_count=b-a, present_qpc_ms=present["CPUStartQPCTime"] if present else "",
                                 unity_gpu_ms=row["gpu_ms"], profiler_playerloop_ms=profile["PlayerLoop"],
                                 profiler_shadow_ms=profile["Draw Main Light Shadowmap"],
                                 profiler_opaque_ms=profile["DrawOpaqueObjects"], profiler_depth_ms=profile["DrawDepthNormalPrepass"],
                                 pm_gpu_busy_ms=present["GPUBusy"] if present else "", pm_gpu_time_ms=present["GPUTime"] if present else ""))
            write_csv(run_dir / "matched-frames.csv", rows)
            for metric in METRICS:
                values = [v for row in rows if (v := positive(row[metric])) is not None]
                summary.append(dict(condition=condition, run=run, metric=metric, requested=len(rows), valid=len(values), missing=len(rows)-len(values), median_ms=percentile(values, .5), p95_ms=percentile(values, .95), over_3_ms=sum(v > 3 for v in values), ambiguous_presents=sum(row["present_count"] != 1 for row in rows), max_qpc_error_ms=max(row["qpc_error_ms"] for row in rows)))
    write_csv(DATA / "summary.csv", summary)


if __name__ == "__main__":
    main()
