# 設計書：WinInspector の C# 移植（exe 版）

> 注記：この設計書は PowerShell 版の実装（`src/`）、設計書（`docs/design.md`）、CI、`tools/Start-Elevated.ps1` を読んで作成しました。Windows 実機での確認はまだしていません。挙動が環境で変わり得る箇所には【要実機確認】を付けています。ファイルは編集していません。

---

## 0. 結論

- **言語・ランタイム**：C# 14、**.NET 10（LTS）**、Windows 向けは `net10.0-windows10.0.19041.0`。.NET 8 は 2026-11-10 にサポートが終了しているため採用しません。
- **配布物**：`WinInspector.exe`（win-x64、self-contained、single-file）。PowerShell も .NET Runtime も事前インストールは不要です。サイズは目安で 30〜45 MB（圧縮後、要実測）。Git にはバイナリを入れず、CI の artifact として出します。
- **構成**：プラットフォーム非依存の `WinInspector.Core`（走査・判定・CSV・UserAssist 解析・設定読み取り）と、Windows 固有 API を担う `WinInspector.Windows`、そして両者を組み立てる `WinInspector.Cli`（exe）に分けます。Core は Linux 上で単体テストと実ディレクトリ走査ができます。
- **出力互換**：ファイル名、CSV の列と順序、summary の行順と文言、出力先（`%LOCALAPPDATA%\WinInspector\Logs\<yyyyMMdd-HHmmss>\`）、終了コードを PowerShell 版と揃えます。レビュー済みの境界条件はすべて引き継ぎます（0.5 節）。
- **読み取り専用・オフラインの保証**：ビルド時の Roslyn BannedApi、Linux CI での IL メタデータ許可リスト検査とソース構文検査、Windows CI での通信遮断テストと実行前後の読み取り専用回帰テストを組み合わせます。自己昇格に使う `Process.Start` は、その 1 箇所だけに限定して許可します。
- **既存 PowerShell 版**：`src/`、`*.cmd`、既存テストは変更しません。CI には jobs を追加する形で共存させます。

---

## 0.5 PowerShell 版から引き継ぐ確定事項

PowerShell 版の該当箇所と、C# 版での扱いを対応させます。Codex は各項目の実装とテストをこの表で確認してください。

| # | 仕様（PS 版の根拠） | C# での扱い |
|---|---|---|
| 1 | DWORD を符号なし 32bit に正規化する。`[uint32]([int64]$v -band 0xffffffffL)`（EnvironmentProbe.ps1:12） | `unchecked((uint)(long)v)` で統一。NtfsDisableLastAccessUpdate と Start_TrackProgs の両方に適用（9章の差分参照） |
| 2 | UserAssist の既知フォルダ GUID を実パスに変換する（UsageEvidence.ps1:16-17） | 7 件の固定マップ（9章の表）。値が空なら GUID のまま残す |
| 3 | Prefetch の汎用名を除外する（UsageEvidence.ps1:33） | `^(?:uninstall\|unins\|install).*\.exe$` と `msiexec.exe`、`update.exe`、`setup.exe` を除外。大文字小文字を区別しない |
| 4 | CountOnly の集約（Classifier.ps1:13、Invoke-WinInspector.ps1:34） | UserAssist 一致で RunCount > 0 かつ LastRunTime が無いものがあれば CountOnly を立て、最優先で「判定不能」にする |
| 5 | 過剰一致の除外（UsageEvidence.ps1:28、AppInventory.ps1:33） | 証拠照合で `C:\`、`C:\Program Files`、`C:\Program Files (x86)`、`C:\Windows`、`C:\Windows\System32` を InstallLocation や exe フォルダとして使わない。サイズ計測ではルート、Program Files、Program Files (x86) の 3 つを除外し、同一 InstallLocation が 2 件以上ある場合も計測しない |
| 6 | 候補の並び順（Invoke-WinInspector.ps1:39） | 区分（A→B）→ サイズ不明を後ろ → サイズ降順。安定ソートを使い、同値は入力順 |
| 7 | CSV 数式対策（LogWriter.ps1:4） | 保護列のみ。先頭が `= + - @ TAB CR LF` のとき `'` を付けてから二重引用で囲む |
| 8 | junction・ネットワーク経路の拒否（FileScanner.ps1:19-21、AppInventory.ps1:20-23） | リパースポイントは中に入らず 1 行のみ記録。UNC・ネットワークドライブは拒否。InstallLocation の祖先にリパースポイントがあれば計測しない |
| 9 | 中断時の件数（Invoke-WinInspector.ps1:47） | 中断しても summary とクローズを必ず行う。状態行は「中断（N件まで）」 |
| 10 | 最終アクセス時刻（EnvironmentProbe.ps1:13、FileScanner.ps1:41） | NTFS かつ有効なら LastAccessStatus は空欄で時刻を出す。無効なら設定 Status、非 NTFS なら `非NTFS(<形式>)`、リパースポイントなら `ReparsePoint`。非 NTFS では時刻を出さない。 |

---

## 1. 技術選定

### 1.1 言語とランタイム

| 観点 | 採用：C# / .NET 10 | 不採用 |
|---|---|---|
| 追加インストール | 不要（single-file self-contained） | PowerShell 版は pwsh 7 が必要 |
| サポート | LTS（2028年11月まで。公式ポリシーで再確認） | .NET 8 はサポート終了済み。.NET 9 は STS |
| Windows API | `Microsoft.Win32.RegistryKey`、`Windows.Management.Deployment`（WinRT 投影）、`ComImport`（IShellLink）を直接使える | Python は Appx と COM が弱い |
| 依存 | 実行時の NuGet 依存はゼロ | 依存が増えるとサプライチェーンの審査対象が増える |
| 言語 | C# 14（.NET 10 の既定） | — |
| AOT | 不採用 | ComImport 相互運用と WinRT 投影の対応が不確実 |

- `<Nullable>enable</Nullable>`、`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`、`<AnalysisLevel>latest-recommended</AnalysisLevel>` を全プロジェクトに設定します。
- `<InvariantGlobalization>true</InvariantGlobalization>` を設定し、日付・数値の書式は `CultureInfo.InvariantCulture` で統一します。

### 1.2 プロジェクト構成

```
WinInspector.sln
src/
  WinInspector.Core/              net10.0（Windows 非依存。Linux でビルド・テスト可）
    Paths/        WinPath.cs（\ 区切りの文字列規則。System.IO.Path に頼らない）
    Scan/         FileScanner.cs, FsEntry.cs, IFileSystem.cs, ScanOptions.cs
    Apps/         AppRecord.cs, AppClassifier.cs, ExclusionRules.cs, EvidenceMatcher.cs,
                  InstallSizePolicy.cs, InstallFolderMeasurer.cs
    Evidence/     UserAssistDecoder.cs, Rot13.cs, KnownFolderMap.cs, PrefetchNameParser.cs
    Settings/     LastAccessRules.cs, TrackingRules.cs, PsdReader.cs（設定ファイルの限定パーサー）
    Output/       CsvWriter.cs, SummaryBuilder.cs, OutputGuard.cs, FileTimeFormat.cs
    Cli/          CliOptions.cs, CliParser.cs（PS 互換の引数解釈。CLI 層の型だけ置く）
  WinInspector.Windows/           net10.0-windows10.0.19041.0（Windows 固有 API のみ）
    Registry/     UninstallRegistryReader.cs, UserAssistRegistryReader.cs,
                  WindowsSettingsReader.cs（NtfsDisableLastAccessUpdate, Start_TrackProgs）
    Appx/         AppxPackageReader.cs（PackageManager）
    Shell/        ShellLinkTargetReader.cs（IShellLink を読み取り専用で使う）
    Fs/           WindowsFileSystem.cs（IFileSystem の実装）, VolumeInfoProvider.cs（DriveInfo）
    Prefetch/     PrefetchDirectoryReader.cs
    Process/      PriorityControl.cs, Elevation.cs（自己昇格。6章の例外箇所）
  WinInspector.Cli/               exe。OutputType=Exe。Program.cs が唯一の組み立て（DI の代わりに手動配線）
    app.manifest, Program.cs, RunContext.cs, Orchestrator.cs
tests/
  WinInspector.Core.Tests/        net10.0。Linux CI で実行。xUnit
  WinInspector.StaticChecks/      net10.0。IL メタデータとソース構文の検査。Linux CI で実行
  WinInspector.Windows.Tests/     net10.0-windows10.0.19041.0。windows-latest で実行
tools/
  csharp-allowlist.json           C# 版の許可リスト（6章）
```

