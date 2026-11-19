# 設計書：WinInspector.exe の GUI 設定画面

> 注記：この設計書は `feat/gui-settings` ブランチ（3b66b6c）の `docs/design-csharp.md`、`docs/design.md`、`src/WinInspector.Cli/`、`src/WinInspector.Core/`、`src/WinInspector.Windows/Process/Elevation.cs`、`tests/WinInspector.StaticChecks/Program.cs`、`tools/BannedSymbols.txt`、`tools/csharp-allowlist.json`、`.github/workflows/ci.yml` を読んで作成しました。Windows 実機での確認はまだしていません。環境によって挙動が変わる箇所には【要実機確認】を付けています。コードは変更していません。
>
> 優先順位：`docs/design-csharp.md` 0.6 節のユーザー決定事項が最優先です。出力仕様の正は `docs/design.md` です。この設計書はそのどちらも変更しません。

---

## 0. 結論とユーザー確認点

### 0.1 結論（推奨案の要約）

- **起動分岐**：引数なしで、かつ対話的な起動（標準入出力がリダイレクトされていない）なら GUI を開きます。引数があれば今までどおり CLI です。明示的に GUI を開く `-Gui` フラグも追加します。管理者として再起動するときにも、このフラグで GUI を開き直します。
- **UI 技術**：WinForms。GUI のコードは `WinInspector.Cli`（exe）の `Gui/` フォルダに置きます。設定の保存、検証、`CliOptions` への変換は UI から切り離して `WinInspector.Core/Gui/` に置き、Linux で単体テストします。
- **設定の保存**：`%LOCALAPPDATA%\WinInspector\gui-settings.json`（JSON、スキーマ番号つき）。書き込みには既存の唯一の書き込み口 `OutputFileWriter` を使うので、書き込み許可箇所は増えません。優先順位は「GUI の保存値 > config.psd1 > 既定値」です。GUI に出していない項目（IncludeRawLastAccess など）は、今までどおり config.psd1 から取ります。
- **実行**：`Orchestrator` に任意引数の進捗コールバック（`IProgress<InspectionProgress>?`）と、結果を返す新メソッド `Execute` を追加します。既存の `Run(options, token)` は残し、戻り値も変えません。GUI は `Task.Run` で裏で実行し、中止ボタンは既存の `CancellationToken` で止めます。
- **管理者として実行**：昇格するのは GUI 自体です。既存の `Elevation.Relaunch` に「終了を待たない」引数を足して再利用します。設定は引数ファイル（既存の `-ArgumentsFile` 方式）で渡し、昇格後の GUI は受け取った設定ですぐに実行を始めます。
- **コンソールウィンドウ**：`OutputType` は **Exe のまま**にします。GUI 起動時に、コンソールを自分だけが使っているとき（ダブルクリック起動）に限り `FreeConsole`（kernel32）で切り離して閉じます。CLI の終了コード、標準出力、CI の実行方法は何も変わりません。
- **出力フォルダを開く**：`WinInspector.Windows/Process/FolderOpener.cs` を新しく作り、`%SystemRoot%\explorer.exe` に検証済みのフォルダパス 1 つだけを渡します。ここを `Process.Start` の 2 つ目の許可箇所にし、静的検査に専用の契約を加えます。

### 0.2 ユーザー確認点（推奨つき）

| # | 確認点 | 選択肢 | 推奨 |
|---|---|---|---|
| 1 | **コンソールウィンドウの扱い**（6 章） | A：Exe のまま。GUI 起動時に自分専用のコンソールだけを `FreeConsole` で閉じる／B：WinExe に変える／C：GUI 専用の exe を別に作る | **A**。CLI の互換を完全に保てる。代わりに、ダブルクリック時にコンソールが一瞬見える |
| 2 | **設定の保存先**（3 章） | A：`%LOCALAPPDATA%\WinInspector\gui-settings.json`／B：exe の隣の `config\gui-settings.json`／C：レジストリ（HKCU） | **A**。どこに exe を置いても書き込めて、ユーザーごとに分かれる。C はレジストリへの書き込みが禁止なので不可 |
| 3 | **引数なし起動の意味が変わること** | 今まで「既定設定で全検査＋Enter 待ち」だった動作が「GUI を開く」に変わる。従来の動作は `WinInspector.exe -Mode All` で得られる | **変更を受け入れる**（ユーザーの要望どおり）。リダイレクト時や非対話セッションでは従来の CLI 動作を続ける（1.2） |
| 4 | **除外フォルダ（Exclude）を GUI に出すか** | 出す／出さない（config.psd1 の値だけを使う） | **出す**。summary の「設定」行にも出る項目で、対象リストと同じ部品で作れる。初期値は空 |
| 5 | **管理者として実行の方式**（5 章） | A：GUI 自体を昇格して開き直す／B：GUI は通常権限のまま、検査だけを昇格した CLI で動かす | **A**。進捗表示と中止ボタンがそのまま使える。B では通常権限の GUI から昇格プロセスを止められない |
| 6 | **フォルダ選択ダイアログの OS 側の記録**（7.4） | Windows のダイアログは、最近使った場所を HKCU に記録することがある【要実機確認】。A：README に OS 側の動作として書く／B：ダイアログを使わず、パスの手入力だけにする | **A**。実機でどの値が書かれるかを Process Monitor で確かめてから README に書く。保証の対象は「検査処理が変更しないこと」のまま |
| 7 | **入力範囲**（2.3） | 未使用日数 1〜3650、フォルダ深さ 0〜10 | **この範囲で採用**。CLI は負の値も受け付けるが、GUI では意味のある範囲に絞る。CLI の仕様は変えない |
| 8 | **`-Help` の表示に `-Gui` を加えるか** | 加える／加えない | **加える**。`-Help` の出力が 1 語増えるだけで、終了コードは同じ |
| 9 | **「優先度を下げる」（LowerPriority）を GUI に出すか** | 出す／出さない | **今回は出さない**。要望の範囲外。必要なら同じ方式で後から足せる |

### 0.3 ユーザー決定事項（2026-11-18）

ユーザーが 0.2 の確認点すべてを推奨どおりで承認しました。本文と矛盾する場合は、この節が優先します。

| # | 決定 |
|---|---|
| 1 | コンソールは A（Exe のまま、GUI 起動時に自分専用のコンソールだけを FreeConsole で閉じる） |
| 2 | 設定の保存先は A（`%LOCALAPPDATA%\WinInspector\gui-settings.json`） |
| 3 | 引数なし起動が「GUI を開く」に変わることを受け入れる |
| 4 | 除外フォルダ（Exclude）も GUI に出す |
| 5 | 管理者として実行は A（GUI 自体を昇格して開き直す） |
| 6 | フォルダ選択ダイアログの OS 側の記録は A（実機確認のうえ README に明記） |
| 7 | 入力範囲は未使用日数 1〜3650、フォルダ深さ 0〜10 |
| 8 | `-Help` の表示に `-Gui` を加える |
| 9 | 「優先度を下げる」（LowerPriority）は今回 GUI に出さない |

実装担当は Haiku、レビュー担当は Claude Code（ユーザー決定 2026-11-18）。

---

## 1. 起動分岐

### 1.1 判定の順序（`Program.Main`）

```
1. args が「-ArgumentsFile <path>」の 2 つなら、ファイルから args を読み直す（既存の処理）
2. args に「-Gui」が含まれる                          → GUI（2 章）
3. args が空 かつ 対話的な起動（1.2 の条件をすべて満たす）→ GUI
4. それ以外                                            → 今までどおり CLI
```

