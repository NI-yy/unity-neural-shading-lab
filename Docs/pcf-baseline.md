# PCFの現状ベースライン（2026-11-07）

PCSSとの処理時間・画面比較に使うPCF側の基準を保存した。今回の描画方式は標準URPの **High品質・7×7テントPCF・16回のハードウェア比較サンプリング**。影マップ解像度8192、4カスケード。PCSSとニューラル方式の実装には着手していない。

## 処理時間

同じDX12 Development Mono x64 Playerを、Editorに接続せずGPU Usageのbinary logをPlayer自身へ保存した。各視点3回、各回1000フレーム。主GPU系列は全6回で1000/1000取得、欠測0。300フレームのウォームアップ後に測定し、2ラウンド目は視点の実行順を逆転した。単位はms。FPSへの換算ではなくGPU処理時間を示す。

| 視点 | 回 | 全体中央値 | 全体p95 | 影マップ生成 中央値 | 不透明描画 中央値 | 透明描画 中央値 |
|---|---:|---:|---:|---:|---:|---:|
| 初期 | 1 | 1.6205 | 1.6560 | 0.318 | 0.593 | 0.041 |
| 初期 | 2 | 1.6240 | 1.6580 | 0.318 | 0.592 | 0.041 |
| 初期 | 3 | 1.6180 | 1.8460 | 0.319 | 0.588 | 0.041 |
| 影生成が重い視点 | 1 | 2.2210 | 2.2630 | 1.235 | 0.349 | 0.040 |
| 影生成が重い視点 | 2 | 2.2090 | 2.2660 | 1.219 | 0.347 | 0.039 |
| 影生成が重い視点 | 3 | 2.2210 | 2.4594 | 1.222 | 0.355 | 0.040 |

中央値・p95は各回を分け、昇順のGPU時間の位置 `(n-1)p` を線形補間して計算した。PCFの参照・フィルタ処理は不透明／透明描画等に含まれ、上表の影マップ生成とは別の処理。PCF単体のGPU時間は今回分離していない。親子マーカーを加算しない。

FrameTimingManagerのGPU時間0は各回3〜12件、計35件あり、生CSVに欠測としてそのまま残した。GPU Usageとの穴埋めや補間はしていない。全Playerの終了コード2はこの自動系列の欠測を示す。性能の主系列は上記の欠測なしGPU Usage。このセッションの独立PresentMon計測は使用していない。

## スクリーンショット

[2視点の一覧](../Benchmarks/Captures/ShadowComparison/PCF/overview.png)（左：初期、右：影生成が重い視点）を保存した。下の画像はどちらも1920×1080の原寸PNG。

- [初期視点](../Benchmarks/Captures/ShadowComparison/PCF/initial.png)
- [影生成が重い視点](../Benchmarks/Captures/ShadowComparison/PCF/shadow-stress.png)

撮影は性能計測と別起動。両方とも300フレームのウォームアップ後、**Time.frameCount=310、シミュレーション時刻5.15秒**、固定シミュレーション刻み1/60秒で撮影した。カメラ姿勢・画角・色空間・撮影時刻は [pcf-baseline.json](../Benchmarks/ShadowComparison/pcf-baseline.json) と各 `capture-state.json` に保存。スクリーンショットは最終画面のPNGで、プロジェクトの照明計算はLinear。

以前の非同期CaptureOnlyは画像保存後に終了時クラッシュする場合があったため、比較用に `-neuralLabCaptureSynchronous` を追加した。EndOfFrameで `CaptureScreenshotAsTexture` を読み出し、PNGを同期保存し、テクスチャ解放後10フレーム待って終了する。今回の2起動は両方とも終了コード0。シーンと影シェーダは変更していない。画像の読み込み、寸法、描画内容も確認した。

## 固定した条件と保存先

- Unity 6000.3.12f1、URP 17.3.0、RTX 4070 SUPER、Direct3D 12、Windows x64 Mono Development Player。
- Fantasy Kingdomの元のDemoシーン、Desktop品質、1920×1080、Render Scale 1、TAA/STP/動的解像度/VSync/FPS上限なし。光源とポスト処理は元の参照設定。
- `initial` は既存の初期カメラ。`shadow-stress` は [高負荷探索で固定したカメラ](heavy-process-candidates.md) で、光源は初期設定のまま。
- [比較用CSV](../Benchmarks/ShadowComparison/pcf-timings.csv) と [基準manifest](../Benchmarks/ShadowComparison/pcf-baseline.json)。生系列は `Benchmarks/Candidates/pcf-baseline-20261107/`。Profiler `.raw` はGit対象外の `LocalData/Candidates/pcf-baseline-20261107/`。
- PCF Playerの全体を `Builds/PCF-Baseline-20261107/` に別保存した。[ファイル一覧とSHA-256](../Benchmarks/ShadowComparison/pcf-player-files.csv) で同じビルドを確認できる。生成Playerと撮影時の詳細ログはGit除外対象。比較用の原寸PNGと一覧画像はGitに保存する。
- 元のURP `Shadows.hlsl` は `LocalData/ShadowComparison/PCF/Shadows.original.hlsl` に退避。SHA-256は `caa3e10c26476cd44cbaf7eadca308a2417bd42cc18a8a4c1466466e2af9173d`。パイプライン依存ハッシュは `89d118024ad1b7b67baa1c4e88e53ad7`。

## 再現とPCSS比較で合わせるもの

1. Editorを閉じ、`Tools/Run-Baseline.ps1 -Build -BuildOnly -Development`。
2. 新しい出力名で `Tools/Run-PCF-Baseline.ps1 -Phase <新規名>`。High PCFのソースハッシュと設定を検証し、2視点×3回の計測と同期撮影を行う。`-Mode Measure`、`-Mode Capture`、`-Mode Export` で各工程を実行できる。既存の測定は上書きしない。
3. `python Tools/Summarize-Candidates.py --phase <出力名>` で件数・時刻対応・有効値を再検証する。

後のPCSS側はAPI、ビルド種別、解像度、影マップ解像度、カスケード、カメラ、光源、ポスト処理、撮影フレームとシミュレーション時刻を合わせる。PCSSで追加する光源サイズ・遮蔽物探索・フィルタのサンプル数は別に記録する。画面比較は同じ座標の原寸PNGを使う。

性能比較時には、この保存済みPCFに加えてPCFも再測定し、PCF/PCSSの実行順を交代させる。時刻の離れた単発測定同士で差を断定しない。数値は計測付きDevelopment Playerの値であり、ReleaseやProfiler無効時の性能とは区別する。