### 1.3 Windows 固有 API とロジックの分離（ports and adapters）

- Core は次のインターフェースだけに依存します。Windows 側は実装を提供し、テストでは偽物を注入します。

```csharp
public interface IFileSystem {
    IEnumerable<FsEntry> Enumerate(string dir);       // 遅延列挙。途中の例外は呼び出し側へ
    FileAttributes? TryGetAttributes(string path);    // ルートと祖先のリパース検査用
    bool DirectoryExists(string path);
    bool FileExists(string path);
}
public sealed record FsEntry(string FullPath, string Name, string Extension, FileAttributes Attributes,
    bool IsDirectory, long Length, DateTime CreationTime, DateTime LastWriteTime, DateTime LastAccessTime);

public interface IVolumeInfo { DriveKind KindOf(string path); string? FormatOf(string path); }  // Unknown/Network/Removable/Fixed
public interface IUninstallSource { IEnumerable<RawUninstallEntry> ReadAll(Action<ErrorRecord> onError); }
public interface IAppxSource { IEnumerable<AppxRecord> ReadCurrentUser(Action<ErrorRecord> onError); }   // 失敗時は例外を投げ、呼び出し側で errors に記録
public interface IUserAssistSource { IEnumerable<(string ValueName, byte[] Data)> ReadAll(); }
public interface IPrefetchSource { IEnumerable<(string FileName, DateTime LastWriteTime)> List(); }
public interface IShortcutTargetReader { string? TryReadTarget(string lnkPath); }
public interface IWindowsSettings {
    object? NtfsDisableLastAccessUpdate();   // Registry の値。無ければ null
    object? StartTrackProgs();
    bool IsAdministrator();
    string CurrentAccountName();             // 取得失敗時は "不明"
    string TimeZoneId();
}
public interface IKnownFolderMap { string? PathOf(string knownFolderGuid); }
```

- `WinPath` は `\` 区切りの文字列規則を自前で持ちます。`System.IO.Path` は Linux 上で `\` を区切りとして扱わないため、Core の経路判定に使ってはいけません。Windows 側の adapter だけが `System.IO` を使います。
- `Program.cs` が Windows 実装を生成して Core へ渡します。Core から Windows 名前空間を参照させません。

### 1.4 Linux でできる範囲

- Core の単体テスト（判定、UserAssist 解析、ROT13、FILETIME、LastAccess、CSV、summary、psd1 読み取り、CLI 解釈、照合ロジック）。
- 偽の `IFileSystem` を使った走査テスト（行順、フォルダ集計、リパース、エラー継続、中断、除外、出力先除外）。
- 実ディレクトリ（`/tmp` の一時フォルダ）に対する走査テスト。`DirectorySeparatorChar` を `/` にした設定で、シンボリックリンクが辿られないことを確認します。
- IL メタデータとソース構文の静的検査。
- 【要実機確認】Linux 上で `net10.0-windows` のプロジェクトをビルドできるか。最初の PoC で確認します。ビルドできない場合は Windows 層を別ソリューションに分けます。

---

## 2. ビルドと配布

### 2.1 プロジェクト設定（`src/WinInspector.Cli/WinInspector.Cli.csproj` の要点）

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
  <SupportedOSPlatformVersion>10.0.19041.0</SupportedOSPlatformVersion>
  <AssemblyName>WinInspector</AssemblyName>
  <RootNamespace>WinInspector</RootNamespace>
  <ApplicationManifest>app.manifest</ApplicationManifest>
  <Nullable>enable</Nullable>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <InvariantGlobalization>true</InvariantGlobalization>
  <GenerateDocumentationFile>false</GenerateDocumentationFile>
  <DebugType>none</DebugType>
  <Company></Company><Authors></Authors><Product>WinInspector</Product>
</PropertyGroup>
```

- 会社名・作者名・ユーザー名は AssemblyInfo やバージョン情報に入れません。
- `app.manifest` の要点：`requestedExecutionLevel level="asInvoker"`（通常実行で UAC を出さない）、`longPathAware=true`（長いパスの扱い）、`dpiAware=true`。

### 2.2 publish コマンド

```
dotnet publish src/WinInspector.Cli -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -p:PublishReadyToRun=false ^
  -o publish/win-x64
```

| 項目 | 判断 | 理由とリスク |
|---|---|---|
| self-contained | **採用（既定）** | 利用者に .NET の導入を求めない。サイズは大きくなる（目安 30〜45 MB、要実測） |
| framework-dependent | 代替案（確認点 1） | 数 MB で済むが、.NET 10 Runtime の導入が必要 |
| single-file | **採用** | 単一の exe として配れる。圧縮（`EnableCompressionInSingleFile`）で起動時に展開する分の遅延が出る（数百 ms 程度、要測定） |
| トリミング | **不採用（v1）** | 削減は数 MB〜十数 MB の見込み。WinRT 投影と ComImport、反射を含むため、警告の見落としで実行時に壊れるリスクがある。v2 で `TrimmerRootAssembly` を検討 |
| ReadyToRun | 不採用 | サイズが増える。起動時間の改善は小さい |
| Native AOT | 不採用 | 1.1 節のとおり |

### 2.3 署名と SmartScreen

- v1 では署名しません。未署名の exe は SmartScreen の警告を受けることがあるため、README に「ブロックの解除」と「実行の許可」の手順を書きます。
- 署名する場合の必要物（証明書の調達、CI のシークレット管理）は利用者の判断事項です（確認点 4）。

### 2.4 CI artifact

- CI の windows ジョブで publish し、`actions/upload-artifact@v4` で `WinInspector-win-x64`（`WinInspector.exe` と `SHA256SUMS.txt`）を出します。`retention-days` は 14 とします。
- SHA256 は `Get-FileHash` で pwsh の 1 ステップとして計算します。
- `publish/`、`bin/`、`obj/`、`TestResults/` は `.gitignore` に入れます。`*.exe` も入れてコミットを防ぎます。
- 実ログや fixture で生成したログは artifact に含めません（design.md 6.3 の方針どおり）。

---

## 3. モジュール対応表

### 3.1 対応表

