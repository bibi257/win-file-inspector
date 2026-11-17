# 設計書：Windows 11 オフライン検査プログラム（仮称 WinInspector）

> 注記：この設計書は知識ベースでまとめたもので、Windows 実機での確認はまだしていません。挙動が環境で変わり得る箇所には【要実機確認】を付けています。これらは 6章の CI と実機スモークテストで確かめます。

---

## 0. 結論

- **推奨言語：Windows PowerShell 5.1**（Windows 11 に標準搭載）。走査やレジストリ読み取りは .NET の API を直接呼びます。PowerShell 7 や Python は追加インストールが必要なので使いません。
- **全体構成**：ランチャー `Start-Inspector.cmd` から次の4つを順に動かします。
  1. 環境プローブ（最終アクセス日時の設定、権限、言語モード、ボリューム種別の確認）
  2. ファイル走査
  3. アプリ一覧と使用痕跡の収集
  4. 判定とログ出力
- **出力**：CSV（Excel で開く明細）と TXT（実行条件・環境・制約・中断状態の要約）の両方を出します。出力先の既定は OneDrive の同期対象外の `%LOCALAPPDATA%\WinInspector\Logs\<実行日時>\` です。
- **オフラインと読み取り専用の保証**：コードの構文（AST）を解析し、許可したコマンドと型だけを使っているかを CI で自動チェックします。禁止APIの一覧、動的実行の禁止、レビュー観点、CI での通信遮断状態での実行テストも組み合わせます。

---

## 0.5 ユーザー決定事項（2026-11-17）— 本文と矛盾する場合はこちらが優先

1. **言語は PowerShell の最新版（7系）にする**。0章・1章の推奨（5.1）から変更するユーザー決定。これに伴う対応：
   - 実行には PowerShell 7 のインストールが前提になる。README に導入手順を明記する（導入は一度きりで、プログラムの実行自体はオフラインのまま）。
   - ランチャーは `pwsh.exe` を呼び、起動前に環境変数 `POWERSHELL_TELEMETRY_OPTOUT=1` と `POWERSHELL_UPDATECHECK=Off` を設定し、テレメトリ送信と更新確認の通信を行わせない。
   - `Get-AppxPackage` の PowerShell 7 での動作は【要実機確認】とする。取得できない場合は推測で埋めず、summary と errors にその旨を明記する。
   - `.ps1` の文字コードは BOM 付き UTF-8 に統一する。CSV は LogWriter が BOM を明示的に書き出す。
2. **走査範囲の既定値**：`%USERPROFILE%` に加えて `C:\Program Files` と `C:\Program Files (x86)` も対象にする。
3. **未使用判定の既定日数**：365日（本文の180日から変更）。
4. **管理者用ランチャー**：用意する（`Start-Inspector-Admin.cmd`、自己昇格して Prefetch を根拠に使えるようにする）。
5. **最終アクセス日時**：本文の推奨どおり（信頼できない場合は空欄＋理由のみ。Raw 列はオプション）。
6. **フォルダ集計**：採用し、既定の深さは3。
7. **ログ出力先**：既定どおり `%LOCALAPPDATA%\WinInspector\Logs\`（実行終了時にパスを表示する）。
8. **リポジトリは public**。ただし本番環境の実ログは絶対に載せない（.gitignore、CI の成果物にも含めない、pre-commit フックのサンプルで多重に防ぐ）。
9. **実機スモークテスト**：狭い範囲のフォルダだけで実施する。チェックリストも小さい対象フォルダで実行する手順にする。

## 1. 言語選定

| 観点 | **Windows PowerShell 5.1** | PowerShell 7 | Python 3 | C#（標準搭載の csc.exe でコンパイル） |
|---|---|---|---|---|
| 追加インストールなしで動くか | ◎ 標準搭載 | × 要インストール | × 要インストール。exe 化すると署名なし exe となり、SmartScreen やウイルス対策に止められやすい | △ .NET Framework 4.8 の csc.exe でビルドはできるが、配布物が署名なし exe になる |
| 大量ファイル走査の速度 | ○ `Get-ChildItem -Recurse` は遅いが、.NET の列挙 API を直接使えば実用的な速度 | ◎ | ◎（`os.scandir`） | ◎ 最速 |
| レジストリの読み取り | ◎ `Microsoft.Win32.RegistryKey` で 32/64bit ビューを明示して読める | ◎ | ○ `winreg` | ◎ |
| Store アプリ（Appx）一覧 | ◎ `Get-AppxPackage` が使える | △ 互換レイヤー経由 | × 別手段が要る | △ WinRT 呼び出しが必要 |
| 保守性・可読性 | ○ テキストなのでユーザー自身が中身を読める | ○ | ◎ | △ 配布物がバイナリで中身を確認しにくい |
| テストのしやすさ | ○ Pester。純粋なロジックは Linux 上の pwsh でも実行できる | ○ | ◎ pytest | ○ |
| 通信の心配 | ◎ 5.1 には利用状況送信（テレメトリ）機能がない | △ テレメトリあり（オプトアウトが必要） | ○ | ◎ |

**推奨は PowerShell 5.1 です。** 決め手は「追加インストールなし」「Appx 一覧が標準で取れる」「テキストなのでユーザーが中身を監査できる」の3点です。弱点の走査速度は、.NET の API を直接使うことで補います。

### 実行ポリシー対策
- **ランチャー `Start-Inspector.cmd`**：`powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0src\Invoke-WinInspector.ps1" %*` で起動し、最後に `pause` を入れます。
  - `-ExecutionPolicy Bypass` はその実行1回限りの指定で、システムの設定は変えません。
  - `-NoProfile` を付けると、ユーザーのプロファイルスクリプトが混ざって動くことを防げます。
