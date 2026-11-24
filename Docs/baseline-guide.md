# Fantasy Kingdom ベースライン

## 導入

Unity Editor 6000.3.12f1で `UnityProject/` を開き、Asset Storeのマイアセットから `Fantasy Kingdom | 3D Sample Project` (ID 298128, version 1.1.0) をインポートする。Complete Projectとしてのインポートはプロジェクト設定を上書きするため、この専用プロジェクトを使う。

取得したサンプル本体はGit管理から除外する。新しいクローンでも各自のUnityアカウントで取得・インポートする必要がある。サンプル本体や生成したPlayerの公開可否は、配布元の利用条件を別途確認する。

インポート後、Unityメニューの `Neural Shading Lab > Prepare Fantasy Kingdom Baseline` を実行する。既存のシーンに未保存の変更がある場合は、先に保存する。この操作は計測用のDesktopパイプラインとRendererのコピーを `Assets/NeuralShadingLab/LocalSample/` に作成し、計測設定を更新する。再実行するとカメラ経路はサンプルの初期視点一つに戻る。

元の `Assets/Scenes/Demo.unity` と既存のAPVデータを使う。シーンを別GUIDへコピーしてベイクとの対応を壊さない。ビルド時のシーン処理で操作UI、カメラ制御、品質変更、ズームに連動するフォグ制御を無効化する。元のシーンファイルは変更しない。

## 固定する条件

- Windows x64、Mono、Direct3D 12（既定、DX11も選択可能）、Linear色空間。このPCにあるWindowsビルドモジュールに合わせてMonoを選択する。バックエンドが異なる結果は混ぜない。
- サンプルのDesktop品質を使用。1920x1080、URP Render Scale 1.0、動的解像度なし、STPなし、TAAなし。
- VSyncなし、FPS上限なし。通常のリリースPlayerで自動計測する。
- 元のPhysical Cameraの初期位置・向き・画角を固定する。
- 固定シミュレーション刻み1/60秒。通常フレームの実測時間とは別の値であり、60fpsに制限する設定ではない。
- 解像度設定後の初期化待ち10フレーム、ウォームアップ300フレーム、遅延データの排出待ち8フレーム、重複を除いたタイムスタンプ付き計測1000件。末尾も8フレーム待って遅れて返る値を更新する。
- 初期ベースラインでは移動経路を設定しない。これは静止視点の比較用であり、他の視点を代表する結果とは扱わない。

元のDesktop設定はRender Scale 0.8とSTPを使うため、このベースラインは配布時デモそのままの性能とは区別する。元のレンダラーの機能は継承し、上記の変更を全比較条件でそろえる。

## ビルドと実行

Unity Editorを閉じ、リポジトリ直下からPowerShellで実行する。

```powershell
.\Tools\Run-Baseline.ps1 -Build -Repeats 3
```

Unityの既定インストール先が違う場合は `-EditorPath` で指定する。ビルド済みなら `-Build` を省略する。画面を描画する必要があるため、Playerを最小化せず、画面をロックせずに実行する。CPU/GPUの負荷を競合させる別アプリは閉じる。

Playerは `Builds/Windows/` に生成され、完了後に自動終了する。各回は別プロセスから開始する。

描画確認用の画像は、計測とは別の起動で保存する。

```powershell
.\Tools\Run-Baseline.ps1 -CaptureOnly -Repeats 1
```

画像は `Benchmarks/Captures/<UTC run id>/capture.png` に保存する。スクリーンショットによる処理負荷を計測結果に混ぜない。

## 出力と読み方

`Benchmarks/Raw/<UTC run id>/` に次を保存する。

- `frames.csv`: GPU/CPU時間（ms）、取得したタイムスタンプ、読み出し時のフレーム番号、解像度スケール、VSync情報。
- `summary.json`: 中央値、95パーセンタイル、収集数、GPU/API/品質設定、カメラ姿勢、ビルド時のパッケージと設定ファイルのハッシュ。

`source-state.json` に実行開始時のGitリビジョンと変更状態を記録する。GPU時間の系列に欠測が残る、または1000件に達しない場合は成功として扱わず、Playerを終了コード2で終了させる。ProfilerRecorderのGPU Frame Timeも別のgpu-counter.csvへ記録し、完全な系列が得られた場合はprimaryGpuTimingSourceへ採用元を明記する。両系列を混ぜて補完しない。GPUが0のデータを高速だった結果として解釈しない。