| PS 版 | C# の配置 | 実装の要点 |
|---|---|---|
| `Invoke-WinInspector.ps1` | `Cli/Program.cs`、`Cli/Orchestrator.cs` | 引数解釈、設定の合成、出力先の準備、フェーズ実行、summary 出力。例外は全体を捕捉して errors に記録 |
| `EnvironmentProbe.ps1` `ConvertFrom-Rot13` | `Core/Evidence/Rot13.cs` | ASCII 英字のみ回転（`Rot13.cs` のテストで確認） |
| 同 `ConvertFrom-FileTimeValue` | `Core/Settings/FileTimeFormat.cs` | 0 以下は null。`DateTime.FromFileTime` が例外なら null |
| 同 `Get-LastAccessSetting` | `Core/Settings/LastAccessRules.cs` | 0/1/0x80000002/0x80000003/null/その他。値は `uint` に正規化してから判定 |
| 同 `Get-InspectorEnvironment` | `Windows/Registry/WindowsSettingsReader.cs` + `Core/Settings/LastAccessRules.cs` | HKLM `SYSTEM\CurrentControlSet\Control\FileSystem`、`RegistryView.Default`、読み取り専用 |
| 同 `Get-AppTrackingSetting` | `Core/Settings/TrackingRules.cs` + `WindowsSettingsReader` | HKCU `Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced`、`Start_TrackProgs`。値なしは `Enabled=false`、Status `値なし（既定で有効の可能性）` |
| `FileScanner.ps1` `Invoke-FileScan` | `Core/Scan/FileScanner.cs` | 下記 3.2 |
| `FileScanner.ps1` `New-InspectorFileRecord` | `Core/Scan/FileRecord.cs` | 下記 3.2 |
| `AppInventory.ps1` `Get-UninstallInventory` | `Windows/Registry/UninstallRegistryReader.cs` | HKLM64、HKLM32、HKCU の順。`OpenBaseKey` + `OpenSubKey(name, false)` |
| 同 `Convert-UninstallKey` | 同上 | 値名 12 個を `object?` で取得して `RawUninstallEntry` にする |
| 同 `Get-AppxInventory` | `Windows/Appx/AppxPackageReader.cs` | 3.3 節 |
| 同 `Measure-InstallFolder` 他 | `Core/Apps/InstallFolderMeasurer.cs` | `IFileSystem` を使う。キャッシュは完全パスで大文字小文字を区別しない |
| 同 `Get-ReparsePointAncestor` | `Core/Scan/ReparseGuard.cs` | `IFileSystem.TryGetAttributes` で各祖先を検査 |
| `UsageEvidence.ps1` `Get-UserAssistEvidence` | `Windows/Registry/UserAssistRegistryReader.cs` | HKCU `...\UserAssist\{GUID}\Count` の値を `(ValueName, byte[])` で渡す。`DoNotExpandEnvironmentNames` を指定 |
| 同 `ConvertFrom-UserAssistData` | `Core/Evidence/UserAssistDecoder.cs` | 長さ < 68 は null。回数は offset 4 の UInt32、時刻は offset 60 の Int64 FILETIME |
| 同 `Resolve-UserAssistPath` | `Core/Evidence/KnownFolderMap.cs` | 7 件のマップ（9章） |
| 同 `Get-PrefetchEvidence` | `Windows/Prefetch/PrefetchDirectoryReader.cs` + `Core/Evidence/PrefetchNameParser.cs` | `%SystemRoot%\Prefetch\*.pf`。ファイル名 `^(.+)-[0-9A-Fa-f]{8}\.pf$` から実行ファイル名を得る。時刻は LastWriteTime。管理者のときだけ呼ぶ |
| 同 `Resolve-AppEvidence` | `Core/Apps/EvidenceMatcher.cs` | 3.4 節 |
| 同 `WScript.Shell` による TargetPath 取得 | `Windows/Shell/ShellLinkTargetReader.cs` | 3.5 節 |
| `Classifier.ps1` | `Core/Apps/AppClassifier.cs`、`Core/Apps/ExclusionRules.cs` | 3.6 節の順序を厳守 |
| `LogWriter.ps1` `ConvertTo-CsvCell` / `Write-InspectorCsvRow` / `Open-InspectorCsv` | `Core/Output/CsvWriter.cs` | 3.8 節 |
| 同 `Assert-OutputPath` | `Core/Output/OutputGuard.cs` | 文字列の接頭辞検査。書き込み直前に毎回呼ぶ |
| 同 `Assert-NoReparseOutputPath` / `New-InspectorOutputDirectory` | `Windows/Fs/OutputDirectory.cs` | 作成は `Directory.CreateDirectory` を 1 箇所だけで呼ぶ |
| 同 `Write-InspectorText` | `Core/Output/SummaryWriter.cs` | UTF-8 BOM、CRLF |
| `config/config.sample.psd1`、`config/exclusions.psd1` の `Import-PowerShellDataFile` | `Core/Settings/PsdReader.cs` | 3.9 節 |

### 3.2 走査（FileScanner）

- **列挙**：`new DirectoryInfo(dir).EnumerateFileSystemInfos()` を使い、`EnumerationOptions` を明示します。`AttributesToSkip = 0`（隠し・システムを含める）、`IgnoreInaccessible = false`、`RecurseSubdirectories = false`、`ReturnSpecialDirectories = false`。PS 版と同じ結果になるかは fixture で確認します（【要実機確認】）。
- **FileInfo を新しく作らない**：`new FileInfo(path)` は stat 呼び出しを増やします。列挙で得た `FileSystemInfo` の値（サイズ、3 種類の日時、属性）を使い、`Refresh()` を呼びません（design.md 2章）。
- **順序（行順の一致）**：PS 版と同じ DFS を使います。
  1. 初期フレーム（Depth=0）をスタックに積む。
  2. ポップしたフレームが `Exit=true` なら、その合計を親へ加算し、`Depth <= FolderDepth` のときだけ folders 行を出す。
  3. そうでなければ列挙し、リパースポイントは files 行（Type=ReparsePoint）として即時出力し、通常ファイルは files 行を出力、サブディレクトリは子リストへ入れる。
  4. 終了フレームを積んでから、子を**列挙順に**積む（ポップは逆順になる）。
- **除外**：ポップ時に `Exclude` の各要素に対して「接頭辞＋区切り文字」で判定します（`Test-PathWithin` と同じ。`C:\Foo` と `C:\FooBar` を区別する）。出力先（`OutputDir`）配下も除外します（9章 差分 4）。
- **フォルダ集計**：`DirectSizeBytes` は直下の通常ファイルの合計。`TotalSizeBytes` と `FileCount` は部分木の合計。`HadErrors` は子とエラー元へ伝播。`FileCount` はリパースポイントを含めません。
- **エラー継続**：項目単位（`EnumerateItem`）とディレクトリ単位（`EnumerateDirectory`）で捕捉し、errors に記録して次へ進みます。途中の例外でも、それまでの結果は残します。
- **進捗**：1 秒に 1 回、件数・現在のパス・経過時間を stderr に `\r` で上書き表示します。終了時に改行します。
- **中断**：`Console.CancelKeyPress` でトークンをキャンセルし、スキャンを抜けたら writer を flush して閉じ、状態を「中断」にします。2 回目の Ctrl+C は既定の終了に任せます。
- **バッファ**：CSV は `BufferedStream`（64 KB）で書き、終了時と中断時に flush します。PS 版の `AutoFlush=true` は行わず、性能を優先します。

### 3.3 Store アプリ（Appx）

- **取得**：`Windows.Management.Deployment.PackageManager` の `FindPackagesForUser("")`（現在のユーザー）。PowerShell の `Get-AppxPackage` は使えません。
- **マッピング**：

| PS 版の項目 | WinRT の取得元 |
|---|---|
| Name | `Package.Id.Name` |
| Version | `Package.Id.Version` を `Major.Minor.Build.Revision` で文字列化 |
| Publisher | `Package.Id.Publisher`（証明書の Subject 形式。`PublisherDisplayName` ではない） |
| InstallLocation | `Package.InstalledLocation.Path` |
| SignatureKind | `Package.SignatureKind` の列挙名（`System` など） |
| IsFramework / IsResourcePackage | 同名のプロパティ |
| PackageFamilyName | `Package.Id.FamilyName` |
| NonRemovable | **未確定**（下記） |

- **NonRemovable が未確定**：WinRT の `Package` に対応するプロパティを確認できていません。実機の PoC で `Package` と `PackageId` のプロパティを列挙して確認します。見つからない場合の案は次のとおりです。
  - 案 A：`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\...` の該当値を読む（場所と意味は【要実機確認】）。
  - 案 B：取得できない扱いとして `false` にし、summary に「NonRemovable 取得不可」と明記する。影響は限定的です。`SignatureKind=System`、`IsFramework`、`IsResourcePackage` の条件で、主要なシステム系は除外されるためです。
  - 推奨：PoC で案 A が確認できれば A、できなければ B。B の場合は差分として 9 章に記録します。
