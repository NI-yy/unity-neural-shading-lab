"""Summarize Player-only candidate ablations without filling missing GPU values."""
import argparse
import bisect
import csv
import json
import math
from pathlib import Path


def read(path):
    with path.open(newline="", encoding="utf-8-sig") as handle:
        return list(csv.DictReader(handle))


def write(path, rows):
    with path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=rows[0], lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)


def positive(value):
    try:
        x = float(value)
        return x if math.isfinite(x) and x > 0 else None
    except (ValueError, TypeError):
        return None


def percentile(values, fraction):
    values = sorted(values)
    if not values:
        return ""
    point = (len(values) - 1) * fraction
    a, b = math.floor(point), math.ceil(point)
    return values[a] + (values[b] - values[a]) * (point - a)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--phase", default="screening-v2")
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    base = args.root / "Benchmarks" / "Candidates" / args.phase
    state = json.loads((base / "source-state.json").read_text(encoding="utf-8-sig"))
    result = []
    for exported in sorted(base.glob("*/*/repeat-frame-times.csv")):
        directory = exported.parent
        view, variant = directory.relative_to(base).parts
        profiles = read(exported)
        markers = [key for key in profiles[0] if key not in ("run", "frame", "state", "frame_start_ms")]
        for run_dir in sorted(directory.glob("run-*")):
            run = int(run_dir.name.split("-")[-1])
            if (run_dir / "execution.json").exists():
                execution = json.loads((run_dir / "execution.json").read_text(encoding="utf-8-sig"))
                if execution["playerExitCode"] not in (0, 2):
                    raise ValueError(f"Failed Player execution: {run_dir}")
            unity_dirs = list((run_dir / "unity").glob("*/summary.json"))
            if len(unity_dirs) != 1:
                raise ValueError(f"Expected exactly one Player result: {run_dir}")
            report = json.loads(unity_dirs[0].read_text(encoding="utf-8-sig"))
            info = json.loads((unity_dirs[0].parent / "candidate-info.json").read_text())
            if info["view"] != view or info["variant"] != variant:
                raise ValueError(f"Candidate identity mismatch: {run_dir}")
            if not report["developmentBuild"] or report["graphicsApi"] != "Direct3D12" or (report["width"], report["height"]) != (1920, 1080):
                raise ValueError(f"Player condition mismatch: {run_dir}")
            ftm = read(unity_dirs[0].parent / "frames.csv")
            if len(ftm) != 1000:
                raise ValueError(f"Short FrameTimingManager series: {run_dir}")
            source = [row for row in profiles if int(row["run"]) == run and positive(row["frame_start_ms"]) is not None]
            times = [float(row["frame_start_ms"]) for row in source]
            has_pm = (run_dir / "presentmon.csv").exists()
            if state["independentTiming"] and not has_pm:
                raise ValueError(f"Required independent capture missing: {run_dir}")
            pm = sorted(read(run_dir / "presentmon.csv"), key=lambda row: float(row["CPUStartQPCTime"])) if has_pm else []
            pm_times = [float(row["CPUStartQPCTime"]) for row in pm]
            stderr = (run_dir / "presentmon.stderr.txt").read_text(encoding="utf-16") if has_pm else ""
            if "overflowed present events" in stderr or "ETW events were lost" in stderr:
                raise ValueError(f"ETW overflow: {run_dir}")
            matched, used = [], set()
            qpcs = [int(row["frame_start_timestamp"]) / 10000 for row in ftm]
            if times != sorted(times) or qpcs != sorted(qpcs):
                raise ValueError(f"Unsorted QPC: {run_dir}")
            for i, frame in enumerate(ftm):
                start = qpcs[i]
                j = bisect.bisect_left(times, start)
                choices = [v for v in (j - 1, j) if 0 <= v < len(times)]
                best = min(choices, key=lambda v: abs(times[v] - start))
                error = abs(times[best] - start)
                gap = min(abs(times[j] - times[best]) for j in (best - 1, best + 1) if 0 <= j < len(times))
                # Require a bijection and a timestamp difference smaller than both
                # 0.05 ms and 1% of the nearest neighbouring profiler-frame gap.
                aligned = error <= .05 and error <= gap * .01 and best not in used
                if aligned:
                    used.add(best)
                profile = source[best]
                end = qpcs[i + 1] if i < 999 else start + 20
                a, b = bisect.bisect_left(pm_times, start), bisect.bisect_left(pm_times, end)
                present = pm[a] if b - a == 1 else None
                row = dict(sample=i, qpc_ms=start, profiler_qpc_ms=times[best], qpc_error_ms=error, qpc_gap_fraction=error/gap, state=profile["state"] if aligned else "AlignmentRejected", present_count=b-a,
                           ftm_gpu_ms=frame["gpu_ms"], pm_gpu_busy_ms=present["GPUBusy"] if present else "")
                row.update({marker: profile[marker] if aligned and "Gathered" in profile["state"] else "" for marker in markers})
                matched.append(row)
            write(run_dir / "matched-frames.csv", matched)
            series = {marker: [row[marker] for row in matched] for marker in markers + ["ftm_gpu_ms", "pm_gpu_busy_ms"]}
            window = pm[bisect.bisect_left(pm_times, qpcs[0]):bisect.bisect_left(pm_times, qpcs[-1] + 20)]
            if has_pm:
                series["pm_gpu_busy_window_ms"] = [row["GPUBusy"] for row in window]
            else:
                del series["pm_gpu_busy_ms"]
            for marker, raw in series.items():
                values = [v for value in raw if (v := positive(value)) is not None]
                result.append(dict(view=view, variant=variant, run=run, marker=marker, requested=len(raw), valid=len(values), missing=len(raw)-len(values),
                                   median_ms=percentile(values, .5), p95_ms=percentile(values, .95), over_3_ms=sum(v > 3 for v in values),
                                   present_ambiguous=sum(row["present_count"] != 1 for row in matched) if has_pm else "not_recorded",
                                   alignment_rejected=sum(row["state"] == "AlignmentRejected" for row in matched),
                                   max_qpc_error_ms=max(row["qpc_error_ms"] for row in matched),
                                   max_qpc_gap_fraction=max(row["qpc_gap_fraction"] for row in matched)))
    if not result:
        raise ValueError("No exported captures")
    write(base / "summary.csv", result)
    print(base / "summary.csv")


if __name__ == "__main__":
    main()
