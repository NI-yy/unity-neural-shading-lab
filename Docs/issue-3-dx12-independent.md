# Issue #3: DX12独立計測と影4096の再評価（2026-11-06）

## 判断（Editor接続の追加計測を含む）

PresentMon 2.6.0のETWによるフレーム別 `GPUBusy` を同じDX12 Development Playerで取得した。Unity GPU Usageとは独立した計測で、測定区間の有効値は全12回で各1005〜1007件、欠測0、ETW present overflow警告なし。参照8192の `GPUBusy` p95はProfiler起動フラグがオフで2.278〜2.453 ms、オンで2.305〜2.405 msだった。3 ms超は6回計8件（計6032 present中）。Issue #1のUnity GPU Usageでは同じ参照条件の `PlayerLoop >3 ms` が3000フレーム中836件だった。この大きな長尾は**Player単独の独立計測では再現しない**。後述のEditor接続同時計測ではPresentMonの `GPUBusy` にも3 ms超が多数現れた。したがって接続収録中のGPU実行・スケジューリング負荷が実在する。ただし通常実行の固有負荷とはいえず、Unity GPU Usageの個々のパスへの時刻帰属の正確さも未確定。

PresentMonの `GPUTime` はGPU待ちを含む経過時間であり、参照8192でも3 ms超が各495〜628件あった。同じフレームの `GPUBusy` はほとんど3 ms以下だった。**3 msという閾値は指標を明記して解釈する**。PresentMon `GPUBusy`、PresentMon `GPUTime`、Unity `PlayerLoop`、FrameTimingManagerを同一のGPU時間として混ぜない。

影8192→4096では、独立 `GPUBusy` 中央値がProfilerフラグオフで1.860〜1.878→1.726〜1.737 ms、オンで1.883〜1.898→1.758〜1.777 msとなった。p95の改善は全ての組で安定せず、通常視点と別視点・別照明の画像に影境界の差がある。**4096は採用せず、8192を維持する。** PresentMonはパス別時刻を出さないが、後述のPlayer側GPU Usageでは影パスの短縮も測定した。ニューラル化の実装は行っていない。

## 条件と生データ

- Unity 6000.3.12f1、URP 17.3.0、RTX 4070 SUPER、Windows 11、DX12、Windows x64 Mono Development Player。Demoシーン初期カメラ静止視点、Desktop品質、1920×1080、Render Scale 1、VSync/FPS上限/動的解像度/TAA/STPなし。初期化10、ウォームアップ300、Unityサンプル1000、遅延排出8フレーム。実行中はPlayerを表示。
- 8192/4096で変えたのはローカルの `BaselineDesktop.asset` の `m_MainLightShadowmapResolution` のみ。各条件でPlayerを再ビルド。パイプライン依存ハッシュは8192が `89d118024ad1b7b67baa1c4e88e53ad7`、4096が `8f85983f8df686e99e506509fde1d48e`。計測後は8192のassetとビルドへ戻した。
- 各ビルドでProfiler起動フラグのオフ/オンを交互に3回ずつ実施。オンは `NEURAL_LAB_GPU_PROFILER_ENABLED True` をPlayerログで確認。この最初の12回ではEditor接続のGPU Usage収録は実施していない。
- [全12回のPresentMon CSV、Playerログ、Unityの生系列、集計](../Benchmarks/Issue3/) を保存。PresentMonはプロセスの全presentを記録する。`presentmon-sample-window.csv` はUnity `frames.csv` の最初と最後の `frame_start_timestamp`（QPC 100 ns）をPresentMon `CPUStartQPCTime`（QPC ms）へ変換し、最後に20 msを加えた区間のpresent。1 Unityフレームに1 presentとは限らないため1000件と厳密一致させず、1005〜1007件をそのまま数えた。起動・ロード区間を統計に入れない。`python Tools/Summarize-Issue3.py` で集計を再生成できる。
- 有効値は正の有限値のみ。`0`、空欄、`NA`は欠測で、元CSVに残し、補間しない。中央値とp95は有効値の昇順で `(n-1)p` の線形補間。Unity側の1000件には各回23〜153件の0欠測があり、その統計は有効部分のみ。全回のPlayer終了コード2はこの欠測を示す。