- **失敗時**：列挙が例外になったら `errors` に `Path=Appx`、`Operation=Get-AppxPackage`、`ErrorType`（例外型名）、`Message` を記録し、summary の「Appx取得状態」を「取得失敗（errors に記録）」にします。
- **InstallDate**：空欄（PS 版と同じ）。`InstallDateSource` も空。
- **順序**：`FindPackagesForUser` の順序は `Get-AppxPackage` と一致しない可能性があります。apps CSV の Appx 行の順序は保証しません。比較テストでは並べ替えてから比べます。
- **Appx のサイズ**：`InstallLocation` の実測（3.7 節）。`WindowsApps` 配下の列挙失敗は、9 章の差分 6 に従って扱います。

### 3.4 レジストリ（読み取り専用）

- すべて `RegistryKey.OpenBaseKey(hive, view)` で開き、`OpenSubKey(name, false)` だけを使います。
- 32bit 側の Uninstall は `RegistryView.Registry32` で開きます（WOW6432Node は OS が解決します）。
- HKCU は `RegistryView.Default`。`OpenRemoteBaseKey` は使いません。
- すべての `RegistryKey` は `using` で破棄します。
- 値の取り出しは `GetValue(name, null)` を使い、`UserAssist` だけ `RegistryValueOptions.DoNotExpandEnvironmentNames` を付けます。
- 型の扱い：DWORD は `int` で来るため、`EstimatedSize` は `long` へ変換して PS 版と同じ符号付きの扱いにします（9 章の差分 7）。`REG_SZ` の数値は PS 版の `[long]` キャストに相当する変換を行い、変換できなければ ProcessApp エラーとしてそのアプリを行に出しません。

### 3.5 ショートカット（.lnk）の読み取り（IShellLink、読み取り専用）

- `WScript.Shell`（ProgID）は使いません。`CreateShortcut` は保存が可能なオブジェクトで、ProgID の動的生成は静的検査（6章）で禁止するためです。
- 使うのは次の 2 つの COM インターフェースだけです。
  - `IShellLinkW`（CLSID `00021401-0000-0000-C000-000000000046`）の `GetPath`（`fFlags=0`）。
  - `IPersistFile`（IID `0000010b-0000-0000-C000-000000000046`）の `Load(path, STGM_READ=0)`。
- **呼ばないもの**：`Save`、`SetPath`、`Resolve`（リンク追跡で UNC に接続し得る）、`SetDescription` などの書き込み系。インターフェースの vtable を埋めるための未使用スロットは `_Reserved…` という名前の宣言だけにし、6章の静的検査で該当名の呼び出しを禁止します。
- 取得した `TargetPath` が `\\` で始まる、またはネットワークドライブなら `networkShortcutCount` を増やし、パスを記録しません（PS 版の Invoke-WinInspector.ps1:30 と同じ）。
- `.lnk` の祖先にリパースポイントがあれば読まずに同じカウントを増やします。
- 例外は握りつぶさず、PS 版と同じく空として扱います（`catch {}` 相当）。ただし静的検査で空 catch が増えないよう、捕捉したら `ShortcutReadFailed` の件数だけ数えます。

### 3.6 判定（AppClassifier）

判定は次の順序で評価し、最初に当たったものを採用します（PS 版 Classifier.ps1 の順序どおり）。

1. **除外**（`Test-AppExcluded`）。理由は次の順に並べ、`; ` で連結します。
   1. `SystemComponent` が文字列として `"1"`（DWORD 1 もこれになる）
   2. `ParentKeyName` が空でない
   3. `ReleaseType` が `^(Update|Hotfix|Security Update)$`（大文字小文字を区別しない）
   4. 名前が `KB\d+` に一致（大文字小文字を区別しない）
   5. 名前が null または空：`名称なし`
   6. 名前パターン（`exclusions.psd1` の各パターン、大文字小文字を区別しない）に最初に一致したもの 1 件（`名前パターン: <パターン>`）
   7. Source が Appx で、`SignatureKind=System`、`IsFramework`、`NonRemovable`、`IsResourcePackage` のいずれか
   
   1 件でもあれば `対象外`。
2. `CountOnly`（UserAssist の一致で回数が 1 以上かつ最終時刻が無いものがある）なら `判定不能`（理由：`実行回数のみの記録あり`）。これは `Classifier.ps1` の最優先判定に合わせる。
3. 最終実行時刻があれば、`時刻 <= now − UnusedDays` で `候補A`、そうでなければ `判定不能`（`使用中（最終実行が閾値以内）`）。
4. 実行回数が 1 以上なら `判定不能`（`証拠あり・時刻なし（実行回数の記録のみ）`）。
5. 追跡が無効または不明なら `判定不能`（`アプリ起動追跡が無効または不明`）。
6. InstallDate が `^\d{8}$` で `yyyyMMdd` として解釈でき、`日付 <= now − UnusedDays` なら `候補B`。
7. それ以外は `判定不能`（`実行記録または有効なインストール日なし`）。

- `now` は `DateTime.Now`（ローカル時刻）。FILETIME の変換もローカル時刻です。
- 境界：「ちょうど N 日前」は候補（`<=`）になります。

### 3.7 サイズ計測（InstallSizePolicy）

PS 版 AppInventory.ps1:33 の判定を次のとおりに移植します。

1. `EstimatedSize` が 0 以外で値あり → `SizeBytes = EstimatedSize × 1024`、`SizeSource = EstimatedSize`。**0 は「値なし」扱い**（PS の真偽判定と同じ）。
2. そうでなければ `InstallLocation` が空でないときだけ次を行う。
   - 完全パスを作り、ルート（`C:`）を求める。
   - 完全パスが次のいずれかと一致（大文字小文字を区別しない）なら計測しない：ルート、`ルート\Program Files`、`ルート\Program Files (x86)`。
   - 同じ正規化済み InstallLocation を持つアプリが 2 件以上なら計測しない（`locationCounts`。Appx を含む全インベントリで数える）。
   - それ以外は `Measure`。結果があれば `SizeSource=FolderSum`。例外は `MeasureInstallFolder` としてエラーに記録。
3. `Measure`（InstallFolderMeasurer）
   - `UNC` は `NetworkPathRejected`、ネットワークドライブは `NetworkDriveRejected`、祖先にリパースポイントがあれば `ReparsePointRejected`（いずれも計測せず null）。
   - キャッシュ（完全パス、大文字小文字を区別しない）。
   - 存在しなければ例外（`InstallLocation を読めません`）。
   - スタックで再帰せず列挙。リパースポイントは飛ばす。ディレクトリは積む。ファイルは `Length` を加算。項目・ディレクトリの例外は記録して続行。
4. 9 章の差分 6（ルートの列挙失敗）と差分 7（負の EstimatedSize）を参照。

### 3.8 証拠の照合（EvidenceMatcher、PS 版 Resolve-AppEvidence）

前処理：
- 証拠のうち、UserAssist で `RunCount == 0` のものを除きます（Prefetch は RunCount が null なので残す）。
- `DisplayIcon` と `UninstallString` から exe パスを抽出します。正規表現は `^\s*"?([^",]+?\.exe)(?:"|,|\s|$)`（大文字小文字を区別しない）。
- InstallLocation の末尾の `\` と `/` を除きます。除外ルート（`C:`、`C:\Program Files`、`C:\Program Files (x86)`、`C:\Windows`、`C:\Windows\System32`。末尾の `\` を除いた形で比較）に一致すれば空にします。

照合の方法は次の順に `found` へ積みます（先に積んだものが `Method` の代表になります）。

1. **InstallLocation**：UserAssist の Path が `loc + '\'` で始まる（大文字小文字を区別しない）。
2. **IconOrUninstaller**：UserAssist の Path が、各 exe の親フォルダ＋`\` で始まる（親フォルダが除外ルートなら除く）。exe ごとに最初の 1 件で打ち切る。
3. **ShortcutTarget**：UserAssist の Path が `.lnk` で、`TargetPath` が exe パスのいずれかと一致（大文字小文字を区別しない）。
4. **AppxAumid**：PackageFamilyName があり、UserAssist の Path が `PFN + "!"` で始まる。
5. **PrefetchName**：exe パスのファイル名（汎用名を除く）と、Prefetch の ExeName が一致（大文字小文字を区別しない）。

集約：
- `times` は時刻がある一致のうち最大（降順の先頭）。その Source を `LastRunSource` にする（時刻がなければ `UserAssist`）。
- `CountOnly` は、UserAssist の一致のうち `RunCount > 0` かつ `LastRunTime` が無いものが 1 件でもあれば true。
- `RunCount` は UserAssist の一致の最大値（一致がなければ null）。
- `Method` は `found` の先頭。

### 3.9 CSV 出力（CsvWriter）

- **文字コード**：UTF-8 BOM あり、改行は CRLF。ヘッダも同じ形式。
- **セル**：すべてのセルを二重引用符で囲み、内部の `"` を `""` にする。`null` は空文字。`DateTime` は `yyyy-MM-dd HH:mm:ss`（InvariantCulture）。`bool` は `True` / `False`（PS 版の `[string]` の結果と同じ大文字の形）。`FileAttributes` は `ToString()`（`Archive, NotContentIndexed` のような表記。PS 版の `[string]` と同じ）。
- **数式対策**：保護列だけに適用（表の順に、先頭文字が `= + - @`、TAB、CR、LF なら先頭に `'` を付けてから引用する）。
  - errors：`Path`、`Message`
  - apps と app_candidates：`Name`、`Version`、`Publisher`、`InstallDate`、`InstallDateSource`、`InstallLocation`、`LastRunSource`、`MatchMethod`、`Status`、`ExcludedReason`（存在しない列名は無視）
  - files、folders：なし
