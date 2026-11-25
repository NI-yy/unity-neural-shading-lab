# PCSSへの変換

現在のメイン方向光のソフトシャドウを、方向光用PCSSへ変更した。影マップの生成、8192の解像度、4カスケード、シーン、材質、カメラは既存の設定を使う。

## 実装

- Unity推奨のソース変更方法に従い、URP 17.3.0とuniversal-configを `UnityProject/Packages/` に埋め込んだ。PackageCacheの原本は変更しない。
- `ShaderLibrary/NeuralLabPCSS.hlsl`: 生の影深度で遮蔽物を探索し、平均遮蔽距離から半影幅を推定し、可変半径のPCFで評価する。方向光の正射影に合わせ、各カスケードのUV／世界距離・深度／世界距離の比を使う。通常Z／Reversed-Zを区別し、サンプルを同じアトラスタイル内に制限する。傾斜面ではreceiver-plane depth補正を使う。
- `ShaderLibrary/Shadows.hlsl`: メイン光のsoft-shadow経路をPCSSへ接続した。ハードシャドウと追加光は元の経路を使う。このプロジェクトの主な影はメイン方向光。
- `Runtime/Passes/MainLightShadowCasterPass.cs`: 実際のカスケード領域と投影行列から必要なスケールをGPUへ渡す。1〜4カスケードおよび2カスケードの長方形アトラスに対応する領域を送る。
- Playerの `NeuralLabBuildInfo.txt` に `main_light_shadow_filter=PCSS` とPCSSソースのSHA-256を記録する。

## 初期パラメータ

`UnityProject/Packages/com.unity.render-pipelines.universal/ShaderLibrary/NeuralLabPCSS.hlsl` の定数で設定する。

| 項目 | 値 |
|---|---:|
| 光源の角直径 | 0.53° |
| 遮蔽物探索 | 32サンプル |
| フィルタ | 32比較サンプル |
| 探索半径算出の上限距離 | 50 m |
| 探索半径の上限 | 32アトラステクセル |
| フィルタ半径の上限 | 32アトラステクセル |
| 遮蔽物判定の深度バイアス | 0.01 m |

`NEURAL_LAB_MAIN_LIGHT_PCSS` を0にして再ビルドすると、元のPCF評価経路に戻る。以前のPCF Playerと画像・測定データは `Builds/PCF-Baseline-20261107/` と [PCFベースライン](pcf-baseline.md) に保持している。PCF計測スクリプトは埋め込みURPを優先して検証し、PCSSをPCFと誤って記録することを防ぐ。

変換・ビルド後、[PCF／PCSSの反復計測と画像比較](pcf-pcss-comparison.md)を実施した。画質パラメータの調整、ニューラル近似は実施していない。

参照: [UnityのURPソース変更手順](https://docs.unity3d.com/cn/6000.0/Manual/urp/customize/modify-urp-source-code.html)、[NVIDIA PCSSの概要](https://developer.nvidia.com/gpugems/gpugems3/part-ii-light-and-shadows/chapter-8-summed-area-variance-shadow-maps)。
