# Issue #1: Fantasy Kingdom GPU Profiler実測（2026-11-04）

> 2026-11-06追記: Issue #2の[Profilerなし対照測定と影設定比較](issue-2-gpu-tail.md)では、同じDevelopmentビルドの自動計測に0の欠測が残り、有効部分には3 ms超がなかった。以下のGPU Usageの長い裾を通常実行時のGPU負荷と断定しない。根本原因は未確定。

## 結論と範囲

Unity Profilerを実際のDevelopment Playerに接続し、GPU Usageの階層を収録した。DX12・同一静止視点で300フレームのウォームアップ後、1000フレームを3回収録した。各回ともGPUプロファイル取得状態は `Gathered, Enabled, Supported` が1000件、欠測0件。元の自動FrameTimingManager/ProfilerRecorder系列は引き続き `incomplete` であり、Profilerの結果でその0を埋めていない。以下の値は**GPU Profilerを有効にしたDevelopment Playerのベースライン**であり、Release Playerの性能値には置き換えられない。

## 条件と証拠

- Unity 6000.3.12f1、URP 17.3.0、Fantasy Kingdom 1.1.0、Windows 11 (10.0.26200)、RTX 4070 SUPER、Direct3D 12、Windows x64 Mono Development Player。
- 元の `Assets/Scenes/Demo.unity` とAPV、Desktop品質、1920×1080、Render Scale 1、Linear、MSAAなし、STP/TAA/動的解像度/VSync/FPS上限なし。初期Physical Cameraを固定。操作UIとカメラ・品質変更等の制御はビルド時に無効化。固定シミュレーション刻み1/60秒。画面を表示し、Playerを前面にして計測。EditorのGame Viewは計測対象ではない。
- Playerログに `NEURAL_LAB_GPU_PROFILER_ENABLED True` と `NEURAL_LAB_PROFILE_READY`。Editorログに `NEURAL_LAB_PROFILER_CONNECTED ... gpu=True`、`NEURAL_LAB_GPU_MODULE_ACTIVE True`、`NEURAL_LAB_REPEAT_RESULT` が3回記録された。GUIのProfiler接続先は `DESKTOP-9F7CQKC - Unity Neural Shading Lab`、GPU UsageでPlayerLoopと描画パスを確認した。
- [フレーム系列](../Benchmarks/GpuProfiler/repeat-frame-times.csv) と [集計](../Benchmarks/GpuProfiler/repeat-summary.csv) をGit対象とする。Unityの `.raw` 3件（各約255 MB）と診断ログは `LocalData/ProfilerTrials/` にローカル保存し、サンプル資産や生成PlayerとともにGitから除外する。フレーム系列は各収録のフレーム300〜1299のみ。`state` と空欄を残し、欠測を0や他系列で補間しない。
- 既存のローカル描画確認画像 `Benchmarks/Captures/20261103T054353509Z/capture.png` と今回のPlayer表示で、初期視点にマゼンタの材質や明らかな欠けは見られなかった。ピクセル単位の同一性確認と他視点の検査は未実施。

## 反復測定

中央値と95パーセンタイルは、各回1000フレームのGPU時間を昇順に並べ、位置 `(n-1) × p` を線形補間した値（ms）。0、NaN、非有限値、Profilerの未取得状態は有効値としない。全1000件が有効な回だけを `complete` とする。以下はすべて `complete`。

| 回 | 有効 / 要求 | 欠測 | PlayerLoop 中央値 | PlayerLoop p95 | RenderGraph 中央値 | RenderGraph p95 |
|---|---:|---:|---:|---:|---:|---:|
| 1 | 1000 / 1000 | 0 | 1.6315 | 3.8988 | 1.5530 | 3.7832 |
| 2 | 1000 / 1000 | 0 | 1.6370 | 3.9845 | 1.5545 | 3.8874 |
| 3 | 1000 / 1000 | 0 | 1.6320 | 3.9486 | 1.5540 | 3.8165 |