- **出力先の検査**：書き込みの直前に `OutputGuard` で「出力先フォルダの接頭辞」を確認し、外れていれば `出力先フォルダ外への書き込みを拒否しました` で止めます。
- **作成**：`FileMode.CreateNew` ではなく `Create`（PS 版と同じ上書き）。同じ秒に 2 回実行すると同じフォルダに上書きされることを README に書きます（9章 差分 8）。

### 3.10 summary の作成（SummaryBuilder）

- **行順と文言**は PS 版 `Invoke-WinInspector.ps1:47` をそのまま移植します。行の数、順序、括弧の位置、句読点を変えません。
- 改行は CRLF。ファイル末尾の改行はテンプレートに従う（PS 版は改行を入れていない）。
- 項目ごとの値の決め方：
  - 「対象」は `Target` を `; ` で連結。
  - 「管理者」は `True` / `False`。
  - 「実行アカウント」は取得失敗時 `不明`。
  - 「最終アクセス設定」は `Status (Value)`。値が null なら `Value` の部分は空（`設定不明 ()`）。
  - 「アプリ起動追跡」は `Status / Value`（値なしの場合は `値なし（既定で有効の可能性） / `）。
  - 「候補A 上位」「候補B 上位」は区分の先頭 10 件を `名前 (サイズ bytes)` で `; ` 連結。サイズが null なら `名前 ( bytes)`（PS 版の挙動）。Mode が Apps / All 以外は `未実行`。
  - 「状態」は中断が優先で `中断（N 件まで）`、次にエラーありで `一部エラー`、それ以外は `完了`。
  - 「所要時間」は `TimeSpan.ToString()`。
  - 「Prefetch使用」は管理者かつ Apps / All のとき `はい`。
  - 「未変換の GUID 付きパス」は解決後のパスに `\{GUID\}\` が残る件数。
- **summary の書き込み失敗**は stderr に出して終了コードは変えません（PS 版と同じ）。

### 3.11 設定ファイル（PsdReader）

- `config/config.psd1` は次の**限定された構文**だけを読みます。`@{ }`、`key = value`、単一引用符の文字列、`$true` と `$false`、整数、`@( ... )` の配列（カンマか改行区切り）、`#` のコメント。
- それ以外の構文（変数、関数、式、`$(...)`）は読まず、`設定ファイルに未対応の構文があります（行 N）` で警告して設定を無視します。
- 読み取り対象は `Mode`、`Target`、`Exclude`、`OutputDir`、`UnusedDays`、`FolderDepth`、`IncludeRawLastAccess`、`IncludeRemovable`。**`LowerPriority` は PS 版と同じく設定ファイルからは読みません。**
- 優先順位は「明示したコマンドライン引数 > config.psd1 > 既定値」。
- 探す場所は exe のあるフォルダの `config\config.psd1`（PS 版はリポジトリの `config/config.psd1`）。
- `exclusions.psd1` の `NamePatterns` は **exe に埋め込んだ既定値**（ビルド時に `config/exclusions.psd1` を EmbeddedResource として取り込み、同じ文字列を使う）。外部の `exclusions.psd1` がその隣にあれば、そちらを優先します。単一ファイルの exe でも判定が同じになるようにするためです。
- 正規表現は .NET の `Regex` を使います（PS 版も .NET の正規表現を使うため、構文は一致します）。

---

## 4. CLI 仕様

### 4.1 オプション（PS 版と同じ名前と既定値）

PS の引数名は大文字小文字を区別せず、README の例（`-Mode All`）をそのまま使えるようにします。C# 標準の `--` 形式は採用しません。引数解釈は手書きの `CliParser` で行います。

| オプション | 型 | 既定値 | 説明 |
|---|---|---|---|
| `-Mode Files\|Apps\|All` | 列挙 | `All` | 大文字小文字を区別しない。他の値はエラー |
| `-Target <path>...` | 文字列の配列 | `%USERPROFILE%`、`C:\Program Files`、`C:\Program Files (x86)`（存在するものだけ） | 次のオプションが現れるまで値を集める |
| `-Exclude <path>...` | 文字列の配列 | なし | 同上 |
| `-OutputDir <path>` | 文字列 | `%LOCALAPPDATA%\WinInspector\Logs` | |
| `-UnusedDays <int>` | 整数 | `365` | 負の値も受け付ける（PS 版と同じ） |
| `-FolderDepth <int>` | 整数 | `3` | 同上 |
| `-IncludeRawLastAccess` | スイッチ | off | |
| `-IncludeRemovable` | スイッチ | off | |
| `-LowerPriority` | スイッチ | off | 自プロセスを BelowNormal にする |
| `-Elevate` | スイッチ | off | 管理者として再起動する（5章） |
| `-NoPause` | スイッチ | off | 終了時の一時停止をしない（新規） |
| `-Help` / `-Version` | スイッチ | — | 表示して終了（終了コード 0） |
| `-ArgumentsFile <path>` | 内部用 | — | 5章。ヘルプには出さない |

- 不明な引数、値の欠落、整数変換の失敗は、理由を stderr に出して**終了コード 1**。
- `-Target` の既定値は PS 版（Invoke-WinInspector.ps1:9）と同じく、存在確認をしたものだけにします。

### 4.2 終了コード

| コード | 意味 | PS 版との対応 |
|---|---|---|
| 0 | 完了、一部エラー、中断（すべて 0） | PS 版は明示的な exit を持たないため 0。中断時の PS 版の終了コードは【要実機確認】。差分があれば 9 章 差分 5 |
| 1 | 引数エラー、予期しない未処理例外 | PS 版の引数バインド失敗は pwsh が 1 を返す（【要実機確認】） |
| 2 | 実行の前提不成立（OS が Windows でない、出力先がネットワーク、リパースポイントを含む出力先、出力先を作れない） | PS 版と同じ |

- PS 版の「FullLanguage モードが必要です」「PowerShell 7 以降が必要です」の検査は exe では不要なので削除します（9章 差分 2）。

---

## 5. 実行環境と自己昇格

### 5.1 通常実行

- `app.manifest` は `asInvoker`。`WinInspector.exe` をダブルクリック、またはコマンドラインから実行すると、UAC は出ません。
- 引数なしでダブルクリックした場合は `-Mode All` 相当で動き、終了時に `Enter キーで終了` と表示して待ちます。`-NoPause` の指定時、または標準入力がリダイレクトされているとき（CI など）は待ちません。

### 5.2 自己昇格（`-Elevate`）

PS 版の `Start-Elevated.ps1` と同じ方式にします。

