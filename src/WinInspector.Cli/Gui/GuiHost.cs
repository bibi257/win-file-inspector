using System.Threading;
using WinInspector.Core.Cli;
using WinInspector.Core.Gui;
using WinInspector.Core.Settings;
using WinInspector.Windows.Process;

namespace WinInspector.Cli.Gui;

/// <summary>
/// Starts the WinForms window. Windows Forms needs an STA thread; Main stays MTA so that the CLI keeps the same
/// COM and WinRT behavior it has today. Only the GUI runs on its own STA thread.
/// </summary>
internal static class GuiHost
{
    public static int Run(string[] args)
    {
        var exitCode = 0;
        var thread = new Thread(() => exitCode = RunOnStaThread(args));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return exitCode;
    }

    private static int RunOnStaThread(string[] args)
    {
        // A console created only for this double-click is removed before the window shows.
        ConsoleWindow.DetachIfOwnedOnly();
        System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.SystemAware);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        var stripped = GuiOptionsMapper.StripGuiFlags(args);
        CliOptions configured;
        GuiSettings initial;
        GuiLoadResult loaded;
        try
        {
            // Same pipeline as the CLI: parse, then apply config.psd1.
            var given = CliParser.Parse(stripped);
            configured = SettingsComposition.Apply(given, stripped);
            loaded = GuiSettingsStore.Load(configured);
            // Explicit arguments (for example -Gui -Mode Files, or the elevated relaunch) replace the saved values.
            initial = stripped.Length > 0 ? GuiOptionsMapper.ApplyArguments(loaded.Settings, stripped, given) : loaded.Settings;
        }
        catch (Exception ex)
        {
            System.Windows.Forms.MessageBox.Show(ex.Message, "WinInspector", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
            return 1;
        }

        // Values given on the command line (-Gui -Mode Files, or the elevated relaunch) are saved only when a run starts (1.3, 3.4).
        var preserveOnClose = loaded.PreserveOnSave || stripped.Length > 0;
        using var form = new MainForm(configured, initial, loaded.Warnings, preserveOnClose, GuiOptionsMapper.IsAutoRun(args));
        System.Windows.Forms.Application.Run(form);
        return 0;
    }
}
