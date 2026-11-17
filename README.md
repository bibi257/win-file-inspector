# WinInspector

Windows 11 向けのオフライン検査ツールです。ファイルとインストール済みアプリの一覧、利用痕跡に基づく長期未使用候補を CSV と summary に出力します。

## 読み取り専用・オフライン

このプログラムはファイル本体を開かず、ファイルやレジストリの内容を変更・削除しません。レジストリキーは書き込み不可で開きます。ファイル書き込みは出力フォルダ配下のログ作成だけです。

アプリ本体に Web 通信、名前解決、転送、リモート実行、パッケージ取得 API は実装せず、`Win32_Product` と `Get-Package` も使用しません。AST 静的検査と許可リストにより、許可リストにないコマンド・型・COM ProgID と、書き込み系 .NET メソッド、動的呼び出しを `tests/static` で検出します。保証対象は本プログラム自身です。Windows、Defender、OneDrive など OS 側の通信は範囲外です。

## 動作環境

Windows 11 と PowerShell 7 以降が必要です。PowerShell 7 は [公式インストール手順](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows) に従って一度だけ導入してください。導入時の取得にはネットワークが必要な場合がありますが、WinInspector の実行はオフラインです。ランチャーは `pwsh.exe` を呼び、テレメトリと更新確認を無効にします。

PowerShell 7 は Microsoft Store、WinGet、または公式 GitHub リリースの MSI から導入できます。組織の管理端末では管理者・ポリシーを確認してください。

## 実行

`Start-Inspector.cmd` をダブルクリックすると通常ユーザーで起動します。Prefetch の痕跡を加える場合は `Start-Inspector-Admin.cmd` を使い、UAC を承認します。管理者ランチャーは引数を一時ファイルに1行ずつ保存して昇格先で復元し、読み込み後にそのファイルを削除します。引数内の改行は使用できません。

```powershell
Start-Inspector.cmd -Mode All
Start-Inspector.cmd -Mode Files -Target C:\Users\Me\Documents -OutputDir D:\LocalLogs
Start-Inspector.cmd -Mode Apps -UnusedDays 365
```

引数は `-Mode Files|Apps|All`、`-Target`、`-Exclude`、`-OutputDir`、`-UnusedDays`（既定365）、`-FolderDepth`（既定3）、`-IncludeRawLastAccess`、`-IncludeRemovable` です。`config/config.sample.psd1` を `config/config.psd1` にコピーして設定できます。明示したコマンドライン引数が設定ファイルより優先されます。

既定の対象は `%USERPROFILE%`、`C:\Program Files`、`C:\Program Files (x86)` です。UNC とネットワークドライブは拒否し、リムーバブルは明示指定がない限り除外します。InstallLocation や出力先までの経路にリパースポイントがある場合は拒否します。Prefetch 照合では `msiexec.exe`、`update.exe`、`setup.exe`、`uninstall*.exe`、`unins*.exe`、`install*.exe` を汎用名として除外します。ログは `%LOCALAPPDATA%\WinInspector\Logs\<実行日時>` に出力します。

ZIP で取得した場合、展開前に ZIP のプロパティで「ブロックの解除」を選んでください。グループポリシーが実行ポリシーを強制する環境では実行できない場合があります。制約付き言語モードでは実行を停止します。

## 出力

1回の実行ごとに `summary_*.txt`、`files_*.csv`、`folders_*.csv`、`apps_*.csv`、`app_candidates_*.csv`、`errors_*.csv` を作ります。不要なモードの CSV は作りません。CSV は BOM 付き UTF-8、CRLF です。Excel の数式実行を避けるため、自由入力テキストが `= + - @`、タブ、改行で始まる場合は先頭にアポストロフィを付けます。

ファイル一覧にはサイズ、作成・更新日時、属性、拡張子を記録します。最終アクセス日時は NTFS で更新が有効と確認できる場合だけ入り、それ以外は空欄と理由が記録されます。Raw 値は `-IncludeRawLastAccess` で追加できます。更新有効でも精度は最大1時間程度で、セキュリティソフト等のアクセスでも更新されます。

アプリは候補A（古い実行記録）、候補B（実行記録なしで追跡有効かつ古いインストール日）、判定不能、対象外に分類します。UserAssist は現在のユーザーの Explorer 経由の起動だけを記録し、コマンドラインなどからの起動は通常記録しません。追跡設定が無効の場合や Store 情報を PowerShell 7 で取得できない場合は推測せず、errors/summary に記録します。候補はアンインストール推奨を意味しません。

列挙時に取得したメタデータは、開かれたままのファイルでは実体とわずかにずれることがあります。ハードリンクは区別できず二重計上の可能性があります。

## 個人情報を含むログ

ログにはパス、アプリ名、利用履歴が含まれます。リポジトリやクラウド同期フォルダに置かず、他者に共有しないでください。不要になったログは利用者自身が削除してください。実ログはこのリポジトリや CI 成果物に含めません。

## 所要時間・中断

対象とファイル数により数分以上かかる場合があります。進捗は概ね1秒ごとに表示します。Ctrl+C で中断すると、出力済みログを確定して中断状態を記録します。中断地点からの再開には対応しません。

## 開発・検証

PowerShell 7 と Pester 5 で `Invoke-Pester ./tests/unit,./tests/static`、Windows では `Invoke-Pester ./tests/windows` を実行します。実機での狭い範囲の確認手順は [verification checklist](docs/verification-checklist.md) を参照してください。CSV をコミットしない pre-commit フックの例は `tools/hooks/pre-commit.sample` です。
