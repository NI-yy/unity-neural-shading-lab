"""Summarize independent PresentMon captures aligned to Unity's QPC sample interval."""
import csv
import glob
import json
import math
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1] / "Benchmarks" / "Issue3"


def percentile(values, p):
    values = sorted(values)
    if not values:
        return ""
    offset = (len(values) - 1) * p
    a = math.floor(offset)
    b = math.ceil(offset)
    return values[a] + (values[b] - values[a]) * (offset - a)


def positive(value):
    try:
        number = float(value)
        return number if math.isfinite(number) and number > 0 else None
    except (TypeError, ValueError):
        return None


def stats(rows, column):
    values = [v for row in rows if (v := positive(row[column])) is not None]
    return len(values), len(rows) - len(values), percentile(values, .5), percentile(values, .95), sum(v > 3 for v in values)


def main():
    output = []
    for condition in ("reference", "shadow4096"):
        for mode in ("off", "on"):
            for repeat in range(1, 4):
                folder = ROOT / condition / f"{mode}-{repeat}"
                unity_paths = list((folder / "unity").glob("*/frames.csv"))
                if len(unity_paths) != 1:
                    raise ValueError(f"Expected one Unity frame CSV in {folder}")
                with unity_paths[0].open(newline="", encoding="utf-8-sig") as file:
                    unity = list(csv.DictReader(file))
                with (folder / "presentmon.csv").open(newline="", encoding="utf-8-sig") as file:
                    pm = list(csv.DictReader(file))
                if len(unity) != 1000 or not pm:
                    raise ValueError(f"Incomplete capture: {folder}")
                # FrameTimingManager frame_start_timestamp is QPC in 100 ns units.
                # PresentMon --qpc_time_ms reports the same QPC clock in ms.
                start = int(unity[0]["frame_start_timestamp"]) / 10000
                end = int(unity[-1]["frame_start_timestamp"]) / 10000 + 20
                selected = [row for row in pm if start <= float(row["CPUStartQPCTime"]) <= end]
                if not 950 <= len(selected) <= 1050:
                    raise ValueError(f"Present count does not match Unity window: {folder}: {len(selected)}")
                stderr = (folder / "presentmon.stderr.txt").read_text(encoding="utf-16")
                if "overflowed present events" in stderr:
                    raise ValueError(f"ETW overflow: {folder}")
                info = json.loads(next((folder / "unity").glob("*/summary.json")).read_text(encoding="utf-8-sig"))
                expected_hash = "89d118024ad1b7b67baa1c4e88e53ad7" if condition == "reference" else "8f85983f8df686e99e506509fde1d48e"
                if f"pipeline_dependency_hash={expected_hash}" not in info["buildInfo"]:
                    raise ValueError(f"Wrong pipeline build: {folder}")
                if info["graphicsApi"] != "Direct3D12" or not info["developmentBuild"] or info["width"] != 1920 or info["height"] != 1080:
                    raise ValueError(f"Wrong Unity condition: {folder}")
                if ("NEURAL_LAB_GPU_PROFILER_ENABLED True" in (folder / "player.log").read_text(encoding="utf-8")) != (mode == "on"):
                    raise ValueError(f"Profiler flag mismatch: {folder}")
                for source, rows, columns in (("presentmon", selected, ("GPUBusy", "GPUTime")), ("unity", unity, ("gpu_ms",))):
                    for metric in columns:
                        valid, missing, median, p95, over3 = stats(rows, metric)
                        output.append(dict(condition=condition, profiler_flag=mode, repeat=repeat, source=source, metric=metric, total=len(rows), valid=valid, missing=missing, median_ms=median, p95_ms=p95, over_3_ms=over3))
                with (folder / "presentmon-sample-window.csv").open("w", newline="", encoding="utf-8") as file:
                    writer = csv.DictWriter(file, fieldnames=pm[0].keys())
                    writer.writeheader()
                    writer.writerows(selected)
    with (ROOT / "summary.csv").open("w", newline="", encoding="utf-8") as file:
        writer = csv.DictWriter(file, fieldnames=output[0].keys())
        writer.writeheader()
        writer.writerows(output)


if __name__ == "__main__":
    main()
