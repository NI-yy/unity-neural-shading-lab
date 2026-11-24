# Issue #2: 長いGPUフレームと影マップ解像度の対照実験（2026-11-06）

## 判断

Issue #1 の Unity GPU Usage 収録では、DX12 Development Player の `PlayerLoop` p95 が3.899〜3.984 ms、`>3 ms` が3000フレーム中836件だった。一方、**同じDevelopmentビルドでGPU Usageを接続しない**今回の自動計測9回では、正のGPU時間に `>3 ms` が1件もなかった。参照条件の有効部分のp95は1.901〜2.136 ms。静止視点の描画パスが収録中だけ突然重くなるというより、GPU Usageのタイムスタンプ計測負荷、GPUスケジューリング、パスへの時刻帰属が長い裾を作る可能性が高い。ただし自動計測には各回20〜112件のGPU時間0があり、Profilerのオン・オフで計測器も違う。**長いフレームの根本原因は未確定**で、GPU Usageのp95を通常実行時のGPU時間へ外挿しない。

影マップ解像度8192→4096を一つだけ変更して評価した。有効部分のGPU中央値は参照の約1.59〜1.61 msから約1.46〜1.47 msへ下がった。p95の改善は安定しておらず、画質にも差が出た。全9回がGPU欠測を含む `incomplete` なので、これは**改善候補の診断結果**であり、確定した高速化として採用しない。材質評価だけのコストは分離できていないため、ニューラル近似対象は選ばず、ニューラル実装は行っていない。

## 条件とデータ

- Unity 6000.3.12f1 / URP 17.3.0 / Fantasy Kingdom 1.1.0、RTX 4070 SUPER、Windows 11、DX12、Windows x64 Mono Development Player。同じ `Assets/Scenes/Demo.unity` の初期カメラ静止視点、Desktop品質、1920×1080、Render Scale 1、VSync/FPS上限/動的解像度/TAA/STPなし。`Time.captureDeltaTime=1/60`、乱数シードは既存のベースライン設定。GPU UsageなしのFrameTimingManagerとProfilerRecorderを全条件で同じように有効化した。
- 条件順は**参照8192 → 影4096 → 参照8192**。各条件は別プロセスで3回、初期化10フレーム、ウォームアップ300フレーム、1000サンプル、末尾の遅延排出8フレーム。変更したのはGit対象外の `Assets/NeuralShadingLab/LocalSample/BaselineDesktop.asset` の `m_MainLightShadowmapResolution` のみ。各ビルドの `pipeline_dependency_hash` は参照 `89d118024ad1b7b67baa1c4e88e53ad7`、4096版 `8f85983f8df686e99e506509fde1d48e`。参照前後のパイプライン依存ハッシュは一致した。計測後にassetを元に戻し、参照Playerを再ビルドした。
- [9回のフレーム系列とPlayerのsummary](../Benchmarks/Issue2/) を保存した。`frames.csv` の0をそのまま残し、有効値は有限かつ正の値だけとした。中央値・p95は**有効部分の診断値**であり、1000フレーム全体の分布とは扱わない。各回の `gpu-counter.csv` も保存したが、FrameTimingManagerの欠測補完には使っていない。`python Tools/Summarize-Issue2.py` で [集計CSV](../Benchmarks/Issue2/automatic-summary.csv) を再生成できる。

| 条件 | 回 | 有効 / 1000 | 欠測 | 有効部分の中央値 ms | 有効部分のp95 ms | 有効な `>3 ms` |
|---|---:|---:|---:|---:|---:|---:|
| 参照・前 | 1 | 903 | 97 | 1.604608 | 1.982054 | 0 |
| 参照・前 | 2 | 894 | 106 | 1.593344 | 1.999565 | 0 |
| 参照・前 | 3 | 903 | 97 | 1.589248 | 2.135859 | 0 |
| 影4096 | 1 | 888 | 112 | 1.466368 | 2.006682 | 0 |
| 影4096 | 2 | 954 | 46 | 1.463296 | 1.875302 | 0 |
| 影4096 | 3 | 952 | 48 | 1.470464 | 1.874842 | 0 |
| 参照・後 | 1 | 965 | 35 | 1.606656 | 1.910579 | 0 |
| 参照・後 | 2 | 980 | 20 | 1.596416 | 1.900544 | 0 |
| 参照・後 | 3 | 968 | 32 | 1.608704 | 1.913498 | 0 |