PlayerLoop中央値の最大差は0.0055 ms（約0.34%）。p95の最大差は0.0857 ms（約2.2%）。中央値だけでなく上側の長い裾も再現した。`PlayerLoop > 3 ms` は各回264、291、281フレームあり、特に不透明描画と深度・法線プリパスに長いフレームがある。これらを削除して統計を作り直していない。測定は静止視点で、サンプル内のVFX・植生等のすべての時間変動を固定したことまでは保証しない。

ProfilerのGPU Usageマーカーは階層型。以下は `ExecuteRenderGraph` 直下の深さ8のマーカーを各フレームで同名分だけ合計した値で、親子マーカーを重複加算していない。`PlayerLoop` の中央値で割った比率は規模感だけを示し、各パスの中央値の合計を全体時間とは扱わない。

| GPUパス | 中央値（3回、ms） | p95（3回、ms） | 全体中央値に対する概算 |
|---|---:|---:|---:|
| 不透明描画 `DrawOpaqueObjects` | 0.580 / 0.581 / 0.580 | 2.536 / 2.544 / 2.543 | 約36% |
| 深度・法線プリパス `DrawDepthNormalPrepass` | 0.326 / 0.326 / 0.326 | 2.075 / 2.084 / 2.086 | 約20% |
| メインライト影 `Draw Main Light Shadowmap` | 0.326 / 0.326 / 0.326 | 0.330 / 0.329 / 0.330 | 約20% |
| Bloom | 0.057 / 0.057 / 0.057 | 0.061 / 0.061 / 0.061 | 約3.5% |
| SSAO | 0.051 / 0.051 / 0.051 | 0.052 / 0.052 / 0.052 | 約3.1% |
| 透明描画 `DrawTransparentObjects` | 0.043 / 0.043 / 0.043 | 0.046 / 0.046 / 0.046 | 約2.6% |
| UberPost | 0.030 / 0.030 / 0.030 | 0.031 / 0.031 / 0.030 | 約1.8% |

不透明描画は材質評価だけでなくジオメトリ・描画命令・ラスタライズ等を含む。材質計算だけのGPU時間はこの計測から分離できない。深度・法線と影の合計中央値は約0.652 msで全体中央値の約40%に相当するが、これらはシェーディング関数を小さなMLPへ置き換えるだけでは消えない。SSAOとBloomはそれぞれ小さく、推論・中間バッファ等の追加負荷を回収できる根拠がない。**この固定視点の測定だけでは、ニューラル近似を進める高負荷の材質処理を選ばない。** 次に候補を探すなら、別視点・照明で材質評価が重くなる場面を確認し、対象マーカーを分離した上で通常の簡略化とも比較する。このIssueでは学習・ニューラル実装を行っていない。

GPU Profiler自体の計測負荷は同じPlayerでオン・オフを対にした有効なGPU系列が得られておらず、定量化できない。全3回は同じProfiler設定なので互いには比較できるが、Release値やProfiler無効時の絶対時間へ外挿しない。

## 自動計測の欠測調査

既存のDX11/DX12 Release PlayerのFrameTimingManager系列は、各1000件中DX11が935/877、DX12が710/773件有効だった。追加のDX12 Development診断では、GPU Profiler起動時有効化なしが798/1000、起動時有効化ありでもFrameTimingManagerが807/1000、ProfilerRecorderが808/1000で `incomplete`。この起動時フラグのログはTrueだった。GPU Usageの記録をPlayerに接続し、GPUモジュールを有効化した場合に、別のProfiler系列では3回とも1000/1000取得できた。

