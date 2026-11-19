using System.Runtime.InteropServices;

namespace WinInspector.Windows.Process;

/// <summary>
/// Removes the console window that a double-click created for this process. A console shared with a parent
/// (cmd or PowerShell) is never detached, so CLI use keeps its output and exit code.
/// </summary>
public static class ConsoleWindow
{
    /// <returns>True when the console was detached.</returns>
    public static bool DetachIfOwnedOnly()
    {
        var list = new uint[2];
        var count = GetConsoleProcessList(list, (uint)list.Length);
        if (count != 1) return false;
        return FreeConsole();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool FreeConsole();
}
