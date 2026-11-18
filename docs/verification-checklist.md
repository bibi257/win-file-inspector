# Windows 11 実機スモークテスト

ログには個人情報が含まれます。ログ本体を共有せず、結果は OK/NG と差分の説明だけを記録してください。必ず小さなテスト用フォルダだけを対象にし、C ドライブ全体を指定しないでください。

## 準備

1. ローカルの一時領域に `WinInspectorSmoke` フォルダを作成します。
2. その中に小さなテキストファイルを数個作り、日本語名、カンマ、引用符を含む名前を1つ用意します。
3. 実行前にエクスプローラーのプロパティでサイズと作成・更新日時を控えます。
4. 機内モードでネットワークを切断します。

## 実行と確認

```cmd
Start-Inspector.cmd -Mode Files -Target "C:\Users\<user>\AppData\Local\Temp\WinInspectorSmoke" -OutputDir "%LOCALAPPDATA%\WinInspector\SmokeLogs"
```

- summary の最終アクセス設定値を `fsutil behavior query disablelastaccess` の結果と照合します。fsutil は確認者が手動実行し、ツール本体は呼び出しません。
- files CSV の数件についてサイズ・作成・更新日時をエクスプローラー表示と比較します。
- CSV が Excel で文字化けせず、日本語・カンマ・引用符を保持することを確認します。
- 実行前後で対象ファイルの日時が変化していないことを確認します。
- 機内モードで完走することを確認します。通信遮断の検証は errors の内容ではなく、ネットワーク監視ツールで外向き通信がないことを確認します。

Apps モードを別途確認するときは、Windows の「設定 > アプリ > インストールされているアプリ」と件数・名称がおおむね一致するかだけを確認します。PowerShell 7 から Appx を取得できない場合は summary/errors に示され、アプリ情報を推測で補完しないことを確認します。

結果記録: 日付 / Windows 11 ビルド / PowerShell 7 バージョン / Files OK・NGと差分 / Apps OK・NGと差分 / オフライン実行 OK・NG。ログファイル自体は共有しないでください。

## C# exe 版

CI artifact の `WinInspector.exe` を取得し、プロパティの「ブロックの解除」を実施してから、PowerShell 版と同じ小さな fixture を `-Mode Files -Target <fixture> -OutputDir <local-output>` で走査します。CSV の列順・BOM/CRLF、summary の行順、走査件数、出力先、終了コード、実行前後のファイル日時を確認してください。機内モードでも完走することを確認します。ログは共有せず、結果と差分だけ記録してください。

## GUI 設定画面（WinInspector.exe -Gui）

1. エクスプローラーからダブルクリック → GUI だけが残り、コンソールは消える（Windows Terminal が既定の場合も確認）。
2. cmd から `WinInspector.exe -Mode Files -Target <小さいフォルダ> -NoPause` → 今までどおりコンソールに出力され、`echo %ERRORLEVEL%` が 0。
3. cmd から引数なしで実行 → GUI が開き、閉じるまで cmd が待つ。
4. 初回起動の初期値（既定の 3 か所、365、3、出力先）と、config.psd1 を置いたときの初期値。
5. 対象の追加・削除・既定に戻す。ネットワークドライブ、UNC、ジャンクション、存在しないフォルダを選んだときのエラー表示。
6. 設定を変えて閉じ、開き直すと復元される。`%LOCALAPPDATA%\WinInspector\gui-settings.json` を壊すと警告帯が出て既定値で開く。
7. 小さいフォルダで検査 → 進捗が動き、完了後に summary が表示される。summary の内容が出力フォルダの summary ファイルと同じ。
8. 実行中に [中止] → 「中断」と表示され、summary と CSV が書かれている。実行中にウィンドウを閉じたときの確認ダイアログ。
9. [出力フォルダを開く] → その実行のフォルダがエクスプローラーで開く。カンマを含む出力先でも開く。
10. 管理者として実行：UAC で [はい] → 開き直してすぐに検査が始まり、summary の「管理者: True」「Prefetch使用=はい」。[いいえ] → 元の画面に戻りメッセージが出る。
11. 標準ユーザーで別の管理者アカウントを入力して昇格 → 出力先と対象が元のユーザーのものになっている。
12. 高 DPI（150%）で文字やボタンが切れない。
13. Process Monitor で、GUI の操作中に書き込まれるファイルとレジストリを記録し、`gui-settings.json`、出力フォルダ、フォルダ選択ダイアログが OS 側で記録する値以外がないこと。
14. 通信を遮断するファイアウォール規則を入れた状態で、GUI から検査を最後まで実行できる。
