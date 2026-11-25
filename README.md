# Unity Neural Shading Lab

Unity公式の3Dサンプルを使い、ニューラルシェーディングによる画質・GPU時間・メモリ使用量の変化を検証する実験リポジトリです。

目的は、既存の重いシェーディング処理を小さな学習済みネットワークで近似し、実際のシーンで軽量化できる条件を調べることです。Unity 6000.3.12f1 / URP 17.3.0 の実験プロジェクトと、Fantasy Kingdom用のベースライン準備・計測ツールを用意しています。ニューラルモデルの比較結果はまだありません。

Development PlayerにUnity Profilerを接続し、GPU Usageを同一条件で3回・各1000フレーム実測しました。GPU欠測は0件で、描画パスの内訳と再現手順を[GPU計測結果](Docs/gpu-profiling-results.md)に記録しています。従来の自動GPU計測は欠測が残り、Release Playerの正式なベースライン値は未確定です。

Issue #2では同じDevelopmentビルドのProfilerなし対照測定と、メインライト影マップ8192→4096の比較を各3回以上行いました。自動GPU系列の0は欠測のまま残し、画質差も確認しました。[長いGPUフレームと影設定の対照実験](Docs/issue-2-gpu-tail.md)にデータ、再現手順、未確定点を記録しています。ニューラル実装は未着手です。

Issue #3ではPresentMonのETW GPU BusyでDX12 Development Playerを独立計測し、影4096を再評価しました。結果と残る不確実性は[DX12独立計測と影設定の再評価](Docs/issue-3-dx12-independent.md)に記録しています。ニューラル実装は未着手です。

材質以外も含めて高負荷処理を探索し、Editor非接続の5条件×3回でGPU Usageを各1000フレーム取得しました。追加視点の影マップ生成が約1.26 ms、全体の約55%を占めるため、影の可視率近似を第一候補にしています。SSAOと法線生成も切り分けました。[高負荷処理の探索](Docs/heavy-process-candidates.md)に生系列、比較画像の所在、条件と制約を記録しています。

PCFとPCSSを比較するため、[PCFの現状ベースライン](Docs/pcf-baseline.md)を別に保存しました。2視点×3回のGPU時間、原寸スクリーンショット、撮影時刻・カメラ、PCF Playerのスナップショットを揃えています。現在のメイン方向光は[PCSSへ変換](Docs/pcss-implementation.md)済みで、[同条件での反復計測と画像比較](Docs/pcf-pcss-comparison.md)を保存しました。PCSSは全体GPU時間が約6〜17%増え、影マップ生成コストはほぼ同じです。

## 最初の実験

1. Unity 6の専用プロジェクトを `UnityProject/` に作成する。
2. 第一候補の [Fantasy Kingdom (URP)](https://assetstore.unity.com/packages/templates/packs/fantasy-kingdom-3d-sample-project-298128) を導入し、元のシーンが動くことを確認する。Editorとパッケージのバージョンを記録する。
3. 固定のカメラ経路でGPU時間を計測し、影・遮蔽・照明・材質評価など、推論で近似できる処理が十分な負荷を占めるか調べる。
4. 対象処理を一つ選び、学習データを収集する。評価用の視点・照明条件を学習から除外する。
5. オフラインで小さなMLPを学習し、重みを書き出してUnityのHLSLシェーダで推論する。
6. 同じシーン・同じ入力条件で、元の処理、通常の軽量化、ニューラル版を比較する。

重い処理がジオメトリ・描画命令・影生成などに偏る場合は、そのサンプルをニューラルシェーディングの性能検証に採用するか再判断します。高速化できなかった結果も記録します。

## 比較の原則

- GPU時間と画質を対にして評価する。FPSだけで結論を出さない。
- 対象処理のGPU時間とシーン全体のGPU時間を両方記録する。
- 同画質でのコスト、または同コストでの画質を比較する。
- 学習条件以外の視点・照明と、移動中のちらつきも確認する。
- モデルの重み、Latent Texture、必要な中間バッファを含めてGPUメモリを数える。

導入と実行の手順は [ベースラインガイド](Docs/baseline-guide.md)、比較の方針は [検証計画](Docs/experiment-plan.md)、結果の記入形式は [計測用CSV](Benchmarks/results-template.csv) を参照してください。

## ディレクトリ

- `Docs/`: 実験設計と手順
- `Benchmarks/`: 計測条件と結果の表
- `UnityProject/`: Unity 6 URP実験プロジェクト
- `Training/`: 学習とモデル書き出しのコードの配置先（未作成）

外部サンプルは配布元から取得し、同梱の利用条件を確認します。サンプル資産や講演PDFの公開リポジトリへの再配布は、許諾を確認してから行います。学習モデルの公開条件も確認します。

## 参考

- [CEDEC 2026: グラフィックスエンジニアのためのニューラルシェーディング入門](https://cedec.cesa.or.jp/2026/timetable/detail/s69774c1c622ae/)
- [An Introduction to Neural Shading: code and materials](https://github.com/shader-slang/neural-shading-s25)
- [Unity Fantasy Kingdom](https://unity.com/demos/fantasy-kingdom)
- [Unity Time Ghost Environment](https://assetstore.unity.com/packages/templates/packs/time-ghost-environment-3d-sample-2024-298911)