1. 親プロセス（非管理者）が引数を一時ファイルに 1 行ずつ UTF-8（BOM なし）で書く。ファイル名は `%TEMP%\wininspector-args-<GUID>.txt`。
2. 改行を含む引数はエラーにする（PS 版の制約を引き継ぐ）。
3. `Process.Start` で自分自身（`Environment.ProcessPath`）を `Verb="runas"`、`UseShellExecute=true` で起動し、引数は `-ArgumentsFile "<path>"` だけにする。`ArgumentList` と `UseShellExecute=true` の組み合わせは使わず、`Arguments` 文字列を自前で引用して作る（【要実機確認】）。
4. 親は子の終了を待ち、子の終了コードをそのまま返す。
5. 子は `-ArgumentsFile` を読み、**ファイルを読んだ直後に削除**する。パスは `%TEMP%` の直下で、ファイル名が `wininspector-args-` で始まるものに限る（PS 版の検査を移植）。違えば終了コード 2。
6. 子は通常の処理を行う。`-Elevate` は子に渡さない。

- UAC を拒否された場合は、親が理由を表示して終了コード 2。
- 子が昇格したことで Prefetch と他ユーザーの一部フォルダを読めます。summary の「管理者」は `True` になります。

### 5.3 自己昇格と静的検査の切り分け

- `Process.Start` は C# 版で唯一の例外として `WinInspector.Windows/Process/Elevation.cs` の 1 メソッドだけに許可します。
- 静的検査は次を確認します（6.3）。
  - `Process.Start` の呼び出しは `Elevation.cs` の中だけ。
  - その呼び出しで `FileName` が `Environment.ProcessPath`、`Verb` が文字列リテラル `"runas"`、`UseShellExecute` が `true`。
  - それ以外の箇所では `Process` の `Start` 系（`Start`、`StartInfo`）を禁止。`GetCurrentProcess` と `PriorityClass` の設定は許可。
- PS 版の `Start-Process` 禁止は、exe 版では「自己昇格の 1 箇所だけ」に置き換わります。README に理由を書きます。

---

## 6. オフライン・読み取り専用の保証と静的検査

PS 版の AST 検査は C# では使えないため、次の 3 層で代替します。

### 6.1 第 1 層：ビルド時（BannedApiAnalyzers）

- `Microsoft.CodeAnalysis.BannedApiAnalyzers` を全プロジェクトに入れ、`BannedSymbols.txt` で次を禁止します（違反は RS0030 のエラーになり、ビルドが止まります）。
  - `T:System.Net.*` の主要型（`WebClient`、`HttpClient`、`Socket`、`Dns`、`WebRequest`、`TcpClient`、`Ping`）
  - RegistryKey の `SetValue`、`DeleteValue`、`DeleteSubKey`、`DeleteSubKeyTree`、`CreateSubKey`、`OpenRemoteBaseKey`
  - `M:Microsoft.Win32.Registry.SetValue`
  - `File` の Delete/Move/Copy/Replace/Create、属性・時刻変更、WriteAllText/Lines/Bytes、AppendAllText/Lines と書き込み用ストリーム取得（各シグネチャは BannedSymbols.txt に列挙）
  - `Directory` の Delete/Move/CreateDirectory
  - `M:System.Activator.CreateInstance(System.Type)`、`M:System.Type.GetType(System.String)`、`M:System.Type.GetTypeFromProgID`、`M:System.Type.GetTypeFromCLSID`、`M:System.Reflection.Assembly.Load(System.String)`、`M:System.Reflection.Assembly.LoadFrom`
  - `T:System.Management.*`（WMI、Win32_Product 対策）
  - `T:Microsoft.CSharp.*`（dynamic）、`T:System.Linq.Expressions.*`の `Compile`
- 例外の許可は、該当ファイルに `#pragma warning disable RS0030` を書く形にし、そのファイルは 6.3 の検査で追加のレビュー対象になります。許可箇所は次だけです：
  - `OutputFileWriter` のファイル書き込みと出力先内の作成
  - `Elevation.cs` の `Process.Start`

### 6.2 第 2 層：CI の静的検査（`tests/WinInspector.StaticChecks`、Linux で実行）

**(a) IL メタデータの許可リスト検査**

- ビルドした `WinInspector.Core.dll`、`WinInspector.Windows.dll`、`WinInspector.Cli.dll` を `System.Reflection.Metadata` で開き、`MemberReference` と `TypeReference` を列挙します。Linux 上でも動きます。
- 参照されている型とメンバーを、`tools/csharp-allowlist.json` と照合します。
  - `allowedNamespaces`：`System`、`System.Collections.*`、`System.Globalization`、`System.Linq`、`System.Text`、`System.Text.RegularExpressions`、`System.IO`（書き込みは (c) の制限）、`Microsoft.Win32`（読み取りは (c) の制限）、`Windows.Management.Deployment`、`Windows.ApplicationModel`、`System.Runtime.InteropServices`（ComImport と Marshal の解放だけ）、`System.Diagnostics`（Stopwatch と自プロセス）。
  - `forbiddenPrefixes`：`System.Net`、`System.Management`、`System.Reflection`（読み取り以外）、`System.Runtime.Loader`、`Microsoft.CSharp`、`System.Linq.Expressions`、`Windows.Networking`、`Windows.Web`、`Windows.Services`。
  - `forbiddenMembers`：`Process.Start`（許可箇所以外）、`RegistryKey.OpenRemoteBaseKey`、`Assembly.Load*`、`Type.GetType`、`Activator.CreateInstance`、`Marshal.GetActiveObject`。
- DllImport の `ImplMap` は、許可された DLL 名（`kernel32.dll`、`ole32.dll`）だけに限り、それ以外（`wininet`、`winhttp`、`ws2_32`、`iphlpapi`、`dnsapi`、`urlmon`）は禁止します。

**(b) ソース構文の検査（Roslyn の構文木）**

- `src/**/*.cs` を構文木で走査し、次を検出します。
  - 文字列リテラルに `WinHttp`、`MSXML2`、`InternetExplorer.Application`、`WScript.Shell`、`Invoke-WebRequest`、`Win32_Product`、`Get-Package` を含む（防御的な検出）。
  - `OpenSubKey` の第 2 引数が `false` 以外のリテラル、または式（書き込み用の取得を防ぐ）。
  - `FileStream` や `File.Open` の第 2 引数 `FileAccess` に `Write` や `ReadWrite` を含む（書き込みは `OutputFileWriter` だけ）。
  - `Process` の `Start` の呼び出し（許可箇所 `Elevation.cs` の 1 メソッド以外）。
  - `ShellLink` 系の `Save`、`SetPath`、`Resolve` の呼び出し。
  - `dynamic` キーワードと `nameof` 以外の反射による呼び出し。

**(c) 書き込み箇所の集約**