- **ZIP でダウンロードした場合**：ファイルに「インターネット由来」の印（MOTW）が付き、`.cmd` の実行時に SmartScreen の警告が出ることがあります。README に、ZIP を展開する前にプロパティ画面で「ブロックの解除」をする手順を書きます。`git clone` で取得した場合はこの印は付きません。
- **グループポリシーで実行ポリシーが強制されている PC**（会社支給機など）：コマンドラインの `-ExecutionPolicy` 指定より GPO が優先されるため実行できません。README に「実行できない環境」として明記します。
- **制約付き言語モード（AppLocker/WDAC）**：.NET の呼び出しが使えないため、起動直後に `$ExecutionContext.SessionState.LanguageMode` を確認し、`FullLanguage` でなければ理由を表示して終了します。
- 自己署名証明書でスクリプトに署名する方式は、利用者の手間が大きいので採用しません。

### PowerShell 5.1 固有の注意
- 日本語を含む `.ps1` は **BOM 付き UTF-8** で保存します。BOM がないと Shift_JIS として読まれ、文字化けします。CI で BOM の有無をチェックします。
- `.gitattributes` で `*.ps1` と `*.cmd` の改行を CRLF に固定します。`.cmd` が LF 改行だと誤動作することがあるためです。

---

## 2. 走査方式

### 使う API
- `System.IO.DirectoryInfo.EnumerateFileSystemInfos()` を、**再帰を使わず自前のスタックで**たどります。
  - `Get-ChildItem -Recurse` は使いません。遅いうえに、リパースポイント（リンク類）の扱いやエラー時の継続を細かく制御しにくいためです。
- サイズと3種類の日時は、ディレクトリ列挙の段階で返ってくる情報（WIN32_FIND_DATA）から取ります。
  - **ファイル本体は開きません。** そのため走査自体で最終アクセス日時が更新されることもありません。
  - `.Refresh()` も呼びません。
  - 注記：開かれている最中のファイルなどでは、列挙で得た値が実体とわずかにずれることがあります（README に記載）。
- 対象外にするもの：
  - **ネットワークドライブや UNC パス**：対象にすると SMB 通信が発生するため、起動時に `DriveInfo.DriveType` で `Network` を拒否し、`\\` で始まるパスも拒否します。
  - **リムーバブルドライブ**：既定では対象外にし、明示的に指定したときだけ走査します。