| 影 | Profilerフラグ | 回 | PresentMon GPUBusy 有効/欠測 | 中央値 ms | p95 ms | >3 ms | Unity有効/欠測 |
|---|---|---:|---:|---:|---:|---:|---:|
| 8192 | off | 1 | 1005/0 | 1.8779 | 2.3170 | 0 | 924/76 |
| 8192 | off | 2 | 1005/0 | 1.8642 | 2.4530 | 0 | 847/153 |
| 8192 | off | 3 | 1006/0 | 1.8604 | 2.2785 | 7 | 924/76 |
| 8192 | on | 1 | 1005/0 | 1.8979 | 2.4046 | 0 | 876/124 |
| 8192 | on | 2 | 1006/0 | 1.8828 | 2.3047 | 1 | 965/35 |
| 8192 | on | 3 | 1005/0 | 1.8825 | 2.3999 | 0 | 878/122 |
| 4096 | off | 1 | 1006/0 | 1.7270 | 2.1195 | 0 | 968/32 |
| 4096 | off | 2 | 1007/0 | 1.7263 | 2.1677 | 0 | 951/49 |
| 4096 | off | 3 | 1006/0 | 1.7373 | 2.2516 | 0 | 948/52 |
| 4096 | on | 1 | 1006/0 | 1.7771 | 2.2516 | 1 | 974/26 |
| 4096 | on | 2 | 1006/0 | 1.7581 | 2.3531 | 0 | 898/102 |
| 4096 | on | 3 | 1006/0 | 1.7763 | 2.1846 | 0 | 977/23 |