参照の再測定で中央値は元に戻ったが、欠測率が前後で変わった。このため「有効値だけ」のp95差から影設定の効果を断定できない。0を削除して1000件の完全なGPU系列に見せることや、ProfilerRecorderの値で穴埋めすることはしていない。全回のPlayer終了コードは欠測を示す2だった。

## 長いフレームの切り分け

Issue #1 のGPU Usageで長いフレームは不透明描画と深度・法線プリパスに集中し、メインライト影はほぼ一定だった。Profilerを有効化すると各パスのGPUタイムスタンプと収録処理が入るため、深さ8のマーカーの長い値を即座に材質・ジオメトリ固有の負荷と解釈できない。今回のGPU Usageなしの参照前後6回では、正のGPU時間に3 ms超がなかった。欠測は前半約9〜11%、後半約2〜4%なので、その中に長いフレームが隠れている可能性は残る。

独立したNsight Systems 2026.1.3のDX12トレースも試した。短い収録はシーン描画に入る前に終わり、35秒収録ではUnity PlayerがクラッシュしてGPUワークロードは1件しか得られなかった。DX12の描画フレームを独立に測定できておらず、その結果を裏付けに使わない。Unity Editorでの4096版GPU Usage追加収録もWindows セキュリティ画面で操作が止まり、行っていない。現時点で **計測負荷と実GPU負荷の寄与率、特定パスへの時刻帰属、欠測フレームの真のGPU時間は不明**。

## 画質比較

別起動で300フレームのウォームアップ後、同じカメラ・設定でPNGを1枚ずつ保存した。参照の変更前後2枚はSHA-256がともに `199e8a7f4f021c37a9f997722332f3ff12a7f08c1ed684e28bc7de19952bc0c5` で**ピクセル完全一致**。4096版は `8234e9f680ed9372c55d79a6bb258c947093147ecf80b2154d3462e2bf83bc7a`。参照対4096の1920×1080・8-bit RGB画像のPSNRは **32.730 dB**（ffmpegの `psnr` フィルタ、sRGB符号値、動的範囲255）。建物・岩・植生の影境界と細部に差が見える。画質同等とは判断していない。他視点、照明、動くシーンでの比較は未実施。

画像はサンプル資産の再配布を避けるため、Git対象外の `Benchmarks/Captures/Issue2/` に `reference.png`、`shadow4096.png`、`difference-4x.png` としてローカル保存した。差分画像は各RGBチャンネルの絶対差を4倍して表示用にクリップしたもの。元画像を同じ位置で比較する。画像はデータの根拠として手元に保持し、公開Gitへ追加しない。

## 再現

1. [ベースラインガイド](baseline-guide.md)に従いサンプルを導入・準備する。既存の `LocalSample/BaselineDesktop.asset` を退避し、参照では `m_MainLightShadowmapResolution: 8192`、候補では `4096` にする。他の設定は触らない。
2. 各条件でEditorを閉じ、`.\Tools\Run-Baseline.ps1 -Build -BuildOnly -Development` を実行する。Playerを表示して `-force-d3d12 -screen-fullscreen 0 -screen-width 1920 -screen-height 1080` で1プロセスずつ起動する。計測は `-neuralLabBenchmark -neuralLabOutput <空の出力ディレクトリ>` を付けて3回、画像は別起動で `-neuralLabCapture` を付ける。`Start-Process -Wait -PassThru` で各回の終了を待つ。GPU欠測があれば終了コード2になるが、rawとsummaryは保存される。
3. `frames.csv`、`gpu-counter.csv`、`summary.json` を条件別の `run-1`〜`run-3` に保存し、`python Tools/Summarize-Issue2.py` を実行する。0・NaN・無限大は欠測で、補間しない。参照を再ビルドして3回取り直す。ビルド種別、API、視点、品質、解像度を混ぜない。
4. 比較画像のPSNRは `ffmpeg -i reference.png -i shadow4096.png -lavfi psnr -f null NUL` で再計算する。保存前に画像の条件とハッシュを確認する。

次の判断には、GPU時間を欠測なく独立取得できる測定系か、同じGPU Usage設定での参照・候補双方の3回収録が必要。その上で影パスと全体の時間、他視点・照明での影の劣化を比較する。不透明描画内の材質評価だけを分離できない限り、ニューラル化の性能根拠にはしない。
