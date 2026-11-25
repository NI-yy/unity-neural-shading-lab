"""Freeze the measured PCF baseline and full-resolution screenshots for comparison."""
import argparse
import csv
import hashlib
import json
import shutil
import struct
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VIEWS = ("initial", "shadow-stress")
MARKERS = ("PlayerLoop", "Draw Main Light Shadowmap", "DrawOpaqueObjects", "DrawTransparentObjects", "DrawDepthNormalPrepass", "SSAO", "ftm_gpu_ms")


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def utc(value):
    return datetime.strptime(value, "%Y%m%dT%H%M%S%fZ").replace(tzinfo=timezone.utc)


def write_csv(path, rows):
    with path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=rows[0], lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--phase", default="pcf-baseline-20261107")
    args = parser.parse_args()
    source = ROOT / "Benchmarks/Candidates" / args.phase
    output = ROOT / "Benchmarks/ShadowComparison"
    pictures = ROOT / "Benchmarks/Captures/ShadowComparison/PCF"
    output.mkdir(parents=True, exist_ok=True)
    pictures.mkdir(parents=True, exist_ok=True)
    manifest = read_json(source / "filter-manifest.json")
    baseline_path = output / "pcf-baseline.json"
    if baseline_path.exists() and read_json(baseline_path)["phase"] != args.phase:
        raise ValueError("A different PCF baseline is already frozen")
    with (source / "summary.csv").open(newline="", encoding="utf-8-sig") as handle:
        summary = list(csv.DictReader(handle))
    selected = [dict(algorithm="PCF", **row) for row in summary if row["marker"] in MARKERS]
    for view in VIEWS:
        for run in range(1, 4):
            primary = [row for row in selected if row["view"] == view and int(row["run"]) == run and row["marker"] == "PlayerLoop"]
            if len(primary) != 1 or primary[0]["valid"] != "1000" or primary[0]["missing"] != "0":
                raise ValueError(f"Incomplete primary GPU capture: {view} run {run}")
            directory = source / view / "reference" / f"run-{run}"
            if read_json(directory / "execution.json")["playerExitCode"] not in (0, 2):
                raise ValueError(f"Player execution failed: {directory}")
            info_files = list((directory / "unity").glob("*/candidate-info.json"))
            if len(info_files) != 1:
                raise ValueError("Ambiguous Player result")
            info = read_json(info_files[0])
            if info["sunShadows"] != "Soft" or info["sunSoftQuality"] != "UsePipelineSettings" or info["disabledRenderers"] or info["disabledFeatures"]:
                raise ValueError(f"Unexpected diagnostic changes: {directory}")
            report = read_json(info_files[0].parent / "summary.json")
            if report["buildInfo"].splitlines() != manifest["buildInfo"].splitlines():
                raise ValueError(f"Build changed within baseline: {directory}")
    write_csv(output / "pcf-timings.csv", selected)
    captures = []
    for view in VIEWS:
        folder = ROOT / "Benchmarks/Captures/Candidates" / args.phase / view / "reference"
        candidates = []
        for state_file in folder.glob("*/capture-state.json"):
            timestamp = utc(state_file.parent.name)
            for execution_file in folder.glob("execution-*.json"):
                execution = read_json(execution_file)
                start = utc(execution_file.stem[len("execution-"):])
                end = datetime.fromisoformat(execution["completedUtc"].replace("Z", "+00:00"))
                if start <= timestamp <= end and execution["playerExitCode"] == 0:
                    candidates.append((state_file, execution_file))
        if len(candidates) != 1:
            raise ValueError(f"Expected one successful capture: {view}")
        state_file, execution_file = candidates[0]
        state = read_json(state_file)
        if state["frameCount"] != 310 or abs(state["simulationTime"] - 5.15) > 1e-6 or not state["method"].startswith("CaptureScreenshotAsTexture"):
            raise ValueError(f"Unexpected screenshot time or method: {view}")
        png = state_file.parent / "capture.png"
        header = png.read_bytes()[:24]
        if header[:8] != b"\x89PNG\r\n\x1a\n" or struct.unpack(">II", header[16:24]) != (1920, 1080):
            raise ValueError(f"Invalid PNG dimensions: {png}")
        target = pictures / f"{view}.png"
        if target.exists() and sha(target) != sha(png):
            raise ValueError(f"Refusing to replace a different frozen screenshot: {target}")
        shutil.copy2(png, target)
        captures.append(dict(view=view, image=target.relative_to(ROOT).as_posix(), sha256=sha(target),
                             source=png.relative_to(ROOT).as_posix(), execution=execution_file.relative_to(ROOT).as_posix(),
                             playerExitCode=0, state=state))
    frozen_player = ROOT / "Builds/PCF-Baseline-20261107/NeuralShadingLab.exe"
    frozen_assembly = frozen_player.parent / "NeuralShadingLab_Data/Managed/Assembly-CSharp.dll"
    player_manifest = output / "pcf-player-files.csv"
    if sha(frozen_assembly).upper() != manifest["assemblySha256"].upper():
        raise ValueError("Frozen Player differs from measured build")
    baseline = dict(phase=args.phase, filter=manifest, primaryGpuRuns=6, requestedFramesPerRun=1000, missingPrimaryGpuFrames=0,
                    timings="Benchmarks/ShadowComparison/pcf-timings.csv", sourceSummarySha256=sha(source / "summary.csv"),
                    frozenPlayer=frozen_player.relative_to(ROOT).as_posix(), frozenPlayerFileManifest=player_manifest.relative_to(ROOT).as_posix(),
                    frozenPlayerFileManifestSha256=sha(player_manifest), overview="Benchmarks/Captures/ShadowComparison/PCF/overview.png",
                    captures=captures)
    baseline_path.write_text(json.dumps(baseline, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    for view in VIEWS:
        rows = [row for row in selected if row["view"] == view and row["marker"] == "PlayerLoop"]
        print(view, "GPU median range ms", min(float(row["median_ms"]) for row in rows), max(float(row["median_ms"]) for row in rows),
              "GPU p95 range ms", min(float(row["p95_ms"]) for row in rows), max(float(row["p95_ms"]) for row in rows))
    print("Frozen PCF: six complete GPU runs and two successful 1920x1080 screenshots.")


if __name__ == "__main__":
    main()