FrameTimingManagerには計測自体の負荷があるため、全条件で同じ計測方式を使う。ここで収集する値はシーン全体のGPU時間であり、個別パスの時間ではない。非同期に返る計測値の `query_frame` は読み出し時点で、描画フレームとの一対一対応を示さない。

個別の重い処理を特定する際は、同じ設定のDevelopment Playerを `-Development -Build` で用意し、Unity ProfilerのGPU UsageモジュールやPIX等で対象パスを調べる。DevelopmentとReleaseの時間を直接混ぜない。取得可能なGPU Profilerの条件に合わせてGraphics Jobsは無効にしている。

## 次の検証

まず参照画像に欠けたモデル、マゼンタのマテリアル、不自然な照明やフォグがないか確認する。描画が正しくない状態の計測値は採用しない。

静止視点の再現を確認してから、複数視点と光条件を追加する。`Assets/NeuralShadingLab/Resources/NeuralLabBaseline.asset` のCamera Posesを編集すると、各姿勢間を同じシミュレーションフレーム数で補間する。カメラ経路を変えた結果は別の比較条件として記録する。

GPUプロファイルで材質評価・フォグ等のコストを調べ、ニューラル近似する処理を一つ選ぶ。ジオメトリや影などが支配的なら、その事実を記録して対象やシーンを再検討する。

UnityEngine.Randomのシードは固定するが、サンプル内のVFX等すべての独立した乱数系の同期はまだ保証しない。厳密なピクセル比較を行う前に、VFXの個別シードと時間も固定する。


## 旧自動計測の診断（2026-11-03）

Windows Playerのビルドと、1920x1080のサンプル描画を確認した。自動計測の診断では、DX11・DX12の両方でGPU時間0の欠測が出た。ProfilerRecorderのGPU Frame Timeにも同じ欠測が出たため、正式なGPUベースラインはまだ採用しない。

最新の診断例は `Benchmarks/Raw/20261103T054943547Z/`。1000件中773件のみGPU時間が有効で、statusはincomplete。記録された中央値は有効な部分だけから計算した診断値であり、1000フレーム全体のGPU分布や最適化の成果を示さない。ゼロを除いた値だけで他の実装に優劣をつけない。

Unity公式も、FrameTimingManagerは4フレーム遅延し、その待機で正しいGPU値の取得が保証されるわけではないと説明している。
https://docs.unity3d.com/cn/6000.0/Manual/frame-timing-manager.html

この状態では、先にDevelopment PlayerのGPUプロファイルで描画パスの内訳を調べる。以下は固定シーンを表示し続けるモードで、自動終了しない。

```powershell
# Unity Editorを閉じてDevelopment Playerだけをビルドする
.\Tools\Run-Baseline.ps1 -Build -BuildOnly -Development

# シーン・カメラ・解像度を同じ条件に固定して表示する
& .\Builds\WindowsDevelopment\NeuralShadingLab.exe -neuralLabProfile -force-d3d12
```

その後Unity Editorを開き、Window > Analysis > Profilerで接続先のPlayerを選択し、GPU Usageモジュールを有効にして記録する。ウォームアップ後の安定した部分を調べ、メイン描画、影、フォグ、ポスト処理など、どこが重いかを確認する。Development Playerの数値はRelease Playerの自動計測値と混ぜない。計測用Playerの画面を前面に出して表示し続ける。

DX11で比較する場合は、自動計測スクリプトに `-GraphicsApi d3d11`、Playerには `-force-d3d11` を指定する。APIの異なるデータ同士を直接比較しない。

## 2026-11-04のGPU Profiler実測

Development Playerに実際にUnity Profilerを接続し、GPU Usageで各1000フレームを3回取得した。全3回でGPU欠測0。主要パスの数値、欠測調査、再現手順は[GPU計測結果](gpu-profiling-results.md)を参照。上記の旧診断は経緯として残すが、現時点の到達点を表すものではない。Release Playerの自動GPU計測は引き続き未確定。