### シンボリックリンク・ジャンクション
- 属性に `ReparsePoint` を持つディレクトリは **中に入りません**。その1行を `Type=ReparsePoint` として記録するだけにします。無限ループと二重計上を防ぐためです。
  - 例：`C:\Users\<user>\Application Data` や `C:\Documents and Settings` はジャンクションです。
- ハードリンク（`WinSxS` など）は区別できないため、二重に計上されます。summary に注記します。
- OneDrive の「クラウドのみ」ファイル（属性 `RecallOnDataAccess` / `Offline`）：
  - 列挙してもダウンロードは発生しません。
  - サイズは論理サイズなので、属性の列を出して利用者が判別できるようにします。
  - ファイル内容は一切読みません（読むとダウンロードが走るため）。

### 権限エラー時の継続
- エラー処理はディレクトリ単位で行います。
- `UnauthorizedAccessException` / `PathTooLongException` / `IOException` / `DirectoryNotFoundException`（走査中に削除された場合）を捕捉し、`errors_*.csv` に記録してから次へ進みます。
- 列挙の途中で例外が出た場合は、そこまでに取れた結果を残します。
- 総エラー件数は summary に出します。

### 長いパス
- 内部では `\\?\C:\...` という拡張パス形式で扱い、出力時に接頭辞を外します（.NET Framework 4.6.2 以降で利用可）。
- `LongPathsEnabled` の設定値にかかわらず動くことを狙います。【要実機確認：CI で 300 文字超のフィクスチャを使い検証】

### 想定ファイル数と所要時間（目安、実測で補正します）
| 範囲 | 想定ファイル数 | 目安時間（SSD） |
|---|---|---|
| ユーザープロファイル配下 | 10万〜50万 | 1〜5分 |
| C ドライブ全体 | 50万〜200万 | 5〜20分 |

- PowerShell 5.1 のループ処理が律速で、1秒あたり数千〜2万件程度と見込みます。CI で合成した10万件の走査を計測して目安を更新します。
- メモリ：結果をため込まず、`StreamWriter` で1行ずつ書き出します。`Export-Csv` に全件を渡す方式は使いません。

### 進捗表示と中断
- **進捗表示は必要です。** `Write-Progress` で件数、現在のフォルダ、経過時間を出します。毎件呼ぶと遅くなるため、**1秒に1回程度**に間引きます。
- **中断**：Ctrl+C を押すと、`finally` ブロックで書き込み途中の内容を確定させ、summary に「中断（N件まで）」と書きます。中断した地点からの再開機能は v1 では持ちません。
- オプションで自プロセスの優先度を `BelowNormal` に下げられるようにします（PC 操作の妨げを減らすため）。

### フォルダ単位の合計（提案：採用）
- 実用性が大きく上がるので採用を提案します。走査中にフォルダを抜けるタイミング（後順）で、合計を親フォルダへ足し上げます。
- `folders_*.csv` に出す内容：直下のサイズ、配下全体のサイズ、ファイル数、深さ。
- 既定では **深さ3まで** を出力し、件数が膨らみすぎないようにします（深さは設定で変更可）。

### 最終アクセス日時（制約3への対応）
**1. 環境の確認方法**
- レジストリ `HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\NtfsDisableLastAccessUpdate`（DWORD）を読みます。管理者権限は不要です。
- 外部コマンドの `fsutil` は呼びません。値の意味は `fsutil behavior query disablelastaccess` と同じです。
  - `0`：ユーザー管理・有効
  - `1`：ユーザー管理・無効
  - `0x80000002`：システム管理・有効
  - `0x80000003`：システム管理・無効
  - 値がない：OS 既定（システム管理）として「不明」扱い
- ボリュームごとのファイルシステム（`DriveInfo.DriveFormat`）も記録します。この設定が効くのは NTFS だけです。

**2. ログへの書き方**
- `LastAccessTime` 列には、状態が「NTFS かつ更新有効」のときだけ値を入れます。
- それ以外の場合は空欄にし、`LastAccessStatus` 列に理由を書きます。例：`更新無効(NtfsDisableLastAccessUpdate=0x80000003)`、`非NTFS(FAT32)`、`設定不明`。
- 記録上の古い値をそのまま見たい場合だけ、オプション `-IncludeRawLastAccess` で別列 `LastAccessTime_Raw` に出します。

