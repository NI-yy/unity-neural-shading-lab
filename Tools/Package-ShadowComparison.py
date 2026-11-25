"""Validate paired captures and repeated GPU timings; keep missing values missing."""
import argparse
import csv
import hashlib
import json
import shutil
import struct
import subprocess
from pathlib import Path
from statistics import median

ROOT = Path(__file__).resolve().parents[1]
VIEWS = ('initial', 'shadow-stress')
MARKERS = ('PlayerLoop', 'Draw Main Light Shadowmap', 'DrawOpaqueObjects', 'DrawTransparentObjects', 'DrawDepthNormalPrepass', 'SSAO', 'ftm_gpu_ms')


def read_json(p):
    return json.loads(p.read_text(encoding='utf-8-sig'))


def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


def write_csv(p, rows):
    with p.open('w', newline='', encoding='utf-8') as f:
        w = csv.DictWriter(f, fieldnames=rows[0])
        w.writeheader()
        w.writerows(rows)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--suffix', default='20261107')
    args = parser.parse_args()
    output = ROOT / 'Benchmarks/ShadowComparison'
    images = ROOT / 'Benchmarks/Captures/ShadowComparison/Comparison'
    images.mkdir(parents=True, exist_ok=True)
    selected, captures, builds, orders, reports = [], {}, {}, [], {}
    for algorithm in ('PCF', 'PCSS'):
        phase = f'{algorithm.lower()}-comparison-{args.suffix}'
        base = ROOT / 'Benchmarks/Candidates' / phase
        builds[algorithm] = (base / 'build-info.txt').read_text(encoding='utf-8-sig').strip()
        orders.extend(read_json_line for line in (base / 'run-order.jsonl').read_text(encoding='utf-8-sig').splitlines() if (read_json_line := dict(algorithm=algorithm, **json.loads(line))))
        with (base / 'summary.csv').open(encoding='utf-8-sig', newline='') as f:
            selected.extend(dict(algorithm=algorithm, **r) for r in csv.DictReader(f) if r['marker'] in MARKERS)
        for view in VIEWS:
            for run in range(1, 4):
                rows = [r for r in selected if r['algorithm'] == algorithm and r['view'] == view and int(r['run']) == run and r['marker'] == 'PlayerLoop']
                assert len(rows) == 1 and int(rows[0]['valid']) >= 999 and int(rows[0]['valid']) + int(rows[0]['missing']) == 1000, (algorithm, view, run)
                directory = base / view / 'reference' / f'run-{run}'
                assert read_json(directory / 'execution.json')['playerExitCode'] in (0, 2)
                paths = list((directory / 'unity').glob('*/summary.json'))
                assert len(paths) == 1
                report = read_json(paths[0])
                assert report['buildInfo'].strip() == builds[algorithm]
                for key, expected in dict(warmupFrames=300, requestedSamples=1000, collectedSamples=1000, simulationRate=60, seed=12345, vSyncCount=0, renderScale=1, width=1920, height=1080, graphicsApi='Direct3D12', developmentBuild=True).items():
                    assert report[key] == expected, (key, report[key])
                info = read_json(paths[0].parent / 'candidate-info.json')
                assert info['view'] == view and info['variant'] == 'reference'
                assert info['sunShadows'] == 'Soft' and info['disabledRenderers'] == 0 and not info['disabledFeatures']
                reports[algorithm, view, run] = report
            folder = ROOT / 'Benchmarks/Captures/Candidates' / phase / view / 'reference'
            states = list(folder.glob('*/capture-state.json'))
            executions = list(folder.glob('execution-*.json'))
            assert len(states) == len(executions) == 1
            assert read_json(executions[0])['playerExitCode'] == 0
            state = read_json(states[0])
            assert state['frameCount'] == 310 and abs(state['simulationTime'] - 5.15) < 1e-6
            png = states[0].parent / 'capture.png'
            assert struct.unpack('>II', png.read_bytes()[16:24]) == (1920, 1080)
            dest = images / algorithm / f'{view}.png'
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(png, dest)
            captures[algorithm, view] = dict(state=state, sha256=sha(dest), image=dest.relative_to(ROOT).as_posix(), source=png.relative_to(ROOT).as_posix(), execution=executions[0].relative_to(ROOT).as_posix())
    shared = ('unity', 'project_settings_sha256', 'quality_settings_sha256', 'graphics_settings_sha256', 'scripting_backend', 'scene_dependency_hash', 'pipeline_dependency_hash')
    parsed = {a: dict(line.split('=', 1) for line in value.splitlines()) for a, value in builds.items()}
    assert all(parsed['PCF'][k] == parsed['PCSS'][k] for k in shared)
    assert parsed['PCSS']['main_light_shadow_filter'] == 'PCSS'
    for view in VIEWS:
        assert captures['PCF', view]['state'] == captures['PCSS', view]['state']
        configurations = [r['configuration'] for (a, v, n), r in reports.items() if v == view]
        assert all(c == configurations[0] for c in configurations)
    players = {'PCF': ROOT / 'Builds/PCF-Baseline-20261107', 'PCSS': ROOT / 'Builds/WindowsDevelopment'}
    player_hashes = {}
    for a, directory in players.items():
        player_hashes[a] = {str(p.relative_to(directory).as_posix()): sha(p) for p in (directory / 'NeuralShadingLab.exe', directory / 'NeuralShadingLab_Data/Managed/Assembly-CSharp.dll', directory / 'NeuralShadingLab_Data/Managed/Unity.RenderPipelines.Universal.Runtime.dll')}
    runtime = read_json(output / 'runtime-identity.json')
    assert runtime['identicalTypeFieldMethodIL']
    for a in ('PCF', 'PCSS'):
        assert runtime[a]['assemblySha256'].lower() == player_hashes[a]['NeuralShadingLab_Data/Managed/Assembly-CSharp.dll']
    shader = ROOT / 'UnityProject/Packages/com.unity.render-pipelines.universal/ShaderLibrary/NeuralLabPCSS.hlsl'
    assert sha(shader) == parsed['PCSS']['main_light_shadow_filter_source_sha256']
    write_csv(output / 'comparison-timings.csv', selected)
    aggregates = []
    for view in VIEWS:
        for marker in MARKERS:
            group = {a: [r for r in selected if r['algorithm'] == a and r['view'] == view and r['marker'] == marker] for a in ('PCF', 'PCSS')}
            for a, rows in group.items():
                assert len(rows) == 3
                aggregates.append(dict(view=view, marker=marker, algorithm=a, median_of_run_medians_ms=median(float(r['median_ms']) for r in rows), min_run_median_ms=min(float(r['median_ms']) for r in rows), max_run_median_ms=max(float(r['median_ms']) for r in rows), median_of_run_p95_ms=median(float(r['p95_ms']) for r in rows), valid=sum(int(r['valid']) for r in rows), missing=sum(int(r['missing']) for r in rows)))
    write_csv(output / 'comparison-aggregate.csv', aggregates)
    manifest = dict(suffix=args.suffix, builds=builds, playerHashes=player_hashes, pcssSourceSha256=sha(shader), captures=[dict(algorithm=a, view=v, **r) for (a,v),r in captures.items()], runOrder=sorted(orders, key=lambda r:r['utc']), timing='Player-side GPU Usage binary log; Editor disconnected; instrumented Development; no PresentMon', aggregation='median of three per-run medians; per-run p95 preserved separately', missingPolicy='Nonpositive GPU times remain missing. No interpolation or substitution between sources. Profiler matches exceeding 0.05 ms or 1% of neighbour gap are rejected and retained as missing.', pcss=dict(angularDiameterDegrees=.53, blockerSamples=32, filterComparisonSamples=32, maxSearchDistanceMeters=50, maxSearchRadiusTexels=32, maxFilterRadiusTexels=32, blockerBiasMeters=.01))
    (output / 'comparison-manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    ffmpeg = shutil.which('ffmpeg')
    assert ffmpeg, 'ffmpeg required for comparison sheets'
    for view in VIEWS:
        inputs = [images / a / f'{view}.png' for a in ('PCF', 'PCSS')]
        # Fixed final-frame sRGB PNGs, side-by-side PCF left / PCSS right.
        overview = '[0:v]scale=960:540[a];[1:v]scale=960:540[b];[a][b]hstack=inputs=2[out]'
        subprocess.run([ffmpeg, '-v', 'error', '-y', '-i', str(inputs[0]), '-i', str(inputs[1]), '-filter_complex', overview, '-map', '[out]', '-frames:v', '1', str(images / f'{view}-pair.png')], check=True)
        # Shadow-stress crop: right wall and its roof shadow; initial: central village.
        x,y,w,h = (1320,400,600,360) if view == 'shadow-stress' else (600,330,600,360)
        crop = f'[0:v]crop={w}:{h}:{x}:{y}[a];[1:v]crop={w}:{h}:{x}:{y}[b];[a][b]hstack=inputs=2[out]'
        subprocess.run([ffmpeg, '-v', 'error', '-y', '-i', str(inputs[0]), '-i', str(inputs[1]), '-filter_complex', crop, '-map', '[out]', '-frames:v', '1', str(images / f'{view}-crop.png')], check=True)
    print(json.dumps(aggregates, indent=2))


if __name__ == '__main__':
    main()
