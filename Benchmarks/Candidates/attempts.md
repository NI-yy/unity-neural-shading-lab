# 採用・不採用の試行

- `confirmed-player-only/`: 採用した5条件×各3回。Editor非接続のPlayer側GPU Usage。各回1000フレーム取得、全体GPU欠測0。FrameTimingManagerの0は別系列の欠測として保存。
- `failed-attempts/no-ssao-confirmed-run-2/`: 確認測定のSSAO除去2回目がPlayer終了時にクラッシュ（終了コード `-1073740771`）。この回と対応する `.raw` は失敗試行として保全し、採用データを同条件の再取得で揃えた。
- `screening-player-only/`: 確認前の単回探索。植生除去は終了時クラッシュで不採用。`lake` は描画対象が実質的に映らず水面比較として棄却。`waterfall` の増加は透明描画ではなく影生成であり、その視点を数値で固定して `shadow-stress` として再測定した。
- `screening/`: PresentMonの既存ETWセッション名と競合。独立CSVを取得できず、不採用。
- `screening-v2/`: 専用ETWセッション名を使ったが対象CSVなし、ETWイベント損失。独立計測に不採用とし、今回の診断ループとその子プロセスを停止した。他の既存セッションを強制停止していない。
- `screening-v3/`: 対象Playerを起動してPIDを明示したが、PresentMon CSVを取得できず、不採用。
- `Benchmarks/Captures/Candidates/confirmed-player-only/`: 性能収録とは別起動。初期Referenceとshadow-stressのhard-shadowsはPNG保存後のクラッシュが再試行でも発生。PNG、失敗ログ、終了コードを保持した。各条件の2枚はそれぞれピクセル一致。SSAO除去、shadow-stressのReferenceとno-shadowsは画像起動が正常終了。`captures.csv` に正常・異常の終了コードを区別して記録した。

独立計測に失敗した試行とクラッシュしたPlayerのデータは保全した。採用した性能反復は `confirmed-player-only/` の15回。ニューラルモデルの実装は行っていない。