PresentMonの[公式メトリクス説明](https://github.com/GameTechDev/PresentMon/blob/main/README-CaptureApplication.md#metric-and-csv-column-definitions)では `GPUTime` は開始から終了までの経過時間、`GPUBusy` は実際にGPUがフレームを処理した時間。`GPUWait` も生CSVに残した。これはETWのフレーム帰属による推定で、GPUハードウェアタイムスタンプによる個々のURPパス時間ではない。

## 画質

初期カメラと光源の[Issue #2のローカル画像](../Benchmarks/Captures/Issue2/)は8192変更前後がピクセル完全一致、8192対4096のPSNRが32.730 dB。今回、カメラを世界Y軸に+7°、シーンのDirectional LightをローカルY軸に+25°回転した別条件で各1枚を撮影した。画像はGit対象外の `Benchmarks/Captures/Issue3/alternate-8192.png` と `alternate-4096.png` に保存。1920×1080の8-bit RGB sRGB符号値でPSNR **34.309 dB**。8192画像SHA-256 `e77cdd5bbad1129d931cd27dfeb94dee91e2abe8f3bea81fa41d3dd66f8d518a`、4096画像 `56bb9947f43d83dea6f922c7515362d443f235505be74f74f2d92aab2c340730`。影の輪郭と細部に差が見える。カメラと照明を同時に変えた1条件なので、どちらが差を強めたかは分離できない。動く視点・照明、複数シーンでの画質保証はしていない。画像はサンプル資産の再配布を避けてGitへ追加しない。

## 再現

1. [ベースラインガイド](baseline-guide.md)でサンプルを準備し、Unity Editorを閉じる。PresentMon 2.6.0の公式リリース `PresentMon-2.6.0-x64.exe` を取得する。計測時のコマンドは `--v2_metrics --qpc_time_ms --set_circular_buffer_size 32768 --process_name NeuralShadingLab.exe --timed 45 --terminate_after_timed --terminate_on_proc_exit`。ETW overflow警告が出た回は破棄する。
2. `m_MainLightShadowmapResolution: 8192` で `.\Tools\Run-Baseline.ps1 -Build -BuildOnly -Development`。`.\Tools\Run-Issue3.ps1 -Condition reference -PresentMonPath <exe>`。次に4096だけへ変更して同じビルド手順と `-Condition shadow4096` を実施。各回のPlayerは `-neuralLabBenchmark -force-d3d12 -screen-fullscreen 0 -screen-width 1920 -screen-height 1080`。Profilerオン回だけ `-neuralLabGpuProfile`。実行順はoff/onを交互に3回。Editor接続計測は後述。
3. 元のassetを戻して参照Playerを再ビルド。`python Tools/Summarize-Issue3.py` を実行し、ハッシュ・API・ビルド種別・解像度・Profilerフラグを検証して `summary.csv` を再生成する。
4. 別視点/照明の画像は、`NeuralLabBaseline.asset` のカメラrotationを `(-0.08285774, 0.86546593, -0.15260499, -0.46990963)`、`Demo.unity` の `Directional Light` の `m_LocalRotation` を `(0.20498897, -0.0903231, -0.12101388, 0.96704543)` にして、8192/4096をそれぞれ再ビルドし、`-neuralLabCapture` で300フレーム後に撮影。元のasset/sceneを復元する。PSNRは `ffmpeg -i alternate-8192.png -i alternate-4096.png -lavfi psnr -f null NUL`。

## 残る不確実性

参照8192ではUnity GPU UsageをEditor接続し `Gathered` を確認した同時PresentMonトレースを3回取得した。接続中の長尾は独立 `GPUBusy` にも現れたが、接続・収録のどの処理がGPU負荷を増やすか、Unityの個々のパスへ正確に帰属するかは未確定。PresentMonは影パス単独を分離できない。追加のPlayer側GPU Usageでは両解像度の影パスを各3回測定した。4096のEditor接続同時トレースはProfiler転送が停止して3回揃わず、完全に対称な接続比較には、転送量を制御した両解像度の再収録が必要。影の画質差が確認できるため、4096の採用を保留する。

## 追加計測：Player側Profiler記録とEditor接続（2026-11-07）

同じDX12 Development PlayerでProfilerの生ログをPlayer自身に保存し、PresentMonを同時実行した。Editorとの転送を伴わず、Unity GPU Usageの全1000フレームが各回 `Gathered`。Profilerの `frameStartTimeMs` とFrameTimingManagerのQPC開始時刻は各回1000/1000フレームで照合でき、最大差は0.004 ms。PresentMonは各フレームのQPC区間にpresentが1件だけある場合に対応づけた。0件または複数件は欠測とし、値0も補間しない。各条件3回、各回1000フレーム。生CSV、Playerログ、照合済みフレームと集計は[Benchmarks/Issue3/binary](../Benchmarks/Issue3/binary/)に保存。大容量のUnity `.raw` は `LocalData/Issue3Binary/` に保持し、Git対象外。

| 影 | 回 | Unity GPU Usage PlayerLoop 有効/欠測、中央値/p95 ms、>3件 | PresentMon GPUBusy 有効/欠測、中央値/p95 ms、>3件 | 影パス中央値 ms |
|---|---:|---|---|---:|
| 8192 | 1 | 1000/0、1.639/1.928、0 | 997/3、1.903/2.182、0 | 0.318 |
| 8192 | 2 | 1000/0、1.649/1.945、0 | 997/3、1.912/2.200、0 | 0.329 |
| 8192 | 3 | 1000/0、1.650/1.944、0 | 995/5、1.910/2.188、0 | 0.326 |
| 4096 | 1 | 1000/0、1.487/1.780、0 | 997/3、1.745/2.023、0 | 0.203 |
| 4096 | 2 | 1000/0、1.492/1.785、0 | 995/5、1.763/2.060、0 | 0.201 |
| 4096 | 3 | 1000/0、1.524/1.823、0 | 995/5、1.788/2.087、0 | 0.200 |

FrameTimingManagerのGPU時間0は8192で各回24/18/20件、4096で14/15/15件。`frames.csv` と照合CSVに0をそのまま残した。これらは欠測であり、有効値として集計していない。影パスは4096で約0.12 ms短縮したが、前述の画像では両視点で影境界と細部に差があり、4096は採用しない。

参照8192について、実際のEditor GUIからDevelopment Playerに接続し、GPU Usageモジュールを有効にして3回収録した。各回1000/1000フレームが `Gathered`。同時PresentMonとのQPC区間にpresentが1件だけあるフレームの `GPUBusy` を比較した。生CSVと照合結果は[Benchmarks/Issue3/connected/reference](../Benchmarks/Issue3/connected/reference/)に、Unity `.raw` はGit対象外の `LocalData/Issue3Connected/reference/` に保存。

| 回 | Unity PlayerLoop 有効/欠測、中央値/p95 ms、>3件 | PresentMon GPUBusy 有効/欠測、中央値/p95 ms、>3件 | 両方>3件 | present曖昧件数 |
|---:|---|---|---:|---:|
| 1 | 1000/0、1.716/4.409、332 | 707/293、2.675/4.572、302 | 197 | 293 |
| 2 | 1000/0、1.690/4.043、313 | 991/9、2.747/4.493、461 | 307 | 9 |
| 3 | 1000/0、1.696/4.039、306 | 991/9、2.724/4.486、451 | 300 | 9 |

長尾は独立 `GPUBusy` にも現れ、両者が3 msを超える同一フレームが197/307/300件ある。Editor非接続のPlayer側ログでは両解像度とも `PlayerLoop` と `GPUBusy` の3 ms超が各回0件だった。接続収録中にGPU Busyが増えることは確認したが、転送、Editor側収集、GPUクエリなどの寄与割合は分離していない。PresentMonのETWフレーム帰属とUnityのGPUクエリの時間軸・対象範囲は同一ではない。Editor接続4096ではProfilerの転送停止警告が出て3回の生ログが揃わず、有効な対称比較には用いていない。

### 追加計測の再現

1. Unity Editorを閉じ、8192または4096の `BaselineDesktop.asset` で `Tools/Run-Baseline.ps1 -Build -BuildOnly -Development` を実行する。ビルドハッシュは上記と一致させる。
2. `LocalData/Tools/PresentMon-2.6.0-x64.exe` を配置し、`Tools/Run-Issue3-Binary.ps1 -Condition reference` または `-Condition shadow4096` を実行する。各条件3回、Player側Profilerの生ログとPresentMonを同時収録する。`python Tools/Summarize-Issue3-Binary.py` で照合と集計を再生成する。8192のassetとPlayerに戻す。
3. Editor接続の再現は、EditorをGUIで開き、Playerを `-neuralLabProfile -neuralLabGpuProfile -force-d3d12 -screen-fullscreen 0 -screen-width 1920 -screen-height 1080` で起動し、PresentMonを `--process_name NeuralShadingLab.exe --v2_metrics --qpc_time_ms --set_circular_buffer_size 32768 --timed 240 --terminate_after_timed` で同時起動する。Editorの `Neural Shading Lab > Capture Three GPU Repeats` を選択し、Playerを前面に置く。`NEURAL_LAB_REPEAT_RESULT` が3回出て `gathered=1000` を確認する。Editorを閉じてから、`-batchmode -quit -projectPath UnityProject -executeMethod GpuRepeatExport.Export -neuralLabIssue3Input <raw_dir> -neuralLabIssue3AllFrames -neuralLabIssue3Output <out_dir>` でCSV化し、`python Tools/Summarize-Issue3-Connected.py` で参照8192を集計する。PresentMon stderrのoverflow警告がある回は破棄する。

4096のEditor接続試行は計測転送が止まり、`NEURAL_LAB_REPEAT_RESULT` が3回揃わなかった。未完了のCSVを有効な3回として扱わない。画質は静止した2条件での比較に限られ、動的な視点・照明での評価は残る。ニューラル化の実装は行っていない。