**3. summary.txt に明記する注意**
- 更新が有効でも、記録の精度は最大1時間です。
- ウイルス対策ソフト、バックアップ、検索インデクサがファイルに触れても更新されるため、「アクセスあり＝ユーザーが使った」とは言えません。
- 設定を変えても、再起動するまで反映されません。

---

## 3. アプリ検査と「長期間使われていない候補」の判定

### 3.1 一覧の取得元
| 取得元 | 内容 | 権限 |
|---|---|---|
| `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`（64bit ビュー） | 全ユーザー向けのデスクトップアプリ | 一般ユーザーで可 |
| 同上（32bit ビュー＝`WOW6432Node`） | 32bit アプリ | 一般ユーザーで可 |
| `HKCU\...\Uninstall` | ユーザー単位でインストールされたアプリ（Teams、VS Code のユーザー版など） | 一般ユーザーで可 |
| `Get-AppxPackage`（現在のユーザー） | Store アプリ | 一般ユーザーで可 |

- **読む値**：`DisplayName`, `DisplayVersion`, `Publisher`, `InstallDate`（YYYYMMDD 形式で、欠けていることが多い）, `EstimatedSize`（KB 単位で、欠けていたり不正確なことが多い）, `InstallLocation`, `DisplayIcon`, `UninstallString`, `SystemComponent`, `ParentKeyName`, `ReleaseType`, `WindowsInstaller`
- 値が欠けている項目は空欄にし、どこから取った値かを `*Source` 列に記録します。
  - サイズの補完：`EstimatedSize` がなく `InstallLocation` がある場合だけ、そのフォルダの実測合計で補い、`SizeSource=FolderSum` とします。
- **使ってはいけないもの**：
  - `Win32_Product`（WMI）：呼ぶと MSI の整合性チェックと自動修復が走り、**読み取り専用の制約に反します**。
  - `Get-Package`：パッケージプロバイダの取得のために通信しようとすることがあります。

### 3.2 実行履歴の手がかり
| 手がかり | 分かること | 権限 | 採否 |
|---|---|---|---|
| **UserAssist**（`HKCU\...\Explorer\UserAssist\{GUID}\Count`） | エクスプローラー経由（スタートメニュー、デスクトップ、ダブルクリック）で起動した exe・ショートカット・Store アプリの最終実行日時と回数。値の名前は ROT13 で符号化され、データは 72 バイトのバイナリ（最終実行の FILETIME はオフセット 60） | 不要（現在のユーザー分のみ） | **採用（主な根拠）** |
| **Prefetch**（`C:\Windows\Prefetch\*.pf`） | 各 `.pf` の更新日時 ≒ その exe の最終実行日時（最大1024件、古いものから消える） | **管理者が必要** | **採用（管理者実行時のみ）**。圧縮された .pf の中身の解析はしません（P/Invoke が要り、複雑になるため）。ファイル名と更新日時だけ使います |
| BAM（`HKLM\SYSTEM\...\bam\State\UserSettings`） | exe の最終実行日時 | 要管理者の可能性【要実機確認】 | 不採用。約7日で消えるため「長期間未使用」の判定には向かない |
| Amcache.hve / SRUM | 詳しい履歴 | 管理者が必要、かつファイルがロックされている | 不採用。読むのにボリュームシャドウコピーなどが必要で、範囲が過剰 |
| exe の最終アクセス日時 | — | — | 不採用。2章のとおり信頼できない |

**UserAssist の限界（summary に明記）**
- コマンドラインや他のプログラムから起動した場合は記録されません。
- 記録されるのは現在のユーザー分だけです。
- 設定「アプリの起動を追跡」（`Start_TrackProgs=0`）がオフだと記録されません。この設定値も読んで summary に出します。
- OS を再インストールすると履歴は消えます。

