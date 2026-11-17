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