- 3 の GUI 起動を「対話的な起動」に限るのは、タスク スケジューラやサービス、パイプからの実行で、誰も操作できないウィンドウが出て止まるのを防ぐためです。
- 2 の `-Gui` は、引数ファイルから読み直した後の args に対しても判定します（昇格後の GUI 起動で使います。5 章）。
- 4 の CLI の流れ（`CliParser.Parse` → `SettingsComposition.Apply` → … → 終了コード）は 1 行も変えません。args が空で CLI に回った場合の Enter 待ちの条件（`!NoPause && !Console.IsInputRedirected && args.Length==0`）もそのままです。

### 1.2 「対話的な起動」の条件

次をすべて満たすときだけ、引数なしで GUI を開きます。

| 条件 | 理由 |
|---|---|
| `Environment.UserInteractive == true` | サービスや、デスクトップのないセッションを除く |
| `Console.IsInputRedirected == false` | パイプや CI からの実行を除く（今までの CLI 動作を続ける） |
| `Console.IsOutputRedirected == false` かつ `Console.IsErrorRedirected == false` | 出力を取り込んでいる呼び出し側（スクリプト）を除く |

### 1.3 `-Gui` フラグの仕様

| 形 | 動作 |
|---|---|
| `-Gui` | GUI を開く。初期値は保存した設定（3 章） |
| `-Gui` と他の CLI オプション（`-Mode`、`-Target` など） | 指定した値を初期値にして GUI を開く。設定ファイルには、実行するまで保存しない |
| `-GuiAutoRun`（内部用。ヘルプに出さない） | `-Gui` と一緒に使ったときだけ有効。GUI を開いてすぐに実行を始める。昇格後の再起動専用（5 章） |
| `-Gui` と `-Help` / `-Version` | CLI と同じく表示して終了（`-Help` / `-Version` を優先） |
| `-Gui` と `-Elevate` | `-Elevate` は「管理者として実行」チェックの初期値として扱う（その場では昇格しない） |

- 解釈の手順：`-Gui` と `-GuiAutoRun` を取り除き、残りを既存の `CliParser.Parse` に渡します。`CliParser` 自体は変えません。引数エラーのときは、CLI と同じ文言をメッセージボックスで表示し、終了コード 1 で終わります。
- `-Gui` は今まで「不明な引数」（終了コード 1）でした。新しいフラグとして意味を持つようになりますが、既存の正しい呼び出しの動作は変わりません。

### 1.4 スレッド（STA）

- WinForms には STA スレッドが必要です。しかし `Main` に `[STAThread]` を付けると、CLI の実行も STA になり、COM（IShellLink）や WinRT（PackageManager）の動作が今と変わるおそれがあります。
- そこで `Main` は今のまま（MTA）にします。GUI のときだけ新しいスレッドを作り、`SetApartmentState(ApartmentState.STA)` を設定して、そこで `Application.Run` を呼んで `Join` で待ちます。
- 検査本体は `Task.Run`（スレッドプール、MTA）で動かします。CLI と同じ条件で `Orchestrator` が動くので、COM と WinRT の動作が CLI と揃います。

---

## 2. 画面仕様（WinForms）

### 2.1 画面構成

1 つのウィンドウ（`MainForm`）に、上から「設定」「実行」「結果」の 3 つの領域を並べます。タブや別ウィンドウは使いません。

```
┌ WinInspector ───────────────────────────────────────────┐
│ ⚠ 設定ファイル config.psd1 に未対応の構文があります（行 3）│ ← 警告帯（必要なときだけ）
│ ── 検査の設定 ─────────────────────────────────────── │
│ モード      (●) すべて  ( ) ファイルのみ  ( ) アプリのみ       │
│ 対象フォルダ ┌───────────────────────────┐ [追加…]        │
│             │ C:\Users\xxx                │ [削除]         │
│             │ C:\Program Files            │ [既定に戻す]    │
│             └───────────────────────────┘                │
│ 除外フォルダ ┌───────────────────────────┐ [追加…] [削除] │
│             └───────────────────────────┘                │
│ 出力先      [C:\Users\xxx\AppData\Local\WinInspector\Logs] [参照…] [既定に戻す] │
│ 未使用と判定する日数 [ 365 ] 日    フォルダ集計の深さ [ 3 ] 階層 │
│ [✓] 管理者として実行（Prefetch も根拠に使います）            │
│ ── 実行 ───────────────────────────────────────────── │
│ [検査を開始]  [中止]                                        │
│ ファイルを走査しています… 12,345 件  経過 00:01:23            │
│ C:\Users\xxx\Documents\...（現在のフォルダ）                 │
│ ── 結果 ───────────────────────────────────────────── │
│ 状態：完了                                                  │
│ ┌ summary の全文（読み取り専用）────────────────────┐       │
│ └──────────────────────────────────────────┘       │
│ [出力フォルダを開く]  [summary をコピー]                      │
└──────────────────────────────────────────────────────────┘
```

- ウィンドウの大きさは変えられるようにし、対象リストと summary 欄が伸び縮みするようにします（`TableLayoutPanel` と `Anchor`）。
- フォントは既定の UI フォント（日本語環境では Yu Gothic UI）。summary 欄は等幅にしません。行が長いので折り返しを有効にします。
- DPI：マニフェストは `dpiAware=true`（システム DPI）のままにし、`Application.SetHighDpiMode(HighDpiMode.SystemAware)` で揃えます。マニフェストは変えません。

### 2.2 コントロールと CliOptions の対応

| コントロール | 文言 | CliOptions | 初期値（保存値がないとき） |
|---|---|---|---|
| ラジオボタン 3 つ | すべて／ファイルのみ／アプリのみ | `Mode` = `All` / `Files` / `Apps` | config.psd1 の `Mode`、なければ `All` |
| リスト＋[追加…][削除][既定に戻す] | 対象フォルダ | `Targets` | config.psd1 の `Target`。空なら既定の 3 か所（存在するものだけ。2.4） |
| リスト＋[追加…][削除] | 除外フォルダ | `Excludes` | config.psd1 の `Exclude`、なければ空 |
| テキストボックス＋[参照…][既定に戻す] | 出力先 | `OutputDir` | config.psd1 の `OutputDir`（空文字は未指定）、なければ `%LOCALAPPDATA%\WinInspector\Logs` を展開したパス |
| NumericUpDown | 未使用と判定する日数 | `UnusedDays` | config.psd1、なければ 365 |
| NumericUpDown | フォルダ集計の深さ | `FolderDepth` | config.psd1、なければ 3 |
| チェックボックス | 管理者として実行 | （GUI の再起動に使う。5 章） | オフ。既に管理者なら「オン・変更不可」で「管理者として実行中」と表示 |
| （画面に出さない） | — | `IncludeRawLastAccess`、`IncludeRemovable` | config.psd1、なければ false |
| （画面に出さない） | — | `NoPause`=true、`Elevate`=false、`LowerPriority`=false、`ShowHelp`/`ShowVersion`=false | 固定 |
| （画面に出さない） | — | `ConfigWarning` | config.psd1 の読み取り警告をそのまま渡す（errors と summary に CLI と同じく残る） |

- モードに応じて、使わない項目を無効（灰色）にします。値は消さずに残します。
  - ファイルのみ：「未使用と判定する日数」を無効にする。
  - アプリのみ：「対象フォルダ」「除外フォルダ」「フォルダ集計の深さ」を無効にする。ただし CLI と同じく `Targets` は渡します（summary の「対象:」行が CLI と同じになるように）。
- 「出力先」を空のままにすると既定値の扱いになり、`OutputDir=null` を渡します。表示上は、既定のパスを薄い文字で示します。

### 2.3 入力の検証とエラーの表示