**アプリと履歴の対応付け**（どの方法で対応付けたかを `MatchMethod` 列に記録）
- UserAssist の exe パス（`{6D809377-...}\...` のような既知フォルダの GUID を実パスに変換）が `InstallLocation` の配下にあれば、そのアプリの記録とみなします。
- `InstallLocation` がない場合は、`DisplayIcon` か `UninstallString` に書かれた exe のフォルダで代用します。
- ショートカット（.lnk）の記録：`WScript.Shell` の `CreateShortcut().TargetPath` でリンク先を読み取るだけにします。`.Save()` は呼びません。許可リストで例外として明示し、レビューでも確認する箇所です。
- Store アプリ：UserAssist にはアプリ ID（AUMID、例 `Microsoft.WindowsCalculator_8wekyb3d8bbwe!App`）で記録されるので、パッケージファミリ名と突き合わせます。

### 3.3 Store アプリ（Appx）の扱い
- 取得する項目：`Name`, `Version`, `Publisher`, `InstallLocation`, `SignatureKind`, `IsFramework`, `NonRemovable`, `IsResourcePackage`
- 候補から外すもの：`SignatureKind=System`、フレームワーク、リソースパッケージ、`NonRemovable`
- インストール日：確かな取得元がないため **空欄** にします。フォルダの作成日時で代用することはしません。
- サイズ：`InstallLocation` の実測合計で補います。ただし `WindowsApps` 配下は一般ユーザーで読めないことがあり、その場合は空欄に理由を添えます【要実機確認】。

### 3.4 判定基準（案）
| 区分 | 条件 | 意味 |
|---|---|---|
| **候補A** | 最終実行記録（UserAssist / Prefetch のうち新しい方）が **N日以上前**（既定 N=180） | 根拠あり |
| **候補B** | 実行記録がなく、追跡設定は有効で、インストール日が N日以上前 | 「未使用の可能性」にとどまり、根拠は弱い |
| 判定不能 | 対応付けができない、追跡設定が無効、インストール日が不明 など | 推測で候補に入れない |
| 対象外 | 下記の除外条件に当たる | 一覧には残し、`ExcludedReason` 列に理由を書く |

- 各区分の中では **サイズの大きい順** に並べ、サイズ不明は末尾に回します。
- 出力は `app_candidates_*.csv`（候補AとBのみ）と、全件を載せる `apps_*.csv` に分けます。

### 3.5 誤判定を避けるための除外（隠さず理由付きで「対象外」にする）
- `SystemComponent=1`、`ParentKeyName` がある（親アプリの部品）、`ReleaseType` が Update / Hotfix / Security Update、`DisplayName` が `KB\d+` を含むもの
- 名前で判定するもの（設定ファイルで編集可）：Visual C++ 再頒布可能パッケージ、.NET 各種ランタイム、DirectX、Windows SDK / WDK、"Driver"・"Chipset"・"Audio"・"Graphics" と主要ハードウェアメーカー名の組み合わせ、Microsoft Edge / WebView2、Microsoft Update Health Tools
- ドライバやランタイムは UserAssist に記録が残らない性質のため、除外しないと必ず「未使用」に見えてしまいます。

---

## 4. ログ形式

### 役割分担（両方出す）
- **CSV**：Excel で絞り込み・並べ替えをするための明細
- **TXT**（`summary_*.txt`）：人が最初に読む要約。実行条件、環境、最終アクセス設定、権限、件数、エラー数、所要時間、完了/中断、候補の上位、制約事項を書きます。