- 書き込みを伴う API（`FileStream` の書き込み、`Directory.CreateDirectory`）は、`OutputFileWriter 以外で使えないことを (b) で確認します。
- 検査器自体の検証として、禁止パターンを含む小さなサンプルを用意し、検査が失敗することを確かめるテストを置きます（検査器が壊れていないことの保証）。

### 6.3 第 3 層：実行時（Windows CI）

- 通信遮断：`New-NetFirewallRule -DisplayName WinInspectorBlock -Direction Outbound -Program <exe> -Action Block`、`Set-NetFirewallProfile -LogBlocked True`。`-Mode All` と `-Mode Files` と `-Mode Apps` を実行し、完走とドロップログに exe の記録が 0 件であることを確認します（【要実機確認】：runner 上でのログ取得）。
- 読み取り専用の回帰：fixture 作成の直後と実行の直後に、対象フォルダの（パス、サイズ、CreationTime、LastWriteTime、LastAccessTime、Attributes）を比較し、同じことを確認します。レジストリの HKCU 側もテスト用キーの値数を比較します。

### 6.4 保証の範囲（README に書くこと）

- 保証するのは「本プログラムが通信を発生させないこと」と「対象のファイルとレジストリを変更しないこと」です。
- 範囲外：Windows のクラウド照会、Defender のスキャン、OneDrive の同期など OS 側の動作。
- 自己昇格のための `Process.Start` は、UAC 承認後に自分自身を起動するだけで、通信はしないことを明記します。

---

## 7. テストと CI

### 7.1 単体テスト（xUnit、Linux）

PS 版の `tests/unit/Logic.Tests.ps1` と `tests/static` の内容を次のように移植します。

| テスト対象 | 代表的なケース |
|---|---|
| Rot13 | 英字の往復（二回で元に戻る）、数字と記号と非 ASCII は変えない、空文字 |
| UserAssistDecoder | 長さ 67 は null、68 は OK、回数は offset 4、時刻は offset 60、FILETIME 0 と負数は時刻 null（回数のみ）、範囲外は null |
| KnownFolderMap | 7 件すべて、大文字小文字の違い、空の値は GUID を残す、未知の GUID は残す |
| LastAccessRules | 0、1、0x80000002、0x80000003、null（設定不明）、0xFFFFFFFF（`0xFFFFFFFF` と表示）、負の int（`-2147483646` は 0x80000002） |
| TrackingRules | 0 は無効、1 は有効、null は値なし、0x80000000 は有効（9 章 差分 1） |
| AppClassifier | 3.6 の順序すべて。境界（ちょうど N 日前は候補）、InstallDate の 8 桁で無効な日付（`20261340`）、`Update` の小文字、KB の小文字、名前が空の `DisplayName`（`??` は空文字を残す） |
| ExclusionRules | ReleaseType の大文字小文字、SystemComponent の `"1"`、Appx の System と Framework |
| EvidenceMatcher | 5 つの方法と順序、RunCount 0 の除去、除外ルートの無効化、exe の親が除外ルートなら無効、代表の Method、LastRunSource、CountOnly の集約、RunCount の最大値 |
| InstallSizePolicy | EstimatedSize 0 は未設定、KB 換算、ルートと Program Files は計測しない、同一 InstallLocation が 2 件以上は計測しない |
| WinPath | 末尾の `\`、ルートの保持、`C:\Foo` と `C:\FooBar` の接頭辞判定、大文字小文字 |
| CsvWriter | 二重引用、`""`、null、DateTime 形式、True/False、保護列の `'` 付与（`= + - @ TAB CR LF`）、BOM と CRLF |
| SummaryBuilder | 行の順序と数、`未実行`、Top10 の `( bytes)`、状態の優先順位（中断 > 一部エラー > 完了） |
| PsdReader | config.sample.psd1 と exclusions.psd1 の読み取り、未対応構文の警告と無視 |
| CliParser | 大文字小文字、`-Target` の複数値、不明な引数、整数の失敗、CLI > config の優先（LowerPriority は config から読まない） |
| FileScanner（偽の FS） | 行の順序（DFS）、フォルダ集計（直下と部分木）、Depth の境界、リパースポイントの 1 行記録、アクセス拒否の継続、中断、Exclude の境界、出力先の除外 |

### 7.2 走査の Linux 検証（実ディレクトリ）

- `/tmp` に一時ツリーを作り、通常ファイル、深いネスト、シンボリックリンク（辿らないこと）、隠しファイルを置きます。`IFileSystem` の Linux 実装（`DirectorySeparatorChar` を使う版）で走査します。
- 期待値は偽の FS の結果と同じであること。

### 7.3 Windows テスト（windows-latest）

