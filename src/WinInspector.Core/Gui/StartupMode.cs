namespace WinInspector.Core.Gui;

public enum StartupKind { Cli, Gui }

/// <summary>
/// Chooses GUI or CLI from the arguments and the console state. Pure, so every combination can be tested on Linux.
/// -Help and -Version always win (they print and exit). -Gui opens the GUI. No arguments at all in an interactive
/// session opens the GUI. Anything else is the CLI, unchanged.
/// </summary>
public static class StartupMode
{
    public static StartupKind Decide(string[] args, bool userInteractive, bool inputRedirected, bool outputRedirected, bool errorRedirected)
    {
        if (args.Any(a => a.Equals("-Help", StringComparison.OrdinalIgnoreCase) || a.Equals("-h", StringComparison.OrdinalIgnoreCase) || a.Equals("-Version", StringComparison.OrdinalIgnoreCase)))
            return StartupKind.Cli;
        if (args.Any(a => a.Equals("-Gui", StringComparison.OrdinalIgnoreCase)))
            return StartupKind.Gui;
        if (args.Length == 0 && userInteractive && !inputRedirected && !outputRedirected && !errorRedirected)
            return StartupKind.Gui;
        return StartupKind.Cli;
    }
}