### ファイル一覧（1回の実行で1フォルダ）
`%LOCALAPPDATA%\WinInspector\Logs\20261117-153000\` の中に次のファイルを作ります。
- `summary_20261117-153000.txt`
- `files_…csv`
- `folders_…csv`
- `apps_…csv`
- `app_candidates_…csv`
- `errors_…csv`

ファイル名にホスト名やユーザー名は入れません（ファイル名自体が個人情報になるため）。

### 出力先の決め方
- **既定**：`%LOCALAPPDATA%` 配下にします。
  - `Documents` は OneDrive の「フォルダーのバックアップ」でクラウドに同期されていることがあり、ログがアップロードされてしまうため避けます。
  - スクリプトと同じ場所も避けます。リポジトリ内にログが混入しかねないためです。
- 出力先フォルダは自動的に走査対象から除外します。
- 実行が終わったら、出力先フォルダのパスを表示します。

### 列の定義
- **files**
  - `Path`, `Type`(File/ReparsePoint), `SizeBytes`, `CreationTime`, `LastWriteTime`, `LastAccessTime`, `LastAccessStatus`, `Attributes`, `Extension`
  - `LastAccessTime_Raw` はオプション指定時のみ
- **folders**
  - `Path`, `Depth`, `DirectSizeBytes`, `TotalSizeBytes`, `FileCount`, `HadErrors`
- **apps**
  - `Source`(HKLM64/HKLM32/HKCU/Appx), `Name`, `Version`, `Publisher`
  - `InstallDate`, `InstallDateSource`, `SizeBytes`, `SizeSource`, `InstallLocation`
  - `LastRunTime`, `LastRunSource`(UserAssist/Prefetch/なし), `RunCount`, `MatchMethod`
  - `Status`(候補A/候補B/判定不能/対象外), `ExcludedReason`
- **errors**
  - `Path`, `Operation`, `ErrorType`, `Message`

### 文字コードと書式
- **BOM 付き UTF-8、改行は CRLF** にします。この形式なら Excel でダブルクリックしても文字化けしません。Shift_JIS は表現できない文字が欠けるので使いません。
- 日時：`yyyy-MM-dd HH:mm:ss`（ローカル時刻）とし、タイムゾーンは summary に書きます。
- **CSV インジェクション対策**：Excel は `= + - @` で始まるセルを数式として解釈します。
  - `Path` 列はドライブ文字で始まるので該当しません。
  - アプリ名など先頭が自由なテキストの列は、該当するときだけ先頭に `'` を付けます。この加工は README に明記します。

---

## 5. 実行形態と設定

