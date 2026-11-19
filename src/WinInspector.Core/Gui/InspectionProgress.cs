using System.Globalization;

namespace WinInspector.Core.Gui;

public enum InspectionPhase { Preparing, ScanningFiles, ReadingApps, ReadingEvidence, Classifying, Writing }

public sealed record InspectionProgress(InspectionPhase Phase, int FileCount, int FolderCount, int ErrorCount, string? CurrentPath, TimeSpan Elapsed);

/// <summary>What Orchestrator.Execute returns. SummaryText is the same string that was written to SummaryPath.</summary>
public sealed record InspectionResult(string RunDirectory, string SummaryText, string SummaryPath, bool Interrupted, int ErrorCount);

public enum RunState { Completed, PartialErrors, Interrupted }

public static class RunOutcome
{
    /// <summary>Same priority as the summary's 状態 line: interrupted, then partial errors, then completed.</summary>
    public static RunState From(bool interrupted, int errorCount) =>
        interrupted ? RunState.Interrupted : errorCount > 0 ? RunState.PartialErrors : RunState.Completed;
}

public static class InspectionProgressText
{
    public static string Format(InspectionProgress progress) => progress.Phase switch
    {
        InspectionPhase.Preparing => "準備しています…",
        InspectionPhase.ScanningFiles => "ファイルを走査しています… " + progress.FileCount.ToString("N0", CultureInfo.InvariantCulture) + " 件",
        InspectionPhase.ReadingApps => "インストール済みアプリの一覧を読み取っています…",
        InspectionPhase.ReadingEvidence => "使用の記録（UserAssist・Prefetch）を読み取っています…",
        InspectionPhase.Classifying => "アプリを判定しています…",
        InspectionPhase.Writing => "結果を書き出しています…",
        _ => string.Empty,
    };
}