- **レジストリ**：テスト用キー `HKCU\Software\WinInspectorTest\` に uninstall 風の値と UserAssist の ROT13 名の値（実在のバイナリ形式）を作り、読み取り元のルートを差し替えて読ませます。キーの作成は CI の PowerShell ステップで行い、テストコードは読み取りだけにします。
- **ショートカット**：`.lnk` の作成は CI の PowerShell ステップ（`WScript.Shell` の `CreateShortcut().Save()`）で行います。テストコードに書き込み系を入れないためです。
- **アクセス拒否**：`icacls` でフォルダの読み取りを拒否したものを作る（PS 版の方式）。
- **長いパス**：300 文字超のパスを作る（`longPathAware` の動作確認）。
- **日本語・絵文字・カンマ・引用符・`=` を含むファイル名**（CSV 検査）。
- **実行**：`dotnet test tests/WinInspector.Windows.Tests`、続けて publish 済みの exe を fixture フォルダに対して `-Mode Files -Target <fixture> -OutputDir <tmp>` で実行し、CSV の内容を期待値と比べます（列、行数、DFS の順序、数式対策）。
- **性能**：合成 10 万件の走査時間を記録します（目安の更新用）。
- **アーティファクトの扱い**：fixture で生成した CSV と summary は、比較後に削除します。アップロードしません。

### 7.4 CI の追加（既存 `ci.yml` に jobs を足す形）

整形はレビューで担保し、CI の format ゲートは行いません。既存の `linux` と `windows` の jobs は変更しません。次の jobs を追加します。

```yaml
  csharp-linux:               # ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - run: dotnet test tests/WinInspector.Core.Tests
      - run: dotnet test tests/WinInspector.StaticChecks

  csharp-windows:             # windows-latest（needs: csharp-linux）
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - run: dotnet test tests/WinInspector.Windows.Tests
      - run: dotnet publish src/WinInspector.Cli -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/win-x64
      - run: <fixture 作成（PowerShell）>    # shell: pwsh
      - run: <exe 実行と CSV 検証、通信遮断、読み取り専用の回帰>   # shell: pwsh
      - run: <SHA256SUMS.txt 作成>
      - uses: actions/upload-artifact@v4
        with: { name: WinInspector-win-x64, path: publish/win-x64/*, retention-days: 14 }
```

- 公開リポジトリのため、Windows ランナーの分数は課金されません（design.md 6.3 の前提）。
- 任意（優先度 低）：Windows ジョブで PS 版と exe を同じ fixture に対して実行し、CSV を正規化（行の並べ替え、`LastRunTime` の時刻比較の許容）して差分を出す「並行検証」ジョブ。両実装の出力一致を継続的に確かめられます。

---

## 8. 共存と移行

### 8.1 出力

- **出力先・ファイル名・列・文字コードを PS 版と揃えます。** 同じ `%LOCALAPPDATA%\WinInspector\Logs\` を使い、どちらの実装の出力でも同じフォルダを見れば済むようにします。
- 同じフォルダに両方の実装の出力が混ざる場合があります。ログの中身から実装を判別できるよう、summary の 1 行目以降に「実装」の行を残します（9章 差分 2 の値）。

### 8.2 ログ形式の互換

- `summary_*.txt` の行順と文言、CSV の列順、ヘッダの文字列は PS 版と同一にします。
- 9 章の差分として挙げた項目だけが異なります。

### 8.3 README への追記方針

- 追記するもの：
  - 「exe 版（推奨）」の節。取得元は CI の artifact であり、Release は作らないこと。
  - PowerShell 不要であること、ダウンロード後の「ブロックの解除」と SmartScreen の扱い、`-Elevate` の仕組みと UAC。
  - exe 版と PS 版の違い（9 章の差分を要約）。
  - 通信遮断の検証方法（6 章）と、保証の範囲（6.4）。
  - 同じ日時の再実行で同じフォルダに上書きされること。
- 変更しないもの：PowerShell 版の手順と、既存の「動作環境」「実行」「出力」「個人情報」の節は、exe 版の節と並べて残します（PS 版は同じ仕様で動き続けるため）。

### 8.4 検査対象とドキュメント

- `docs/verification-checklist.md` に exe 版の確認を追加します（exe のハッシュ照合、`-Elevate` の UAC、ダブルクリックでの一時停止、summary の「実装」行）。
- `docs/design-csharp.md`（この設計書を保存する場所の案）を作るかどうかは確認点 6 で決めてください。

---

## 9. PS 版との意図的な差分（要確認）

| # | 項目 | PS 版 | C# 版（案） | 理由と推奨 |
|---|---|---|---|---|
| 1 | DWORD の扱い（Start_TrackProgs） | `[int]` キャスト。0x80000000 などは例外になり「値なし」扱い | 符号なし正規化して 0 以外は有効 | 0x80000000 を有効と見るのが正しいと判断。PS 版の `Get-AppTrackingSetting` にも同じ修正を入れる |
| 2 | summary の「PowerShell」「言語モード」行 | `PowerShell: 7.x` と `言語モード: FullLanguage` | 行は残し、値を `実行形式: WinInspector.exe（.NET 10.0.x）` と `言語モード: 該当なし（exe 版）` に置き換える | 行の位置を揃えるため。確認点 2 |
| 3 | 制約付き言語モードと PowerShell バージョンの検査 | 起動時に検査して exit 2 | 削除 | exe では不要 |
| 4 | 出力先の走査除外の範囲 | `-OutputDir` に渡すのは実行フォルダ（runDir）だけ。過去の実行フォルダは走査される | 出力先フォルダ全体を除外する | design.md 4章「出力先フォルダは自動的に除外」と一致させる。PS 版も同じ修正を行うことを推奨。確認点 3 |
| 5 | 中断時の終了コード | 明示的な exit なし（0 の見込み） | 0 | 【要実機確認】。差分があれば PS 版の値に合わせる |
| 6 | ルートの列挙に失敗した InstallLocation | 例外が内部で捕捉されて `0` を FolderSum として返す（`WindowsApps` などで発生し得る。design.md 3.3 の「空欄と理由」に反する） | 列挙失敗は null（未計測）にし、エラーとして記録する | 設計意図に合わせる。PS 版も修正することを推奨。確認点 5 |
| 7 | 負の EstimatedSize | 符号付きのまま計算し、負の SizeBytes が出る | PS 版と同じ（符号付き） | 不正値の扱いは別途決める。推奨は「負の値は未設定扱い」。両実装に入れる |
| 8 | 同じ秒の再実行 | 同じフォルダに上書き | 同じ | README に明記する。変更するなら両実装 |
| 9 | Appx の NonRemovable | `Get-AppxPackage` の値を使う | 取得できなければ `false` とし、summary に注記（3.3 の案 B） | PoC の結果で決める。確認点 6 |
| 10 | Appx の行順 | `Get-AppxPackage` の順 | `FindPackagesForUser` の順 | 順序は保証しない（比較テストは並べ替えてから行う） |
| 11 | 進捗表示 | `Write-Progress`（ホスト UI） | stderr に `\r` で上書き表示 | コンソール環境の差 |

---

## 10. ユーザー確認点（推奨付き）

1. **配布形式**：self-contained（単一 exe、サイズ約 30〜45 MB）か、framework-dependent（数 MB、.NET 10 Runtime が別途必要）か。
   - **推奨：self-contained**。PowerShell 不要という目的に合い、導入の手間がない。
2. **summary の「実装」「言語モード」の行**：値を exe 向けに置き換えてよいか（9 章 2）。
   - **推奨：行の位置を保ったまま値を置き換える**。行の数と順序が同じなので、比較と読み取りがそのまま使える。
3. **出力先の走査除外の範囲**：runDir のみ（PS 版の現状）か、OutputDir 全体（design.md 4章の記述）か（9 章 4）。
   - **推奨：OutputDir 全体**。過去のログ（個人情報を含む）を再走査しないため。PS 版も同時に直す。
4. **署名**：v1 は未署名のままでよいか。署名する場合は証明書の調達と CI での署名が必要。
   - **推奨：v1 は未署名**。README で SmartScreen の手順を説明する。署名は v2 で判断する。
5. **ルートの列挙失敗の扱い**：FolderSum を空欄（未計測）にするか、PS 版の 0 のままか（9 章 6）。
   - **推奨：空欄にしてエラーに記録**。design.md 3.3 の方針に合う。
6. **Appx の NonRemovable**：PoC で取得できなければ `false` として注記する案 B でよいか（9 章 9）。
   - **推奨：案 B**。影響は System 署名などの除外で大部分が補われる。PoC で案 A が確認できれば A を採用。
7. **負の EstimatedSize**：PS 版の挙動を維持するか、未設定扱いにするか（9 章 7）。
   - **推奨：未設定扱い**（両実装に入れる）。
8. **設定ファイルの扱い**：exe の隣の `config\config.psd1` を読む方式でよいか（3.11 節）。
   - **推奨：そのまま採用**。単一ファイルの exe でも動き、既存の設定ファイルの形式も使える。
9. **自己昇格の方式**：同じ exe の `-Elevate`（推奨）か、管理者専用の別 exe（`requireAdministrator` マニフェスト）か。
   - **推奨：`-Elevate`**。通常実行で UAC を出さずに済む。`Process.Start` の例外を 1 箇所に限定し、6.2 の検査で守る。
10. **PS 版との並行検証ジョブ**：Windows CI で両実装を同じ fixture に対して実行して比較するジョブを入れるか（7.4）。
    - **推奨：入れる**（優先度は低めでよい）。両実装の出力差を継続的に検知できる。
11. **Linux でのビルド**：Windows TFM のプロジェクトを Linux でビルドできるかは PoC 次第（1.4）。できない場合は Windows 層を別ソリューションに分けてよいか。
    - **推奨：PoC で判断し、ダメなら分ける**。Core のテストは Linux で動かし続ける。

---

## 付録：実装の着手順（Codex 向け）

1. **PoC（Windows 実機または windows-latest）**：NonRemovable の取得方法、`FindPackagesForUser` の動作、ShellLink の `Load` と `GetPath` の動作、`EnumerationOptions` の既定値（隠しファイルの扱い）を確認する。結果は 9 章に反映する。
2. **Core**：`WinPath`、`Rot13`、`UserAssistDecoder`、`KnownFolderMap`、`LastAccessRules`、`TrackingRules`、`AppClassifier`、`ExclusionRules`、`EvidenceMatcher`、`InstallSizePolicy`、`CsvWriter`、`SummaryBuilder`、`OutputGuard`、`PsdReader`、`CliParser`。各々に単体テストを付ける（7.1）。
3. **FileScanner**：偽の FS でテストを先に書き、3.2 の DFS 順序を再現する。Linux の実ツリーでも確認する（7.2）。
4. **Windows 層**：レジストリ（読み取り専用）、Appx、Prefetch、DriveInfo、ShellLink（読み取り専用の COM）、優先度、自己昇格。
5. **Cli**：`Program.cs` の配線、設定の合成、フェーズ実行、中断処理、summary、`-Elevate`、`-NoPause`。
6. **静的検査**：BannedSymbols.txt、IL メタデータ検査、構文検査、検査器自身のテスト（6章）。
7. **CI**：`csharp-linux` と `csharp-windows` の追加（7.4）。
8. **ドキュメント**：README の追記（8.3）、verification-checklist の追加（8.4）。PS 版の既存テストは変更しない。

## 0.6 ユーザー決定事項（2026-11-17）

10章の確認点は、すべて推奨どおりで承認された。

1. 配布形式：self-contained（単一 exe）。
2. summary の行：位置を保ち、値を exe 向けに置き換える。
3. 走査除外：出力先フォルダ全体。PowerShell 版にも同じ修正を入れる。
4. 署名：v1 は未署名。README に SmartScreen の手順を書く。
5. ルート列挙失敗時：FolderSum は空欄（未計測）とし、エラーに記録する。PowerShell 版にも同じ修正を入れる。
6. Appx の NonRemovable：取得できなければ false＋注記（案 B）。PoC で取得方法が確認できれば案 A。
7. 負の EstimatedSize：未設定扱いにする。PowerShell 版にも同じ修正を入れる。
8. 設定ファイル：exe の隣の config\config.psd1 を読む方式を採用。
9. 自己昇格：同じ exe の -Elevate 方式。
10. PS 版との並行検証ジョブ：CI に入れる（優先度は低め）。
11. Linux ビルド：PoC で判断し、必要なら Windows 層を分ける。

あわせて 9 章の差分 1（Start_TrackProgs の符号なし正規化）も PowerShell 版へ入れる。