- **対象範囲の既定**：**`%USERPROFILE%` 配下（推奨）**。個人データの大半がここにあり、管理者権限なしでほぼ全部読めます。C ドライブ全体はオプション（`-Target C:\`）にします。
- **既定で除外するフォルダ**：
  - 出力先フォルダ（常に除外）
  - C ドライブ全体を走査するときの `$Recycle.Bin` と `System Volume Information`
  - `Windows\WinSxS`（ハードリンクだらけで合計が大きく膨らむため。オプションで含められる）
  - `AppData` は既定で含めます。容量の大きな原因になりやすい場所だからです。
- **管理者権限**：既定は **一般ユーザーで実行** します。
  - 管理者で実行すると、Prefetch を根拠に使えるようになり、他ユーザーのフォルダや Program Files 配下の一部も読めるようになります。
  - その分、誤操作したときの影響も大きくなりますが、読み取り専用はコードの構造で担保しています（6章参照）。
  - 管理者で実行するかどうかは summary に記録します。
  - 管理者用のランチャーを別に用意するかどうかは確認点にします。
- **設定ファイル**：任意の `config.psd1` を読みます。
  - `Import-PowerShellDataFile` で読むので、設定ファイルにコードを書いても実行されません。
  - コマンドライン引数が設定ファイルより優先されます。
  - 主な引数：`-Mode Files|Apps|All`、`-Target`、`-Exclude`、`-OutputDir`、`-UnusedDays`、`-FolderDepth`、`-IncludeRawLastAccess`、`-IncludeRemovable`

---

## 6. オフライン保証・読み取り専用保証と、テスト・検証の方法

### 6.1 オフラインの保証（制約1）
1. **許可リスト方式の静的検査**
   - PowerShell のパーサー（`[System.Management.Automation.Language.Parser]::ParseFile`）で全スクリプトを解析し、使っているコマンド・.NET 型・COM の ProgID を抜き出します。
   - それらを `tools/allowlist.psd1` と照合し、**許可リストにないものが1つでもあれば CI を失敗** させます。禁止リストより漏れが少ない方式です。
2. **動的実行の禁止**
   - `Invoke-Expression`、`[scriptblock]::Create`、`& $変数` での呼び出し、`Add-Type`、`New-Object -ComObject` の動的な ProgID、`Start-Process`、外部 exe の呼び出しを禁止します。
   - 動的実行があると静的検査をすり抜けられるため、まとめて禁止して検査の前提を守ります。
3. **明示的に禁止するもの**（README とレビュー観点にも記載）
   - Web 通信：`Invoke-WebRequest`/`Invoke-RestMethod`、`System.Net.*`（WebClient, HttpClient, Sockets, Dns）
   - 疎通確認・名前解決：`Test-Connection`/`Test-NetConnection`/`Resolve-DnsName`
   - ファイル転送・メール：`Start-BitsTransfer`、`Send-MailMessage`
   - モジュール・パッケージ取得：`Install-/Find-/Update-Module`、`Update-Help`、`*-Package*`、`Install-PackageProvider`
   - リモート実行：`-ComputerName` / `-Session` 付きのコマンド
   - 通信系 COM：`MSXML2.XMLHTTP`、`WinHttp.*`、`InternetExplorer.Application`
   - 署名検証：`Get-AuthenticodeSignature`（失効確認のため通信し得る）
   - UNC パスやネットワークドライブへのアクセス
4. **実行時の検証（CI）**
   - Windows ランナーで、`powershell.exe` の外向き通信を遮断するファイアウォール規則を追加し、ドロップのログを有効にしてから全モードを実行します。
   - 「実行が成功すること」と「ドロップログに対象プロセスの記録が0件であること」を確かめます。【要実機確認：ランナー上でのファイアウォールログ取得の可否】
5. **保証の範囲を明記**
   - 保証するのは「本プログラムが通信を発生させないこと」です。
   - OS 側の動作（Defender のクラウド照会や OneDrive の同期など）は範囲外で、その旨を README に書きます。

### 6.2 読み取り専用の保証（制約2）
- 許可リストに含めないもの：`Remove-*`、`Set-*`（自プロセスの優先度設定を除く）、`New-Item*`（出力先の作成を除く）、`Rename-*`、`Move-*`、`Clear-*`、`*-Acl`、`Remove-AppxPackage`、`msiexec`、`reg.exe`
- .NET の書き込み系メソッドも許可しません：`RegistryKey.SetValue/DeleteValue`、`OpenSubKey(name, $true)`、`File.Delete`、`File.Set*Time` など
- レジストリは `RegistryKey.OpenBaseKey(..., Registry64/32)` から、`OpenSubKey(name, $false)` で **書き込み不可を明示して** 開きます。
- ファイルへの書き込みは `LogWriter` モジュールだけで行い、書き込み先は出力先フォルダの配下に限ります。書く前にパスが出力先配下かを確かめます。

### 6.3 テスト・検証の段階

**1. Linux 上（pwsh 7 + Pester 5）**
- Windows に依存しないロジックをテストします。
  - ROT13 の復号、UserAssist バイナリの解析、FILETIME の変換
  - 最終アクセス設定値の解釈、判定ルール、除外ルール
  - CSV のエスケープとインジェクション対策、パスの正規化
  - AST による許可リスト検査、BOM と CRLF の検査
- 収集処理は「データの取得元」を引数で差し替えられる設計にして、テストではダミーデータを渡します。

**2. GitHub Actions の Windows ランナー**（`windows-latest`、`shell: powershell` で 5.1 を指定）
- ランナーは Windows Server で、Windows 11 ではありません。インストール済みアプリや最終アクセス設定の既定値が違う前提で使います。
- テスト用フィクスチャはテストコードの側で作ります（作成処理はプログラム本体には含めません）。
  - ファイル：シンボリックリンク、ジャンクション、300文字超のパス、`icacls` でアクセスを拒否したフォルダ、日本語・絵文字・カンマ・引用符・`=` を含むファイル名、`SetCreationTime` で日時を固定したファイル
  - レジストリ：`HKCU:\Software\WinInspectorTest\` の下に作ったダミーのアンインストールキーと UserAssist データを、取得元を差し替えて読ませます。
- 確認すること：
  - 期待どおりの CSV が出ること
  - 走査対象のファイルの日時が実行前後で変わらないこと（読み取り専用の回帰テスト）
  - 合成した10万件の走査時間
  - 6.1 の通信遮断状態での実行
- 費用：Windows ランナーはプライベートリポジトリだと分数消費が2倍になります（パブリックなら無料）。
- **CI の成果物に実データのログは含めません。** フィクスチャで生成したログも原則アップロードしません。

**3. ユーザーの Windows 11 実機でのスモークテスト**
- `docs/verification-checklist.md` の手順に沿って行います。
  - summary の最終アクセス設定が `fsutil behavior query disablelastaccess` の結果と一致するか
  - 任意のファイル数件の日時とサイズがエクスプローラーのプロパティ表示と一致するか
  - 「設定 > アプリ」の一覧と件数がおおむね一致するか
  - 機内モードで実行しても完走するか
- 結果は「OK/NG」と差分の説明だけを返してもらい、ログそのものは共有しない運用にします。

---

## 7. リポジトリ構成案

```
win-file-inspector/
├─ Start-Inspector.cmd          # ランチャー（CRLF）
├─ src/
│  ├─ Invoke-WinInspector.ps1   # エントリポイント・引数処理
│  └─ lib/
│     ├─ EnvironmentProbe.ps1   # 最終アクセス設定・権限・言語モード・ボリューム
│     ├─ FileScanner.ps1        # 走査・フォルダ集計
│     ├─ AppInventory.ps1       # Uninstall キー・Appx
│     ├─ UsageEvidence.ps1      # UserAssist・Prefetch
│     ├─ Classifier.ps1         # 候補判定・除外ルール
│     └─ LogWriter.ps1          # 唯一の書き込み箇所
├─ config/
│  ├─ config.sample.psd1
│  └─ exclusions.psd1           # アプリ除外の名前パターン
├─ tests/
│  ├─ unit/                     # Linux 上の pwsh でも実行可
│  ├─ windows/                  # Windows ランナー専用
│  └─ static/                   # 許可リスト（オフライン・読み取り専用）、BOM/CRLF の検査
├─ tools/allowlist.psd1
├─ docs/
│  ├─ design.md                 # 本設計書
│  └─ verification-checklist.md
├─ .github/workflows/ci.yml
├─ .gitignore                   # *.csv, *.txt（docs 配下を除く）, Logs/ など
└─ .gitattributes               # *.ps1 / *.cmd を CRLF に固定
```

- 補助策として、`*.csv` のコミットを拒否する pre-commit フックのサンプルを付けます。

**README に書くこと**
1. 何をするか / しないか：読み取り専用で、削除や変更は一切しない
2. **完全オフラインであること**：通信しないことの保証方法（6.1）と、保証の範囲外（OS 側の動作）
3. 動作環境：Windows 11 と標準の PowerShell 5.1 だけで動き、追加インストールは不要
4. 使い方：`Start-Inspector.cmd` をダブルクリックする方法と引数の例。MOTW の「ブロックの解除」。GPO や制約付き言語モードで実行できないこと
5. 出力内容：ファイル一覧、列の説明、最終アクセス日時が空欄になる理由、候補区分の意味と限界（UserAssist の制約）
6. **ログの取り扱い注意**：個人情報を含むこと。リポジトリやクラウド同期フォルダに置かない、共有しないこと。不要になったら削除すること（削除は利用者自身の手で行う）
7. 所要時間の目安と中断方法

---

## ユーザーへの確認点

1. **走査範囲の既定値**：ユーザープロファイル配下（推奨）と C ドライブ全体のどちらにしますか。
2. **未使用と判定する日数**：既定を 180日にしてよいですか。
3. **管理者実行**：Prefetch を根拠に加えるための「管理者用ランチャー」を用意しますか。一般ユーザーでの実行だけにしますか。
4. **最終アクセス日時が信頼できない場合**：空欄＋理由だけ（推奨）にしますか。記録上の古い値も別列に出す方式を既定にしますか。
5. **フォルダ集計**：採用してよいですか。既定の深さは 3 でよいですか。
6. **ログの出力先**：既定を `%LOCALAPPDATA%\WinInspector\Logs\` にしてよいですか。
7. **リポジトリの公開範囲**：パブリックにしますか、プライベートにしますか（CI の Windows ランナーの費用が変わります）。
8. **実機スモークテスト**：Windows 11 実機でのスモークテストを実施していただけますか（結果は OK/NG と差分の説明だけで構いません）。