807件のDevelopment診断では、FrameTimingManagerの0が193件すべて単独で現れ、隣り合う0の間隔は159/192区間で5サンプル。Recorderの0は192件で、FrameTimingManagerの0を1サンプル後ろにずらした集合と192件一致した。非同期に返る同じGPUクエリ結果を、2つのAPIが異なる読み出し時点で見ている可能性が高い。双方を独立した欠測補完源として扱えない。計測コードはタイムスタンプで重複排除し、後から得た正の値で同じタイムスタンプを更新し、末尾8フレーム待つ。`GetLatestTimings` は直近4件を読むため、さらに遅いクエリ結果を拾えていない可能性もある。ただしUnity内部のクエリ処理と返却タイミングは直接観測しておらず、**0になる根本原因は未確定**。焦点・ウィンドウ状態、GPU API、開発ビルドの違いだけでは欠測が解消しなかった。

別の初期診断では、Profilerに接続していてもGPU Usageモジュールを有効にする前の保存データにGPU階層がなかった。以後はGPUモジュールを明示的に有効化し、各フレームの `GetGpuStatisticsAvailabilityState` で `Gathered` を検証する。`ProfilerRecorder`のGPU時間0は引き続き0として生データに残す。旧自動系列の有効部分だけの中央値は正式なGPUベースラインとして採用しない。

Unityの[Frame Timing Manager説明](https://docs.unity3d.com/cn/6000.0/Manual/frame-timing-manager.html)はGPU結果に遅延があり、待っても常に値が得られるとは限らないと説明する。[GPU Usage Profiler](https://docs.unity3d.com/kr/6000.0/Manual/ProfilerGPU.html)はDevelopment Player接続でパス階層を調べられる。

## 再現手順

1. [ベースラインガイド](baseline-guide.md)に従ってサンプルを導入し、`Neural Shading Lab > Prepare Fantasy Kingdom Baseline` を実行する。元のDemoシーン、初期カメラと上記の設定を確認する。
2. Unity Editorを閉じ、リポジトリ直下から ` .\Tools\Run-Baseline.ps1 -Build -BuildOnly -Development ` を実行する。生成したPlayerとサンプル資産はGitへ追加しない。
3. Playerを表示して起動する。PowerShell例：

   ```powershell
   & .\Builds\WindowsDevelopment\NeuralShadingLab.exe -neuralLabProfile -neuralLabGpuProfile -force-d3d12 -screen-width 1920 -screen-height 1080
   ```

4. Unity Editorで `UnityProject/` を開き、`Window > Analysis > Profiler` を表示する。接続先をDevelopment Playerに設定し、GPU Usageモジュールを有効にする。リポジトリ付属メニュー `Neural Shading Lab > Connect Development Player Profiler` と `Enable GPU Usage Module` でも同じ設定を行える。GPU Usageに `PlayerLoop` と `ExecuteRenderGraph` が表示されることを先に確認する。
5. Editorメニュー `Neural Shading Lab > Capture Three GPU Repeats` を実行し、直後にPlayerを前面に戻して表示し続ける。各回でProfilerをクリアし、300フレームを捨てて1000フレームを収録する。Editorログの `NEURAL_LAB_REPEAT_RESULT` で各回の `gathered=1000 enabled_ungathered=0 disabled=0` を確認する。`attached-repeat-1.raw`〜`3.raw` は `LocalData/ProfilerTrials/` に保存される。
6. `Neural Shading Lab > Export Three GPU Repeats` を実行する。各 `.raw` の取得状態を読み、300〜1299フレームのGPU階層値を `Benchmarks/GpuProfiler/repeat-frame-times.csv` に出力する。`python Tools/Summarize-GpuProfiler.py` で `repeat-summary.csv` を再計算する。空欄・`incomplete` があればその回を比較から外すか取り直し、0を埋めない。取り直しも記録する。

この手順はGPUパスの比較用。Release Playerで自動計測を再試行する場合は `Run-Baseline.ps1` の通常モードを別に実行し、出力の `status` と0の件数を確認する。Development/Release、DX11/DX12、Profilerオン/オフの系列を混ぜない。

