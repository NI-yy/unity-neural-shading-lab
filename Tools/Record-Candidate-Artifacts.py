"""Record hashes, PNG dimensions, process exits, and verified measurement counts."""
import csv
import hashlib
import json
import struct
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "Benchmarks/Candidates"
PHASE = "confirmed-player-only"


def utc(value):
    return datetime.strptime(value, "%Y%m%dT%H%M%S%fZ").replace(tzinfo=timezone.utc)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    rows = []
    for png in sorted((ROOT / "Benchmarks/Captures/Candidates" / PHASE).glob("*/*/*/capture.png")):
        view, variant, run_id, _ = png.relative_to(ROOT / "Benchmarks/Captures/Candidates" / PHASE).parts
        timestamp = utc(run_id)
        matches = []
        for execution in png.parent.parent.glob("execution-*.json"):
            info = json.loads(execution.read_text(encoding="utf-8-sig"))
            start = utc(execution.stem[len("execution-"):])
            end = datetime.fromisoformat(info["completedUtc"].replace("Z", "+00:00"))
            if start <= timestamp <= end:
                matches.append(info)
        if len(matches) != 1:
            raise ValueError(f"Ambiguous capture execution: {png}")
        header = png.read_bytes()[:24]
        if header[:8] != b"\x89PNG\r\n\x1a\n":
            raise ValueError(f"Invalid PNG: {png}")
        width, height = struct.unpack(">II", header[16:24])
        if (width, height) != (1920, 1080):
            raise ValueError(f"Wrong resolution: {png}")
        rows.append(dict(view=view, variant=variant, run_id=run_id, path=png.relative_to(ROOT).as_posix(), width=width, height=height,
                         sha256=sha(png), execution_exit_code=matches[0]["playerExitCode"],
                         execution_log=(png.parent.parent / matches[0]["log"]).relative_to(ROOT).as_posix()))
    with (DATA / "captures.csv").open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=rows[0], lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)
    with (DATA / PHASE / "summary.csv").open(newline="", encoding="utf-8-sig") as handle:
        timing = list(csv.DictReader(handle))
    primary = [row for row in timing if row["marker"] == "PlayerLoop"]
    if len(primary) != 15 or any(row["valid"] != "1000" or row["missing"] != "0" for row in primary):
        raise ValueError("Expected 15 complete 1000-frame GPU Usage captures")
    sources = ["UnityProject/Assets/NeuralShadingLab/Runtime/BaselineRunner.cs", "UnityProject/Assets/NeuralShadingLab/Runtime/CandidateProbe.cs",
               "UnityProject/Assets/NeuralShadingLab/Editor/CandidateAudit.cs", "UnityProject/Assets/NeuralShadingLab/Editor/GpuRepeatExport.cs",
               "Tools/Run-Candidates.ps1", "Tools/Summarize-Candidates.py", "Tools/Record-Candidate-Artifacts.py",
               "UnityProject/Assets/NeuralShadingLab/LocalSample/BaselineDesktop.asset", "UnityProject/Assets/NeuralShadingLab/LocalSample/BaselineDesktopRenderer.asset",
               "UnityProject/Assets/NeuralShadingLab/Resources/NeuralLabBaseline.asset"]
    manifest = dict(primary_source="Player-side Unity GPU Usage binary logging, Editor disconnected", independent_timing_this_phase=False,
                    runs=15, requested_per_run=1000, complete_gpu_runs=15, missing_gpu_frames=0,
                    build_info=(DATA / PHASE / "build-info.txt").read_text(encoding="utf-8-sig"),
                    source_sha256={path: sha(ROOT / path) for path in sources},
                    capture_files=len(rows), capture_failed_process_exits=sum(row["execution_exit_code"] != 0 for row in rows))
    (DATA / "measurements-manifest.json").write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"Verified {len(primary)} complete GPU runs and {len(rows)} PNG artifacts; failed capture exits remain explicit.")


if __name__ == "__main__":
    main()