検証は Core の `GuiSettingsValidator`（8 章）で行います。ファイルシステムの確認はインターフェースを通して注入します。

| 項目 | 検証 | エラー文言（例） |
|---|---|---|
| 対象フォルダ | Files / All のとき 1 件以上 | 対象フォルダを 1 つ以上追加してください。 |
| 対象・除外フォルダ | 絶対パス（`X:\…`）であること | 「{パス}」はドライブ名から始まるフォルダを指定してください。 |
| 対象・除外フォルダ | UNC（`\\` で始まる）でないこと、ネットワークドライブでないこと | 「{パス}」はネットワーク上の場所のため指定できません。 |
| 対象フォルダ | 存在するフォルダであること | 「{パス}」が見つかりません。削除するか、別のフォルダを選んでください。 |
| 対象フォルダ | リパースポイント（ジャンクションなど）でないこと | 「{パス}」はリンク（ジャンクション）のため検査できません。リンク先のフォルダを直接追加してください。 |
| 対象・除外フォルダ | 重複しないこと（大文字小文字を区別しない、末尾の `\` を除いて比較） | 追加時に黙って重複を除き、状態欄に「既に追加されています」と表示 |
| 対象フォルダ | 他の対象の中に含まれる | エラーにはしない。注意として「{パス} は {親} に含まれるため、二重に集計されます」を表示 |
| 出力先 | 空（既定）または絶対パス。UNC・ネットワークドライブでない。ドライブが存在する。自身や祖先にリパースポイントがない | 出力先にネットワーク上の場所は使えません。／出力先のドライブが見つかりません。 |
| 未使用日数 | 1〜3650 の整数（NumericUpDown で範囲外を入力できない） | — |
| フォルダ深さ | 0〜10 の整数 | — |

- **表示方法**：
  - 項目の横に `ErrorProvider` のアイコンを出し、マウスを重ねると文言が出ます。
  - 「検査を開始」を押したときに全項目を検証し、エラーがあれば実行せず、実行領域の状態欄に「入力内容を確認してください（2 件）」と出して、最初のエラーの項目にフォーカスを移します。
  - 項目からフォーカスが外れたときにもその項目を検証し、直ったらすぐにアイコンを消します。
  - 「検査を開始」ボタンは無効にしません（押したときに理由が分かるようにするため）。
- 保存値を読み込んだ時点で存在しなくなった対象フォルダは、削除せずリストに残し、行の先頭に「（見つかりません）」を付けて表示します。実行するには削除が必要です。
- 実行時の最終的な判定は、今までどおり `Orchestrator` 側の検査（`ValidateLocalPath` と `OutputPrecheckException`）が行います。GUI の検証は、利用者に早めに知らせるための前段です。

### 2.4 対象リストの操作

- **[追加…]**：`FolderBrowserDialog`（`ShowNewFolderButton=false`、`UseDescriptionForTitle=true`、説明は「検査するフォルダを選んでください」）。選んだパスを 2.3 の規則で検証し、問題があればメッセージボックスで理由を出して追加しません。
- **[削除]**：選んだ行を削除します（複数選択可）。Delete キーでも削除できます。確認ダイアログは出しません（[既定に戻す] で戻せるため）。
- **[既定に戻す]**：既定の 3 か所（`%USERPROFILE%`、`C:\Program Files`、`C:\Program Files (x86)` のうち存在するもの）に置き換えます。リストが既定と違うときだけ有効にします。
- 既定の 3 か所の求め方は、`Orchestrator` の private メソッド `DefaultTargets()` を Core の `DefaultTargets.Resolve(Func<Environment.SpecialFolder,string>, Func<string,bool> directoryExists)` に移して共有します。`Orchestrator` はこれを呼ぶだけにするので、CLI の結果は変わりません。
- 保存するときは、リストが既定と同じなら `targets: null`（既定を使う）として保存します。別のユーザーや別の PC で開いたときに、その環境の既定に追従させるためです。

### 2.5 出力先の操作

- **[参照…]**：`FolderBrowserDialog`（新しいフォルダの作成を許可）。
- **[既定に戻す]**：テキストボックスを空にします（既定のパスを使う）。

---

## 3. 設定の保存

### 3.1 保存先の比較

| 案 | 場所 | 長所 | 短所 |
|---|---|---|---|
| **A（推奨）** | `%LOCALAPPDATA%\WinInspector\gui-settings.json` | 常に書き込める。ユーザーごとに分かれる。既定の出力先（`…\WinInspector\Logs`）と同じ親フォルダで分かりやすい | exe を別の PC に持っていっても設定は付いてこない |
| B | exe の隣の `config\gui-settings.json` | config.psd1 と同じ場所。USB メモリで持ち運べる | exe を `Program Files` やダウンロードフォルダ、読み取り専用の場所に置くと書けない。1 台の PC を複数人で使うと設定が混ざる。リポジトリ内の `WinInspector.exe` を直接実行すると、作業ツリーに未追跡ファイルができる |
| C | HKCU のレジストリ | — | `RegistryKey.SetValue` などはビルド時に禁止（BannedSymbols）。読み取り専用の方針に反するので不可 |

**推奨は A** です。

### 3.2 形式

```json
{
  "schemaVersion": 1,
  "mode": "All",
  "targets": null,
  "excludes": [],
  "outputDir": null,
  "unusedDays": 365,
  "folderDepth": 3,
  "runAsAdministrator": false,
  "windowBounds": { "x": 100, "y": 100, "width": 760, "height": 820 }
}
```

- `targets: null` は「既定の 3 か所」。`outputDir: null` は「既定の出力先」。
- 読み書きは `System.Text.Json` の**ソース生成**（`JsonSerializerContext`）を使います。リフレクションによる型の動的な解決をしないので、6 章の IL 検査（`Type.GetType` の禁止など）に触れません。
- 文字コードは UTF-8（BOM あり。`OutputFileWriter` の仕様）。読み込み側は BOM があってもなくても読めます。
- 保存する内容はフォルダのパスで、ユーザー名を含むことがあります。ログと同じ扱いで、`%LOCALAPPDATA%` の外へは出しません。

### 3.3 書き込み方法（書き込み許可箇所を増やさない）

- 書き込みは既存の `OutputFileWriter` を使います。
  - `OutputFileWriter.EnsureDirectory(%LOCALAPPDATA%\WinInspector, %LOCALAPPDATA%\WinInspector)`
  - `OutputFileWriter.Write(…\gui-settings.json, %LOCALAPPDATA%\WinInspector, json)`
- `OutputGuard.AssertWithin` が「`%LOCALAPPDATA%\WinInspector` の外には書かない」ことを保証します。静的検査の書き込み許可（`OutputFileWriter.cs` だけ）は変える必要がありません。
- 書き込む前に、`%LOCALAPPDATA%\WinInspector` とその祖先にリパースポイントがないことを確認します（出力先と同じ検査）。この検査は `Orchestrator.ValidateLocalPath` を `WinInspector.Windows/Fs/LocalPathValidator.cs` に移して共有します（処理は同じで、`Orchestrator` からはそれを呼ぶだけにする）。
- 一時ファイルに書いてから置き換える方式（`File.Move` / `File.Replace`）は禁止 API のため使いません。書き込みが途中で失敗して壊れた場合は、3.5 の規則で既定値に戻ります。ファイルは 1 KB 程度なので、実際に壊れる可能性は小さいです。

### 3.4 保存のタイミング

| タイミング | 保存する内容 |
|---|---|
| 「検査を開始」を押し、検証を通った直後（実行の前） | 画面の全設定 |
| 管理者として再起動する直前 | 画面の全設定（同じユーザーで昇格した場合は、昇格後も同じファイルを読む） |
| ウィンドウを閉じるとき | 画面の全設定（検証エラーがあってもそのまま保存。次回、エラー表示つきで復元される）とウィンドウの位置 |

- `-Gui` に CLI オプションを付けて開いた場合（1.3）、それらの値は「検査を開始」を押すまで保存しません。
- 保存に失敗しても検査は続けます。状態欄に「設定を保存できませんでした：{理由}」と出します。

### 3.5 壊れた設定ファイルの扱い

| 状態 | 扱い |
|---|---|
| ファイルがない | config.psd1 → 既定値の順で初期値を作る（3.6） |
| 64 KB を超える | 読まない。壊れたものと同じ扱い |
| JSON として読めない、ルートがオブジェクトでない | 全体を無視して 3.6 の初期値にする。警告帯に「保存された設定を読み込めなかったため、初期設定で開きました。次に保存するときに上書きします。」と出す |
| `schemaVersion` がない、または 1 より大きい | 全体を無視（上と同じ）。新しい版で保存したファイルを古い版が壊さないよう、**この場合は終了時に上書き保存しない**（「検査を開始」を押したときだけ上書きする） |
| 項目ごとの不正（`mode` が未知の文字列、数値が範囲外、配列に空文字や null） | その項目だけ初期値に戻す（数値は範囲内に丸める）。警告帯に「一部の設定を初期値に戻しました（mode、unusedDays）」と出す |

- 壊れたファイルは削除しません（`File.Delete` は禁止 API）。次の保存で上書きします。

### 3.6 config.psd1（SettingsComposition）との優先順位

**GUI の画面に出す項目**（Mode、Target、Exclude、OutputDir、UnusedDays、FolderDepth）

```
初期値：GUI の保存値（項目ごと） > config.psd1 > 既定値
実行時：画面の値をそのまま使う（config.psd1 で上書きしない）
```

- CLI の「明示した引数 > config.psd1 > 既定値」と同じ考え方です。画面で見えている値が、実行時に使われる値になります。

**GUI の画面に出さない項目**（IncludeRawLastAccess、IncludeRemovable）

```
実行時：config.psd1 > 既定値（今までの CLI の引数なし起動と同じ）
```

**実装**

- `SettingsComposition.Apply(CliParser.Parse([]), [])` を GUI の起動時に 1 回呼び、「config.psd1 を反映した既定の CliOptions」を作ります。これを初期値の土台にします。
- テストしやすくするため、`SettingsComposition` に設定ファイルのパスを受け取るオーバーロード `Apply(CliOptions cli, string[] args, string configPath)` を追加します。既存の `Apply(cli, args)` はこれを `AppContext.BaseDirectory\config\config.psd1` で呼ぶだけにします（動作は変わりません）。
- config.psd1 に未対応の構文がある場合（`ConfigWarning` あり）は、CLI と同じく config.psd1 の値を全部使わず、`ConfigWarning` を実行時の `CliOptions` に渡して errors と summary に残します。GUI では警告帯にも出します。
- `LowerPriority` は CLI と同じく config.psd1 から読みません。

---

## 4. 実行・進捗・中止

### 4.1 Orchestrator への最小の変更

```csharp
// Core（新規）
public enum InspectionPhase { Preparing, ScanningFiles, ReadingApps, ReadingEvidence, Classifying, Writing }
public sealed record InspectionProgress(InspectionPhase Phase, int FileCount, int FolderCount, int ErrorCount, string? CurrentPath, TimeSpan Elapsed);
public sealed record InspectionResult(string RunDirectory, string SummaryText, string SummaryPath, bool Interrupted, int ErrorCount);

