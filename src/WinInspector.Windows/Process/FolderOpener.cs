using WinInspector.Core.Gui;
using WinInspector.Core.Output;
using WinInspector.Windows.Fs;

namespace WinInspector.Windows.Process;

/// <summary>
/// Opens a result folder in Explorer. This is the second place allowed to start a process (the static check
/// lists it in tools/csharp-allowlist.json with its own contract). It starts only %SystemRoot%\explorer.exe,
/// never shell-executes a path, and passes exactly one verified folder as an argument.
/// </summary>
public static class FolderOpener
{
    /// <param name="runDirectory">The run folder that Orchestrator returned.</param>
    /// <param name="outputRoot">The output folder the run was written under.</param>
    public static void Open(string runDirectory, string outputRoot)
    {
        if (!PathRules.IsDriveAbsolute(runDirectory))
            throw new InvalidOperationException("フォルダのパスはドライブ名から始まる絶対パスである必要があります。");
        if (!Directory.Exists(runDirectory))
            throw new DirectoryNotFoundException("フォルダが見つかりません: " + runDirectory);
        OutputGuard.AssertWithin(runDirectory, outputRoot);

        var probe = new WindowsPathProbe();
        if (probe.IsNetworkPath(runDirectory) || probe.HasReparsePointOnPathChain(runDirectory))
            throw new InvalidOperationException("ネットワーク上の場所、またはリンクを含むフォルダは開けません。");

        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var info = new System.Diagnostics.ProcessStartInfo { FileName = explorer, UseShellExecute = false };
        info.ArgumentList.Add(runDirectory);
        // Explorer returns quickly after handing the folder to the shell; we do not wait for it.
        using var started = System.Diagnostics.Process.Start(info);
    }
}