// Cli/Orchestrator.cs
public static string Run(CliOptions options, CancellationToken cancellationToken = default)
    => Execute(options, cancellationToken, progress: null).RunDirectory;      // 既存。シグネチャと戻り値は同じ

public static InspectionResult Execute(CliOptions options, CancellationToken cancellationToken, IProgress<InspectionProgress>? progress)
    { /* 今の Run の本体を移す。要所で progress?.Report(...) を呼ぶ */ }
```

- `Run` の中身を `Execute` に移し、`Run` は `Execute(..., null)` を呼ぶだけにします。`progress` が `null` なら何も報告しないので、CLI の動作と出力は今と同じです。
- `Execute` は summary の文字列を `SummaryBuilder.Build` で作った後、ファイルに書くのに加えて戻り値にも入れます。summary の内容と文言は変えません。
- **FileScanner**：`ScanOptions` に任意の `Action<int,string>? OnProgress`（件数、今のフォルダ）を足します。既定値は `null` です。0.25 秒に 1 回まで（`Stopwatch` で間引く）呼びます。フォルダ単位で呼ぶので、走査の順序と結果は変わりません。
- **報告する場所**：準備の完了、ファイル走査（上の間引きつき）、アプリ一覧の取得の前、使用証拠の取得の前、判定（アプリ 50 件ごと）、書き出しの前。報告の間隔は最短で 0.25 秒です。`Progress<T>` は 1 回ごとに UI スレッドへ処理を送るため、間引かないと画面が固まるからです。
- **進捗の文言**（`InspectionProgressText.Format`、Core。テスト対象）

| Phase | 表示 |
|---|---|
| Preparing | 準備しています… |
| ScanningFiles | ファイルを走査しています… {FileCount:N0} 件 |
| ReadingApps | インストール済みアプリの一覧を読み取っています… |
| ReadingEvidence | 使用の記録（UserAssist・Prefetch）を読み取っています… |
| Classifying | アプリを判定しています… |
| Writing | 結果を書き出しています… |

- 経過時間は GUI 側のタイマー（1 秒ごと）で表示します。報告の有無に関係なく時間が進むので、長い処理でも止まって見えません。
- 割合（%）の進捗バーは出しません。総件数は走査が終わるまで分からないためです。`ProgressBar` は `Style=Marquee`（動いている表示）にします。

### 4.2 バックグラウンド実行

```csharp
private async void StartButton_Click(object? sender, EventArgs e)
{
    // 1. 検証（2.3）→ エラーなら return
    // 2. 管理者として実行がオンで、今が管理者でなければ 5 章へ
    // 3. 設定を保存（3.4）
    // 4. 画面を「実行中」にする（設定欄を無効化、[検査を開始]→無効、[中止]→有効）
    _cts = new CancellationTokenSource();
    var progress = new Progress<InspectionProgress>(ShowProgress);       // UI スレッドで作る
    try
    {
        var result = await Task.Run(() => Orchestrator.Execute(options, _cts.Token, progress));
        ShowResult(result);
    }
    catch (Orchestrator.OutputPrecheckException ex) { ShowFailure("出力先を準備できませんでした", ex.Message); }
    catch (Exception ex)                            { ShowFailure("検査を完了できませんでした", ex.Message); }
    finally { /* 画面を「待機中」に戻す。_cts を破棄 */ }
}
```

- `Orchestrator` の呼び出しは CLI と同じく、スレッドプール（MTA）で同期実行します（1.4）。
- 実行中は設定欄をすべて無効にします。同時に 2 つの検査は動かしません。
- `LowerPriority` は GUI では使わないので、プロセスの優先度は変えません。

### 4.3 中止

- [中止] を押すと `_cts.Cancel()` を呼び、ボタンを無効にして文言を「中止しています…」にします。
- 止まる場所は、今の CLI で Ctrl+C を押したときと同じです。ファイル走査はフォルダごと、アプリの処理は 1 件ごとに確認します。
- `Orchestrator` は中止しても summary と CSV を必ず書いてから返ります（0.5 節の 9、「中断（N 件まで）」）。GUI は `Execute` が返るのを待ち、結果を表示します。
- アプリ 1 件のフォルダサイズの計測（`InstallFolderMeasurer`）は途中で止まらないので、大きなフォルダの計測中に中止すると数秒〜数十秒かかることがあります【要実機確認】。今回は `Orchestrator` の止まる場所を増やしません（変更を最小にするため）。遅すぎる場合の改善案は「計測器にトークンを渡す」ことで、別の作業にします。
- **実行中にウィンドウを閉じようとした場合**：「検査を中止して閉じますか？これまでの結果は保存されます。」と確認します。[中止して閉じる] を選ぶと中止し、`Execute` が返って summary が書かれてからウィンドウを閉じます。

### 4.4 結果の表示

| 状態 | 条件 | 表示 |
|---|---|---|
| 完了 | `!Interrupted && ErrorCount == 0` | 状態：完了 |
| 一部エラー | `!Interrupted && ErrorCount > 0` | 状態：一部エラー（{N} 件。詳細は出力フォルダの errors ファイル） |
| 中断 | `Interrupted` | 状態：中断しました（途中までの結果を保存しました） |
| 開始できず | `OutputPrecheckException` | 出力先を準備できませんでした：{理由}。出力先を変えて、もう一度お試しください。（summary 欄は空。[出力フォルダを開く] は無効） |
| 予期しない失敗 | その他の例外 | 検査を完了できませんでした：{理由}。（summary 欄は空） |

- 状態の決め方は summary の「状態:」行と同じ優先順位です（中断 > 一部エラー > 完了）。Core の `RunOutcome.From(interrupted, errorCount)` にまとめてテストします。
- **summary の表示**：`InspectionResult.SummaryText`（ファイルに書いたものと同じ文字列）を、読み取り専用・複数行の `TextBox`（`ReadOnly=true`、`ScrollBars=Vertical`、`WordWrap=true`）に表示します。ファイルを読み直さないので、summary の書き込みに失敗した場合でも内容を表示できます。
- **[summary をコピー]**：`Clipboard.SetText(SummaryText)`。
- 結果の下に「出力先：{RunDirectory}」を表示します。

### 4.5 出力フォルダを開く

**禁止リストとの関係**

- ファイルを開く方法には、`Process.Start(フォルダのパス)`（`UseShellExecute=true`）、`ShellExecute`（shell32.dll の P/Invoke）、`SHOpenFolderAndSelectItems`（shell32.dll）などがあります。
- 今の静的検査では、`Process.Start` は `tools/csharp-allowlist.json` に書いたファイル（今は `Elevation.cs` だけ）の外では使えず、IL 上も `WinInspector.Windows.dll` の外では使えません。DllImport は `kernel32.dll` と `ole32.dll` だけが許されているので、shell32.dll は使えません。

**推奨する方式**

- `src/WinInspector.Windows/Process/FolderOpener.cs` を新しく作り、ここだけで `Process.Start` を呼びます。

```csharp
public static class FolderOpener
{
    // runDirectory は Orchestrator が返した実行フォルダ、outputRoot は出力先フォルダ
    public static void Open(string runDirectory, string outputRoot)
    {
        // 1. 絶対パスで、ドライブ名から始まり、UNC でない
        // 2. Directory.Exists(runDirectory)
        // 3. OutputGuard.AssertWithin(runDirectory, outputRoot)
        // 4. LocalPathValidator で自身と祖先にリパースポイントがない、ネットワークドライブでない
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var info = new ProcessStartInfo { FileName = explorer, UseShellExecute = false };
        info.ArgumentList.Add(runDirectory);
        using var _ = System.Diagnostics.Process.Start(info);   // 終了は待たない
    }
}
```

- `UseShellExecute=true` でフォルダのパスを直接渡す方式は採りません。パスが万一フォルダでなかった場合に、拡張子の関連付けで別のプログラムが起動するおそれがあるためです。起動するのは `%SystemRoot%\explorer.exe` だけに固定します。
- `explorer.exe` は引数の `/` や `,` をスイッチとして解釈することがあります。渡すのはドライブ名から始まる検証済みの絶対パスだけで、`ArgumentList` で引用符を付けて渡します。カンマを含むパスの動作は【要実機確認】です。
- 失敗したら（例外）、メッセージボックスで「フォルダを開けませんでした。パス：{RunDirectory}」と出し、パスをコピーできるようにします。

**静的検査の変更（7.2）**

- `tools/csharp-allowlist.json` の `processStartAllowlist` に `FolderOpener.cs` を足し、ファイルごとに契約を持たせます。
  - `Elevation.cs`：今の契約（`Environment.ProcessPath`、`UseShellExecute=true`、`Verb="runas"`）。
  - `FolderOpener.cs`：`explorer.exe` の文字列リテラル、`SpecialFolder.Windows`、`UseShellExecute = false`、`ArgumentList.Add` を含み、`Verb` を含まないこと。
- 今の検査は、許可ファイルすべてに Elevation の契約を求めているので、`FolderOpener.cs` を足すだけでは失敗します。契約をファイルごとに分ける変更が必要です。

---

## 5. 管理者として実行

### 5.1 方式の比較

| 観点 | **A（推奨）：GUI 自体を昇格して開き直す** | B：GUI は通常権限、検査だけ昇格した CLI で実行 |
|---|---|---|
| 進捗表示 | そのまま使える | 別プロセスなので取れない（昇格プロセスの標準出力は `UseShellExecute=true` ではリダイレクトできない） |
| 中止 | そのまま使える | 通常権限のプロセスから昇格プロセスを止められない（アクセス拒否） |
| summary の表示 | `InspectionResult` から表示 | 出力フォルダから一番新しい実行フォルダを探す必要がある（同時実行で取り違えるおそれ） |
| 実装量 | `Relaunch` に「待たない」引数を足す程度 | 結果の受け渡しの仕組みが別に必要 |
| 欠点 | ウィンドウが一度閉じて開き直す | — |

### 5.2 流れ（A）

1. 「管理者として実行」がオンで、今のプロセスが管理者でない状態で [検査を開始] を押す。
2. 検証を通ったら、確認ダイアログ「管理者として開き直して検査を始めます。Windows の確認画面（UAC）で［はい］を選んでください。」を出す（[開き直す] / [キャンセル]）。
3. 設定を保存する（3.4）。
4. 画面の設定から CLI 引数を作る（`GuiOptionsMapper.ToArguments`、Core。8 章）。

   ```
   -Gui -GuiAutoRun -Mode All -Target C:\Users\xxx "C:\Program Files" -Exclude … -OutputDir <展開済みの絶対パス> -UnusedDays 365 -FolderDepth 3 -Elevate
   ```

   - `-OutputDir` は既定値の場合も、**展開した絶対パスを必ず渡します**。別の管理者アカウントの資格情報で昇格した場合（標準ユーザーの PC など）、昇格後の `%LOCALAPPDATA%` が別のユーザーのものになるため、出力先がずれるのを防ぎます。
   - `-Target` も、既定の 3 か所を展開したパスで渡します。同じ理由で、`%USERPROFILE%` が別のユーザーのものにならないようにします。
   - 末尾の `-Elevate` は、昇格後の画面でチェックをオンに見せるための初期値です（1.3。昇格後の GUI は既に管理者なので、再び昇格はしません）。
5. `Elevation.Relaunch(arguments, waitForExit: false)` を呼ぶ。
   - 既存の方式どおり、引数は `%TEMP%\wininspector-args-<GUID>.txt` に 1 行ずつ書き、`-ArgumentsFile` で渡します。改行を含む引数はエラーです（フォルダのパスには改行が入らないので、実際には起きません）。
   - **UAC で [いいえ] を選んだ場合**（Win32Exception 1223）：引数ファイルを消して、「管理者として開き直すことができませんでした。通常の権限で実行する場合は、チェックを外してください。」と出し、元の画面に戻ります。
6. 昇格した子プロセスが起動したら、元の GUI は閉じます（終了コード 0）。
7. 子は `-ArgumentsFile` を読んで削除し（既存の処理）、`-Gui -GuiAutoRun` を見て GUI を開き、受け取った値を画面に入れて、すぐに検査を始めます。

### 5.3 Elevation.Relaunch の変更

- 今の `Relaunch(string[] args)` は子の終了を `WaitForExit` で待ちます。GUI の UI スレッドで待つと画面が固まるので、引数を足します。

  ```csharp
  public static int Relaunch(string[] args, bool waitForExit = true)
  ```

  - `waitForExit=true`（既定）は今と同じ動作で、CLI の `-Elevate` から呼ばれます。
  - `waitForExit=false` のときは、起動できたら 0 を返します。**このとき引数ファイルは消しません**（子が読み終える前に消すと失敗するため）。消すのは子の `ReadArguments` です。起動に失敗したときだけ親が消します。
- `-Elevate` を引数から除く処理は、`waitForExit=false` のときは行いません（5.2 の 4 で GUI の初期値として使うため）。この分岐は `Relaunch` の中に入れず、呼び出し側で `-Elevate` を最後に足す形でもかまいません。実装時に、CLI 側の動作が変わらない方を選んでください。
- 静的検査の契約（`Environment.ProcessPath`、`UseShellExecute=true`、`Verb="runas"`）はそのまま満たします。

### 5.4 チェックボックスの状態

| 今のプロセス | チェックの表示 |
|---|---|
| 管理者でない | 保存値のとおり。変更できる。説明「Prefetch も使用の根拠にします。実行時に Windows の確認画面が出ます。」 |
| 管理者 | オン・変更不可。「管理者として実行中」と表示 |

- 起動時に自動で昇格することはしません。昇格は必ず [検査を開始] を押したときだけです。

---

## 6. コンソールウィンドウの扱い

### 6.1 選択肢の比較

| 観点 | **A（推奨）：Exe のまま＋GUI 時に FreeConsole** | B：WinExe に変更 | C：GUI 専用の exe を別に作る |
|---|---|---|---|
| ダブルクリック起動の見た目 | コンソールが一瞬（0.1〜0.3 秒程度）出て消える【要実機確認】 | コンソールは出ない | GUI exe は出ない |
| cmd から CLI を実行 | 今と同じ。cmd は終了まで待ち、`%ERRORLEVEL%` に終了コードが入る | cmd は終了を待たずにプロンプトに戻る。`%ERRORLEVEL%` は取れない（`start /wait` が必要） | 今と同じ |
| PowerShell から CLI を実行 | 今と同じ（`$LASTEXITCODE` が入る） | GUI アプリは待たれず、`$LASTEXITCODE` が設定されないことがある。CI のスクリプト（`& WinInspector.exe …; if ($LASTEXITCODE -ne 0)`）が壊れる | 今と同じ |
| 標準出力・標準エラー | 今と同じ | コンソールがないので、対話中の画面には何も出ない（`AttachConsole` で親のコンソールにつなぐと、プロンプトと出力が混ざる）。リダイレクト時は出る | 今と同じ |
| Ctrl+C での中断 | 今と同じ | 使えない | 今と同じ |
| `-Help` の表示 | 今と同じ | 画面に出ない | 今と同じ |
| 配布物 | 1 つの exe | 1 つの exe | exe が 2 つになる（30〜45 MB × 2。共通部分を分けると single-file の利点が減る） |
| 制約「引数ありの CLI の動作・終了コードを変えない」 | 満たす | **満たさない** | 満たす |

- **推奨は A** です。B は CLI の互換と CI の実行方法を壊すため、制約に合いません。C は配布物と CI の作業が倍になり、利用者がどちらを使えばよいか迷います。

### 6.2 A の具体的な動作

- GUI を開く直前に、`kernel32.dll` の `GetConsoleProcessList` でこのコンソールにつながっているプロセスの数を調べます。
  - **1 つ（自分だけ）**：ダブルクリックなどで、このプロセスのために作られたコンソールです。`FreeConsole` を呼んで切り離すと、コンソールウィンドウは閉じます。
  - **2 つ以上**：cmd や PowerShell から `WinInspector.exe -Gui` などで起動した場合です。親のコンソールなので切り離しません（閉じると親の画面が消えるため）。この場合、GUI を閉じるまでシェルは待ちます。
- 使う API はどちらも `kernel32.dll` で、静的検査の DllImport 許可リスト（`kernel32.dll`、`ole32.dll`）の範囲内です。
- **Windows 11 で既定のターミナルが Windows Terminal の場合**：ダブルクリックで Windows Terminal のタブが開きます。`FreeConsole` 後にそのタブ（またはウィンドウ）が閉じるか、「プロセスは終了しました」と残るかは【要実機確認】です。`FreeConsole` を呼んでもウィンドウが残るかどうかを実機確認項目（8.5）に入れます。それでも問題が大きい場合は、確認点 1 を再検討します。
- GUI のときは `Console.WriteLine` を使いません（コンソールが無いため）。エラーはすべて画面に出します。

---

## 7. オフライン保証の維持

### 7.1 構成

- WinForms のコードは `WinInspector.Cli` プロジェクト（`WinInspector.dll` / `WinInspector.exe`）に入るので、今の `BannedApiAnalyzers`（ビルド時）と `StaticChecks`（Linux CI でのソースと IL の検査）がそのまま効きます。新しいプロジェクトは作りません。
- Core に足す `Gui/` のコード（設定、検証、変換）も、Core の `BannedApiAnalyzers` と `StaticChecks` の対象です。
- `WinInspector.Cli.csproj` に `<UseWindowsForms>true</UseWindowsForms>` を足します。参照するのは .NET の Windows Desktop 共有フレームワークだけで、NuGet の実行時依存は増えません。

### 7.2 GUI でよく使う API と禁止リストの関係

| API | 用途 | 扱い |
|---|---|---|
| `Process.Start` | 出力フォルダを開く／昇格 | `FolderOpener.cs` と `Elevation.cs` の 2 か所だけ許可（4.5、5.3）。ファイルごとの契約を静的検査で確認 |
| `ShellExecute` / `SHOpenFolderAndSelectItems`（shell32.dll） | フォルダを開く | **使わない**。DllImport の許可は kernel32 と ole32 だけ。shell32 を足さない |
| `LinkLabel` から URL を開く、ヘルプの URL | — | **使わない**。画面に URL を置かない |
| `WebBrowser` / `WebView2` コントロール | — | **使わない**。summary は `TextBox` で表示 |
| `FolderBrowserDialog` | フォルダ選択 | 使う。選んだパスは 2.3 の規則で検証（ネットワーク上の場所は拒否）。ダイアログが OS 側で行う処理は 7.4 |
| `Clipboard.SetText` | summary のコピー | 使う（ファイルやネットワークに触れない） |
| `File.WriteAllText` など書き込み系 | 設定の保存 | 直接は使わない。`OutputFileWriter` 経由だけ（3.3） |
| `StreamWriter` / `FileStream`（書き込み） | — | GUI では使わない。今の BannedSymbols には入っていないので、静的検査に「`new StreamWriter(` と `new FileStream(` は `Elevation.cs` 以外で禁止」を足す（design-csharp.md 6.2 (b) で予定していた検査の一部） |
| `System.Text.Json`（ソース生成） | 設定の読み書き | 使う。名前空間は禁止対象外。リフレクションによる型の解決をしない |
| `Properties.Settings` / `ApplicationSettingsBase` | 設定の保存 | **使わない**。保存先と形式を制御できず、`user.config` の場所が版ごとに変わるため |
| `Application.SetHighDpiMode`、`Timer`、`ErrorProvider` | 画面 | 使う |

- **名前の注意**：今の `StaticChecks` は、禁止された型の**短い名前**がソースに含まれるだけで失敗します（`FindBanned` が `Ping`、`Dns`、`Socket`、`Binder`、`WebClient` などを部分文字列で探す）。GUI のクラス名や変数名に、これらの文字列を含めないでください（例：`FolderBinder`、`DnsLabel` は不可）。

### 7.3 静的検査に加える変更（まとめ）

1. `processStartAllowlist` をファイルごとの契約に変える（4.5）。
2. `new StreamWriter(` と `new FileStream(` を許可箇所以外で禁止する（7.2）。
3. IL 検査で `System.Diagnostics.Process::Start` が `WinInspector.Windows.dll` の外にないことは、今の検査のままで守られます（`FolderOpener` は Windows 層に置くため）。
4. 検査器の自己テスト（`--self-test`）に、`FolderOpener` の契約違反（`UseShellExecute=true` や `Verb` を含む）を検出できることを加えます。

### 7.4 保証の範囲（README に追記）

- 保証の内容は変わりません：「本プログラムは通信しない」「検査対象のファイルとレジストリを変更しない」。
- 新しく書き込むのは `%LOCALAPPDATA%\WinInspector\gui-settings.json` だけです（ログと同じ親フォルダ）。
- 範囲外として明記するもの：
  - フォルダ選択ダイアログは Windows のシェルの部品です。最近使った場所の記録（HKCU の ComDlg32 関連の値など）を Windows が書くことがあります【要実機確認：Process Monitor で値の名前を確かめる】。ダイアログの「ネットワーク」を開くと、Windows がネットワーク上の機器を探すことがあります。
  - 「出力フォルダを開く」で起動したエクスプローラーの動作（サムネイルの作成、最近使ったファイルの記録など）。
- CI の通信遮断テスト（design-csharp.md 6.3）は CLI の実行に対して行います。GUI の通信遮断の確認は実機確認項目（8.5）に入れます。

---

## 8. テスト方針

### 8.1 構成（UI と論理の分離）

```
src/WinInspector.Core/Gui/             net10.0。Linux でテストできる
  GuiSettings.cs              設定のモデル（record）。3.2 の JSON に対応
  GuiSettingsJson.cs          JSON の読み書き（ソース生成の JsonSerializerContext）。壊れたファイルの扱い（3.5）
  GuiSettingsDefaults.cs      初期値の決定：保存値 > config.psd1 から作った CliOptions > 既定値（3.6）
  GuiSettingsValidator.cs     入力検証（2.3）。IPathProbe を注入
  GuiOptionsMapper.cs         GuiSettings → CliOptions、GuiSettings → 昇格用の CLI 引数、CliOptions → GuiSettings
  DefaultTargets.cs           既定の 3 か所（2.4。Orchestrator からも使う）
  InspectionProgress.cs       進捗と結果の型、進捗の文言（4.1）、RunOutcome（4.4）
  IPathProbe.cs               DirectoryExists, IsNetworkPath, HasReparsePoint, DriveExists
src/WinInspector.Windows/
  Fs/LocalPathValidator.cs    Orchestrator.ValidateLocalPath を移したもの。IPathProbe の Windows 実装もここ
  Process/FolderOpener.cs     4.5
  Process/ConsoleWindow.cs    GetConsoleProcessList / FreeConsole（6.2）
src/WinInspector.Cli/Gui/
  GuiHost.cs                  STA スレッドの作成、Application.Run（1.4）
  MainForm.cs                 画面。上の Core の部品を呼ぶだけにして、判断を持たない
```

- `MainForm` には「どの値が正しいか」「何を保存するか」の判断を書きません。それらはすべて Core の部品にあり、単体テストで確かめます。

### 8.2 単体テスト（`tests/WinInspector.Core.Tests`、Linux）

| 対象 | 代表的なケース |
|---|---|
| GuiSettingsJson | 往復（保存して読むと同じ）、`targets: null`、BOM あり・なし、JSON でない、空ファイル、64 KB 超、`schemaVersion` なし・2、未知の `mode`、範囲外の数値（丸め）、配列に null・空文字、未知のキーは無視 |
| GuiSettingsDefaults | 保存値なし＋config.psd1 なし → 既定値、config.psd1 だけ → その値、保存値と config.psd1 の両方 → 保存値、項目ごとの混在（保存値に `excludes` がない）、`ConfigWarning` のとき config.psd1 の値を使わない |
| GuiSettingsValidator | 2.3 の表のすべて。Apps モードでは対象の空を許す。重複の判定（大文字小文字、末尾の `\`）、入れ子は注意で止めない、出力先の空は既定扱い |
| GuiOptionsMapper | 画面の値 → `CliOptions`（`NoPause=true`、`Elevate=false`、`LowerPriority=false`、非表示項目は config.psd1 の値、`OutputDir` の空は null）。**昇格用の引数の往復**：`ToArguments` の結果から `-Gui`、`-GuiAutoRun` を除いて `CliParser.Parse` に通すと、同じ `CliOptions` になる（空白・日本語・`(x86)` を含むパス、除外が空のとき `-Exclude` を出さない） |
| DefaultTargets | 3 つとも存在、一部が存在しない、同じパスの重複（大文字小文字違い）を 1 つにする |
| SettingsComposition（パス指定のオーバーロード） | 既存のテストを新しいオーバーロードでも通す |
| InspectionProgress / RunOutcome | 各 Phase の文言、件数の桁区切り、中断 > 一部エラー > 完了 の優先順位（`SummaryBuilder` の「状態:」と一致すること） |
| FileScanner の進捗 | `OnProgress` を渡しても渡さなくても、行の順序と内容が同じ。呼ばれる回数が間引かれている（偽の時計を注入） |

### 8.3 静的検査のテスト（`tests/WinInspector.StaticChecks`）

- 7.3 の変更。自己テストに、`FolderOpener` の契約違反、許可外のファイルでの `new StreamWriter(` を検出できることを加えます。

### 8.4 Windows のテスト（`tests/WinInspector.Windows.Tests`）

- `LocalPathValidator`：移す前と同じ結果になること（ジャンクション、存在しないパス、ドライブのルート）。
- `FolderOpener` の検証部分（起動の手前まで）：出力先の外、UNC、存在しないフォルダを拒否すること。実際にエクスプローラーを起動するテストは CI では行いません。
- `Orchestrator.Execute`：小さな fixture に対して、`progress` ありとなしで同じ CSV と summary になること。`InspectionResult.SummaryText` がファイルの内容と同じこと。途中でキャンセルすると `Interrupted=true` で summary が書かれていること。

### 8.5 UI の実機確認項目（`docs/verification-checklist.md` に追加）

1. エクスプローラーからダブルクリック → GUI だけが残り、コンソールは消える（Windows Terminal が既定の場合も確認）。
2. cmd から `WinInspector.exe -Mode Files -Target <小さいフォルダ> -NoPause` → 今までどおりコンソールに出力され、`echo %ERRORLEVEL%` が 0。
3. cmd から引数なしで実行 → GUI が開き、閉じるまで cmd が待つ。cmd の画面は消えない。
4. 初回起動の初期値（既定の 3 か所、365、3、出力先）と、config.psd1 を置いたときの初期値。
5. 対象の追加・削除・既定に戻す。ネットワークドライブ、UNC、ジャンクション、存在しないフォルダを選んだときのエラー表示。
6. 設定を変えて閉じ、開き直すと復元される。`gui-settings.json` を壊すと警告帯が出て既定値で開く。
7. 小さいフォルダで検査 → 進捗が動き、完了後に summary が表示される。summary の内容が出力フォルダの summary ファイルと同じ。
8. 実行中に [中止] → 「中断」と表示され、summary と CSV が書かれている。実行中にウィンドウを閉じたときの確認ダイアログ。
9. [出力フォルダを開く] → その実行のフォルダがエクスプローラーで開く。カンマを含む出力先でも開く。
10. 管理者として実行：UAC で [はい] → 開き直してすぐに検査が始まり、summary の「管理者: True」「Prefetch使用=はい」。[いいえ] → 元の画面に戻りメッセージが出る。
11. 標準ユーザーで別の管理者アカウントを入力して昇格 → 出力先と対象が元のユーザーのものになっている。
12. 高 DPI（150%）で文字やボタンが切れない。
13. Process Monitor で、GUI の操作中に書き込まれるファイルとレジストリを記録し、`gui-settings.json`、出力フォルダ、7.4 の OS 側の記録以外がないこと。
14. 通信を遮断するファイアウォール規則を入れた状態で、GUI から検査を最後まで実行できる。

---

## 9. CI への影響

### 9.1 Linux（`csharp-linux`）

- `WinInspector.sln` のビルドは `-p:EnableWindowsTargeting=true` で行っており、`Directory.Build.props` にも `EnableWindowsTargeting=true` があります。この設定があれば、`UseWindowsForms=true` のプロジェクトも Linux でビルドできます（Windows Desktop の参照パックを NuGet から取得する）。テストの実行（`WinInspector.Core.Tests`）は WinForms に依存しません。【要 CI 確認：最初の PR で確かめる】
- `TreatWarningsAsErrors` と `AnalysisLevel=latest-recommended` により、WinForms の解析警告（WFO で始まるもの）もエラーになります。見つかったら、抑制ではなくコードを直して対応します。
- `ImplicitUsings` と `UseWindowsForms` の組み合わせで、`System.Drawing` と `System.Windows.Forms` が暗黙の using に加わります。既存の `Orchestrator.cs` などで名前の衝突（`Timer` など）が出た場合は、csproj に `<Using Remove="System.Windows.Forms" />` を書き、GUI のファイルだけで明示的に using します。
- `StaticChecks` の IL 検査は `WinInspector.dll` の中の GUI のコードも調べます。WinForms の型（`System.Windows.Forms.*`）は禁止の名前空間ではないので、そのまま通ります。

### 9.2 Windows（`csharp-windows`）

- フィクスチャの実行は `-Mode Files -Target … -OutputDir … -FolderDepth 5 -NoPause` で**引数あり**なので、1.1 の 4（CLI）に進み、今と同じ動作です。
- PowerShell の `&` 実行で、標準入出力はリダイレクトされています。万一引数なしで実行しても、1.2 の条件で CLI に回るため、CI で GUI が開いて止まることはありません。
- publish したファイルのサイズは、WinForms の分だけ増えます（目安で数 MB〜十数 MB【要実測】）。`EnableCompressionInSingleFile` は今のまま使います。
- `OutputType` を変えない（6 章の A）ので、`$LASTEXITCODE` の確認はそのまま動きます。
- CI では引数なしの実行を追加しません。引数なしの実行は既定の 3 か所の全体走査になり、時間がかかるためです。代わりに、1.1〜1.2 の分岐を Core の純粋な関数 `StartupMode.Decide(args, isInteractive, inRedirected, outRedirected, errRedirected)` にまとめ、単体テスト（Linux）ですべての組み合わせを確かめます。

### 9.3 リポジトリ内の WinInspector.exe

- 3b66b6c でリポジトリに保存した `WinInspector.exe` と `SHA256SUMS.txt` は、GUI を実装した版を publish し直したときに更新します（手順は今と同じ）。

---

## 10. 実装手順の分割案

各段階は単独でビルドとテストが通り、CLI の動作を変えないことを確かめてから次に進みます。

| # | 内容 | 変えるファイル（主なもの） | 確認 |
|---|---|---|---|
| 1 | **下準備（動作は変えない）**：`DefaultTargets` を Core へ、`ValidateLocalPath` を `LocalPathValidator` へ移す。`SettingsComposition.Apply` にパス指定のオーバーロード | Orchestrator.cs、Core/Gui/DefaultTargets.cs、Windows/Fs/LocalPathValidator.cs、SettingsComposition.cs | 既存のテストと CI のフィクスチャが通る |
| 2 | **進捗と結果**：`InspectionProgress`、`InspectionResult`、`Orchestrator.Execute`、`ScanOptions.OnProgress` | Orchestrator.cs、Scanner.cs、Core/Gui/InspectionProgress.cs | 8.2 の FileScanner、8.4 の Execute。CLI の出力が同じ |
| 3 | **Core の GUI ロジック**：設定のモデル・JSON・初期値・検証・変換・起動分岐の判定 | Core/Gui/* | 8.2 の表 |
| 4 | **Windows 層**：`FolderOpener`、`ConsoleWindow`、`Elevation.Relaunch(waitForExit)` | Windows/Process/* | 8.4 |
| 5 | **静的検査**：ファイルごとの Process.Start 契約、StreamWriter / FileStream の制限、自己テスト | tests/WinInspector.StaticChecks、tools/csharp-allowlist.json | 自己テストと本検査が通る |
| 6 | **画面**：`UseWindowsForms`、`GuiHost`、`MainForm`、`Program.Main` の分岐 | WinInspector.Cli.csproj、Cli/Gui/*、Program.cs | Linux と Windows の CI が通る。8.5 の実機確認 |
| 7 | **ドキュメント**：README（GUI の使い方、設定の保存先、保証の範囲 7.4）、verification-checklist（8.5）、`-Help` の文言 | README.md、docs/verification-checklist.md | — |

- 段階 1〜5 は画面がなくても進められ、Linux でほぼすべてを確かめられます。段階 6 だけが実機での確認を必要とします。
- PowerShell 版（`src/*.ps1`）と `.cmd` ランチャーは、どの段階でも変更しません。

---

## 付録：変更しないことの確認

| 制約 | この設計での扱い |
|---|---|
| 出力の互換（ファイル名・CSV の列・summary の文言） | `Orchestrator` の書き出し処理は変えない。GUI は `Execute` が返した文字列を表示するだけ |
| 引数ありの CLI の動作・終了コード | `-Gui` を含まない引数は、今の CLI の流れをそのまま通る（1.1 の 4）。`OutputType` も変えない |
| PowerShell 版と .cmd ランチャー | 変更しない |
| オフライン保証 | 書き込みは `OutputFileWriter` のみ、`Process.Start` は Windows 層の 2 ファイルのみ（どちらも契約を静的検査で確認）、DllImport は kernel32 のみ追加利用、ネットワーク系 API は不使用 |
| 利用者向けの文言 | すべて日本語 |

---

## 付記：実装時の決定（司令塔、2026-11-18）

- DPI の扱いについて、本文の「マニフェストは変えません」から変更しました。WinForms 導入で解析器 WFO0003 がエラーになるため、`app.manifest` から `dpiAware` 要素だけを外し（longPathAware 等は不変）、`WinInspector.Cli.csproj` の `ApplicationHighDpiMode` を `SystemAware` に設定しました。`SetHighDpiMode(HighDpiMode.SystemAware)` の呼び出しも残しており、DPI の意味（システム DPI に追従）は従来どおりです。
