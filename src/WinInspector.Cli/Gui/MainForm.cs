using System.ComponentModel;
using System.Globalization;
using WinInspector.Core.Cli;
using WinInspector.Core.Gui;
using WinInspector.Windows.Fs;
using WinInspector.Windows.Process;
using WinInspector.Windows.Registry;

namespace WinInspector.Cli.Gui;

/// <summary>
/// The settings window. It shows values and calls Core for every decision (validation, saving, CLI mapping).
/// It does not decide what is valid or what to save.
/// </summary>
internal sealed class MainForm : Form, IMessageFilter
{
    private static readonly Size MinimumWindow = new(640, 560);
    // Smallest height of the result group. Below the full height the page scrolls, but the summary stays usable.
    private const int ResultMinHeight = 240;
    // Gap around the content, the same 3px margin the earlier single TableLayoutPanel gave each group.
    private const int ContentMargin = 3;
    private const int WheelStepPixels = 48;
    private const int WmMouseWheel = 0x020A;

    private readonly CliOptions _configured;
    private readonly IPathProbe _probe = new WindowsPathProbe();
    private readonly bool _isAdmin = IsAdministrator();
    private readonly bool _autoRun;
    private readonly ErrorProvider _errorProvider = new();
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 1000 };

    private bool _saveOnClose;
    private CancellationTokenSource? _cts;
    private bool _closeWhenDone;
    private DateTime _runStarted;
    private InspectionResult? _lastResult;
    private string? _lastOutputRoot;
    private WindowBounds? _bounds;
    // The "run as administrator" value that came from the settings, not the forced value shown in an administrator session.
    private bool _runAsAdminSaved;

    // The page: _top (warning, settings, run) sits at its preferred height; _result takes the rest of the window.
    // When the window is too short, the page scrolls instead of squeezing the content.
    private readonly Panel _scroll = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly TableLayoutPanel _top = new() { ColumnCount = 1, RowCount = 3 };
    private readonly GroupBox _result = new() { Text = "結果", Padding = new Padding(8) };
    private bool _laying;

    private readonly Label _warning = new() { AutoSize = true, ForeColor = Color.Maroon, Visible = false };
    private readonly RadioButton _modeAll = new() { Text = "すべて", AutoSize = true };
    private readonly RadioButton _modeFiles = new() { Text = "ファイルのみ", AutoSize = true };
    private readonly RadioButton _modeApps = new() { Text = "アプリのみ", AutoSize = true };
    private readonly ListBox _targets = new() { Height = 96, Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended, IntegralHeight = false };
    private readonly Button _addTarget = new() { Text = "追加…", AutoSize = true };
    private readonly Button _removeTarget = new() { Text = "削除", AutoSize = true };
    private readonly Button _resetTargets = new() { Text = "既定に戻す", AutoSize = true };
    private readonly ListBox _excludes = new() { Height = 64, Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended, IntegralHeight = false };
    private readonly Button _addExclude = new() { Text = "追加…", AutoSize = true };
    private readonly Button _removeExclude = new() { Text = "削除", AutoSize = true };
    private readonly TextBox _outputDir = new() { Dock = DockStyle.Fill };
    private readonly Button _browseOutput = new() { Text = "参照…", AutoSize = true };
    private readonly Button _resetOutput = new() { Text = "既定に戻す", AutoSize = true };
    private readonly Label _outputHint = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly NumericUpDown _unusedDays = new() { Minimum = GuiLimits.MinUnusedDays, Maximum = GuiLimits.MaxUnusedDays, Width = 90 };
    private readonly NumericUpDown _folderDepth = new() { Minimum = GuiLimits.MinFolderDepth, Maximum = GuiLimits.MaxFolderDepth, Width = 70 };
    private readonly CheckBox _runAdmin = new() { AutoSize = true };
    private readonly Button _start = new() { Text = "検査を開始", AutoSize = true };
    private readonly Button _cancel = new() { Text = "中止", AutoSize = true, Enabled = false };
    private readonly Label _status = new() { AutoSize = true, Text = "待機中" };
    private readonly Label _elapsed = new() { AutoSize = true };
    private readonly Label _detail = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly ProgressBar _progress = new() { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30, Dock = DockStyle.Fill, Visible = false };
    private readonly Label _notes = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly TextBox _summary = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Dock = DockStyle.Fill };
    private readonly Button _openFolder = new() { Text = "出力フォルダを開く", AutoSize = true, Enabled = false };
    private readonly Button _copySummary = new() { Text = "summary をコピー", AutoSize = true, Enabled = false };

    public MainForm(CliOptions configured, GuiSettings initial, IReadOnlyList<string> warnings, bool preserveOnClose, bool autoRun)
    {
        _configured = configured;
        _autoRun = autoRun;
        _saveOnClose = !preserveOnClose;
        _bounds = initial.WindowBounds;

        Text = "WinInspector";
        MinimumSize = MinimumWindow;
        if (initial.WindowBounds is { } saved)
        {
            // A saved position on a screen that is gone (or a smaller one now) is moved into the work area of the screen it is on.
            var fitted = WindowPlacement.FitToWorkArea(saved, WorkAreaFor(saved), MinimumWindow.Width, MinimumWindow.Height);
            StartPosition = FormStartPosition.Manual;
            Location = new Point(fitted.X, fitted.Y);
            Size = new Size(fitted.Width, fitted.Height);
        }
        else
        {
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(780, 860);
        }

        BuildLayout();
        WireEvents();
        Application.AddMessageFilter(this);

        _warning.Text = string.Join(Environment.NewLine, warnings);
        _warning.Visible = warnings.Count > 0;
        ApplySettings(initial);
        LayoutContent();
    }

    /// <summary>The work area of the screen that holds the saved window (or is nearest to it), as plain integers for Core.</summary>
    private static WindowBounds WorkAreaFor(WindowBounds saved)
    {
        var screen = Screen.FromRectangle(new Rectangle(saved.X, saved.Y, Math.Max(1, saved.Width), Math.Max(1, saved.Height)));
        var area = screen.WorkingArea;
        return new WindowBounds(area.X, area.Y, area.Width, area.Height);
    }

    private void BuildLayout()
    {
        _top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _top.Controls.Add(_warning, 0, 0);
        _top.Controls.Add(BuildSettingsGroup(), 0, 1);
        _top.Controls.Add(BuildRunGroup(), 0, 2);
        BuildResultGroup();
        _scroll.Controls.Add(_top);
        _scroll.Controls.Add(_result);
        Controls.Add(_scroll);
    }

    /// <summary>
    /// Places the top block at its preferred height and the result group below it. The result group takes the
    /// remaining height (the same as the earlier percent row). When that would be less than <see cref="ResultMinHeight"/>,
    /// the result keeps that height and the page scrolls.
    /// </summary>
    private void LayoutContent()
    {
        if (_laying) return;
        _laying = true;
        try
        {
            // Full width first. If the content does not fit, the vertical scroll bar takes its width, so the content is narrower.
            var topHeight = _top.GetPreferredSize(new Size(_scroll.Width, 0)).Height;
            var resultTop = topHeight + ContentMargin;
            var overflows = resultTop + ResultMinHeight + ContentMargin > _scroll.Height;
            var width = Math.Max(0, _scroll.Width - (overflows ? SystemInformation.VerticalScrollBarWidth : 0));
            topHeight = _top.GetPreferredSize(new Size(width, 0)).Height;
            resultTop = topHeight + ContentMargin;

            var top = new Rectangle(0, 0, width, topHeight);
            var result = new Rectangle(ContentMargin, resultTop, Math.Max(0, width - 2 * ContentMargin), Math.Max(ResultMinHeight, _scroll.Height - resultTop - ContentMargin));
            if (_top.Bounds != top) _top.Bounds = top;
            if (_result.Bounds != result) _result.Bounds = result;
            _scroll.AutoScrollMinSize = new Size(0, resultTop + ResultMinHeight + ContentMargin);
        }
        finally
        {
            _laying = false;
        }
    }

    private GroupBox BuildSettingsGroup()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 0 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddRow(grid, "モード", Flow(_modeAll, _modeFiles, _modeApps), null);
        AddRow(grid, "対象フォルダ", _targets, Column(_addTarget, _removeTarget, _resetTargets));
        AddRow(grid, "除外フォルダ", _excludes, Column(_addExclude, _removeExclude));
        AddRow(grid, "出力先", _outputDir, Flow(_browseOutput, _resetOutput));
        AddRow(grid, "", _outputHint, null);
        AddRow(grid, "未使用と判定する日数", Flow(_unusedDays, new Label { Text = "日", AutoSize = true }), null);
        AddRow(grid, "フォルダ集計の深さ", Flow(_folderDepth, new Label { Text = "階層", AutoSize = true }), null);
        AddRow(grid, "", _runAdmin, null);

        var box = new GroupBox { Text = "検査の設定", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8) };
        box.Controls.Add(grid);
        return box;
    }

    private GroupBox BuildRunGroup()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(Flow(_start, _cancel), 0, 0);
        layout.Controls.Add(_status, 0, 1);
        layout.Controls.Add(_progress, 0, 2);
        layout.Controls.Add(_elapsed, 0, 3);
        layout.Controls.Add(_detail, 0, 4);

        var box = new GroupBox { Text = "実行", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8) };
        box.Controls.Add(layout);
        return box;
    }

    private void BuildResultGroup()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_notes, 0, 0);
        layout.Controls.Add(_summary, 0, 1);
        layout.Controls.Add(Flow(_openFolder, _copySummary), 0, 2);

        // The group's bounds are set by LayoutContent, so it does not dock.
        _result.Controls.Add(layout);
    }

    private void WireEvents()
    {
        _scroll.Resize += OnContentChanged;
        _top.Layout += OnContentChanged;
        _modeAll.CheckedChanged += OnModeChanged;
        _modeFiles.CheckedChanged += OnModeChanged;
        _modeApps.CheckedChanged += OnModeChanged;
        _addTarget.Click += OnAddTarget;
        _removeTarget.Click += OnRemoveTarget;
        _resetTargets.Click += OnResetTargets;
        _targets.KeyDown += OnTargetsKeyDown;
        _addExclude.Click += OnAddExclude;
        _removeExclude.Click += OnRemoveExclude;
        _excludes.KeyDown += OnExcludesKeyDown;
        _browseOutput.Click += OnBrowseOutput;
        _resetOutput.Click += OnResetOutput;
        _outputDir.Leave += OnValidationEvent;
        _unusedDays.ValueChanged += OnValidationEvent;
        _folderDepth.ValueChanged += OnValidationEvent;
        _start.Click += OnStartClicked;
        _cancel.Click += OnCancelClicked;
        _openFolder.Click += OnOpenFolder;
        _copySummary.Click += OnCopySummary;
        _clock.Tick += OnClockTick;
        Shown += OnShown;
        FormClosing += OnFormClosing;
    }

    private void ApplySettings(GuiSettings settings)
    {
        _modeAll.Checked = settings.Mode == "All";
        _modeFiles.Checked = settings.Mode == "Files";
        _modeApps.Checked = settings.Mode == "Apps";
        if (!_modeAll.Checked && !_modeFiles.Checked && !_modeApps.Checked) _modeAll.Checked = true;

        _targets.Items.Clear();
        foreach (var target in settings.Targets ?? DefaultTargetsNow()) _targets.Items.Add(target);
        _excludes.Items.Clear();
        foreach (var exclude in settings.Excludes) _excludes.Items.Add(exclude);
        _outputDir.Text = settings.OutputDir ?? string.Empty;
        _unusedDays.Value = Math.Clamp(settings.UnusedDays, GuiLimits.MinUnusedDays, GuiLimits.MaxUnusedDays);
        _folderDepth.Value = Math.Clamp(settings.FolderDepth, GuiLimits.MinFolderDepth, GuiLimits.MaxFolderDepth);

        _runAsAdminSaved = settings.RunAsAdministrator;
        _runAdmin.Checked = _isAdmin || settings.RunAsAdministrator;
        _runAdmin.Text = _isAdmin ? "管理者として実行中" : "管理者として実行（Prefetch も使用の根拠にします。実行時に Windows の確認画面が出ます）";
        _outputHint.Text = "空欄は既定の出力先: " + GuiSettingsStore.DefaultOutputDir;

        ApplyEnabledState();
        RefreshErrors();
    }

    /// <summary>Enables the controls that apply to the current mode, and disables everything while a run is active.</summary>
    private void ApplyEnabledState()
    {
        var idle = _cts is null;
        var folders = idle && !_modeApps.Checked;
        _modeAll.Enabled = idle;
        _modeFiles.Enabled = idle;
        _modeApps.Enabled = idle;
        _unusedDays.Enabled = idle && !_modeFiles.Checked;
        _folderDepth.Enabled = folders;
        _targets.Enabled = folders;
        _addTarget.Enabled = folders;
        _removeTarget.Enabled = folders;
        _resetTargets.Enabled = folders;
        _excludes.Enabled = folders;
        _addExclude.Enabled = folders;
        _removeExclude.Enabled = folders;
        _outputDir.Enabled = idle;
        _browseOutput.Enabled = idle;
        _resetOutput.Enabled = idle;
        _runAdmin.Enabled = idle && !_isAdmin;
        _start.Enabled = idle;
        _cancel.Enabled = !idle;
        _progress.Visible = !idle;
    }

    private void OnModeChanged(object? sender, EventArgs e)
    {
        ApplyEnabledState();
        RefreshErrors();
    }

    private void OnValidationEvent(object? sender, EventArgs e) => RefreshErrors();

    private void OnAddTarget(object? sender, EventArgs e) => AddFolder(_targets, "検査するフォルダを選んでください");

    private void OnAddExclude(object? sender, EventArgs e) => AddFolder(_excludes, "除外するフォルダを選んでください");

    private void OnRemoveTarget(object? sender, EventArgs e) => RemoveSelected(_targets);

    private void OnRemoveExclude(object? sender, EventArgs e) => RemoveSelected(_excludes);

    private void OnTargetsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete) RemoveSelected(_targets);
    }

    private void OnExcludesKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete) RemoveSelected(_excludes);
    }

    private void OnResetTargets(object? sender, EventArgs e)
    {
        _targets.Items.Clear();
        foreach (var target in DefaultTargetsNow()) _targets.Items.Add(target);
        RefreshErrors();
    }

    private void OnBrowseOutput(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog { Description = "結果を保存するフォルダを選んでください", UseDescriptionForTitle = true, ShowNewFolderButton = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) _outputDir.Text = dialog.SelectedPath ?? string.Empty;
        RefreshErrors();
    }

    private void OnResetOutput(object? sender, EventArgs e)
    {
        _outputDir.Text = string.Empty;
        RefreshErrors();
    }

    private void AddFolder(ListBox list, string description)
    {
        using var dialog = new FolderBrowserDialog { Description = description, UseDescriptionForTitle = true, ShowNewFolderButton = false };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var path = dialog.SelectedPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path)) return;
        if (list.Items.Cast<string>().Any(item => PathRules.SameFolder(item, path)))
        {
            _notes.Text = "既に追加されています: " + path;
            return;
        }
        list.Items.Add(path);
        _notes.Text = string.Empty;
        RefreshErrors();
    }

    private void RemoveSelected(ListBox list)
    {
        var indices = list.SelectedIndices.Cast<int>().OrderByDescending(index => index).ToArray();
        foreach (var index in indices) list.Items.RemoveAt(index);
        RefreshErrors();
    }

    private void OnStartClicked(object? sender, EventArgs e) => StartInspection();

    private void StartInspection()
    {
        if (_cts is not null) return;
        var settings = ReadSettings();
        var effective = EffectiveTargets(settings);
        var validation = GuiSettingsValidator.Validate(settings, effective, _probe);
        ShowValidation(validation);
        if (!validation.IsValid)
        {
            _status.Text = "入力内容を確認してください（" + validation.Errors.Count.ToString(CultureInfo.InvariantCulture) + " 件）";
            ControlFor(validation.Errors[0].Field)?.Focus();
            return;
        }

        // The elevated child (-GuiAutoRun) is already administrator; it must not ask for elevation again (UAC loop).
        if (settings.RunAsAdministrator && !_isAdmin && !_autoRun)
        {
            RestartElevated(settings, effective);
            return;
        }

        var saved = TrySave(settings, out var saveError);
        BeginRun(settings, effective);
        if (!saved) _notes.Text = saveError ?? string.Empty;
    }

    private void BeginRun(GuiSettings settings, IReadOnlyList<string> effective)
    {
        var options = GuiOptionsMapper.ToCliOptions(settings, _configured, effective);
        _lastOutputRoot = settings.OutputDir ?? GuiSettingsStore.DefaultOutputDir;
        _lastResult = null;
        _summary.Text = string.Empty;
        _openFolder.Enabled = false;
        _copySummary.Enabled = false;
        _status.Text = "準備しています…";
        _detail.Text = string.Empty;
        var cts = new CancellationTokenSource();
        _cts = cts;
        _runStarted = DateTime.Now;
        _clock.Start();
        ApplyEnabledState();
        var progress = new Progress<InspectionProgress>(OnProgress);
        _ = ExecuteAsync(options, cts.Token, progress);
    }

    private async Task ExecuteAsync(CliOptions options, CancellationToken token, IProgress<InspectionProgress> progress)
    {
        InspectionResult? result = null;
        string? problem = null;
        try
        {
            result = await Task.Run(() => Orchestrator.Execute(options, token, progress));
        }
        catch (Orchestrator.OutputPrecheckException ex)
        {
            problem = "出力先を準備できませんでした：" + ex.Message + "。出力先を変えて、もう一度お試しください。";
        }
        catch (Exception ex)
        {
            problem = "検査を完了できませんでした：" + ex.Message + "。";
        }
        EndRun(result, problem);
    }

    private void OnProgress(InspectionProgress progress)
    {
        _status.Text = InspectionProgressText.Format(progress);
        _detail.Text = progress.CurrentPath ?? string.Empty;
    }

    private void EndRun(InspectionResult? result, string? problem)
    {
        _clock.Stop();
        _cts?.Dispose();
        _cts = null;
        _lastResult = result;
        if (result is not null)
        {
            _summary.Text = result.SummaryText;
            _status.Text = StateText(RunOutcome.From(result.Interrupted, result.ErrorCount), result.ErrorCount);
            _detail.Text = "出力先: " + result.RunDirectory;
            _openFolder.Enabled = true;
            _copySummary.Enabled = true;
        }
        else
        {
            _summary.Text = string.Empty;
            _status.Text = problem ?? "検査を完了できませんでした。";
            _detail.Text = string.Empty;
        }
        ApplyEnabledState();
        if (_closeWhenDone) Close();
    }

    private static string StateText(RunState state, int errorCount) => state switch
    {
        RunState.Interrupted => "状態：中断しました（途中までの結果を保存しました）",
        RunState.PartialErrors => "状態：一部エラー（" + errorCount.ToString(CultureInfo.InvariantCulture) + " 件。詳細は出力フォルダの errors ファイル）",
        _ => "状態：完了",
    };

    private void OnCancelClicked(object? sender, EventArgs e) => RequestCancel();

    private void RequestCancel()
    {
        if (_cts is null) return;
        _cts.Cancel();
        _cancel.Enabled = false;
        _status.Text = "中止しています…";
    }

    private void OnClockTick(object? sender, EventArgs e)
    {
        var elapsed = DateTime.Now - _runStarted;
        _elapsed.Text = "経過 " + elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }

    private void OnShown(object? sender, EventArgs e)
    {
        if (_autoRun) StartInspection();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_cts is not null)
        {
            var answer = MessageBox.Show(this, "検査を中止して閉じますか？これまでの結果は保存されます。", "WinInspector", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (answer != DialogResult.OK)
            {
                e.Cancel = true;
                return;
            }
            _closeWhenDone = true;
            RequestCancel();
            e.Cancel = true;
            return;
        }

        if (WindowState == FormWindowState.Normal) _bounds = new WindowBounds(Left, Top, Width, Height);
        if (_saveOnClose)
        {
            // Nothing to show on the way out. The last successful save is what the next start uses.
            try { GuiSettingsStore.Save(ReadSettings()); }
            catch (Exception) { }
        }
    }

    private void RestartElevated(GuiSettings settings, IReadOnlyList<string> effective)
    {
        var answer = MessageBox.Show(this, "管理者として開き直して検査を始めます。Windows の確認画面（UAC）で［はい］を選んでください。", "WinInspector", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
        if (answer != DialogResult.OK) return;
        TrySave(settings, out _);
        try
        {
            var arguments = GuiOptionsMapper.ToArguments(settings, effective, ExpandOutputDir(settings.OutputDir));
            Elevation.Relaunch(arguments, waitForExit: false);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            MessageBox.Show(this, "管理者として開き直すことができませんでした。通常の権限で実行する場合は、チェックを外してください。", "WinInspector", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "管理者として開き直せませんでした：" + ex.Message, "WinInspector", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Close();
    }

    private void OnOpenFolder(object? sender, EventArgs e)
    {
        if (_lastResult is null || _lastOutputRoot is null) return;
        try
        {
            FolderOpener.Open(_lastResult.RunDirectory, _lastOutputRoot);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "フォルダを開けませんでした。パス：" + _lastResult.RunDirectory + Environment.NewLine + ex.Message, "WinInspector", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnCopySummary(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_summary.Text)) return;
        try
        {
            Clipboard.SetText(_summary.Text);
            _notes.Text = "summary をコピーしました。";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(this, "クリップボードに書けませんでした。もう一度お試しください。", "WinInspector", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private bool TrySave(GuiSettings settings, out string? error)
    {
        try
        {
            GuiSettingsStore.Save(settings);
            _saveOnClose = true;
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = "設定を保存できませんでした：" + ex.Message;
            return false;
        }
    }

    private GuiSettings ReadSettings()
    {
        var targets = _targets.Items.Cast<string>().ToArray();
        var defaults = DefaultTargetsNow();
        var isDefault = targets.Select(PathRules.Key).SequenceEqual(defaults.Select(PathRules.Key));
        var output = _outputDir.Text.Trim();
        return new GuiSettings(
            ReadMode(),
            isDefault ? null : targets,
            _excludes.Items.Cast<string>().ToArray(),
            output.Length == 0 ? null : output,
            (int)_unusedDays.Value,
            (int)_folderDepth.Value,
            _isAdmin ? _runAsAdminSaved : _runAdmin.Checked,
            _bounds);
    }

    private string ReadMode() => _modeFiles.Checked ? "Files" : _modeApps.Checked ? "Apps" : "All";

    private static string[] EffectiveTargets(GuiSettings settings) => settings.Targets ?? DefaultTargetsNow();

    private static string[] DefaultTargetsNow() =>
        DefaultTargets.Resolve(folder => Environment.GetFolderPath(folder), path => Directory.Exists(path));

    private static string ExpandOutputDir(string? outputDir) => Path.GetFullPath(outputDir ?? GuiSettingsStore.DefaultOutputDir);

    private void RefreshErrors()
    {
        var settings = ReadSettings();
        ShowValidation(GuiSettingsValidator.Validate(settings, EffectiveTargets(settings), _probe));
    }

    private void ShowValidation(GuiValidationResult validation)
    {
        _errorProvider.Clear();
        foreach (var group in validation.Errors.GroupBy(issue => issue.Field))
        {
            var control = ControlFor(group.Key);
            if (control is not null) _errorProvider.SetError(control, string.Join(Environment.NewLine, group.Select(issue => issue.Message)));
        }
        _notes.Text = string.Join(Environment.NewLine, validation.Notes);
    }

    private Control? ControlFor(string field) => field switch
    {
        "mode" => _modeAll,
        "targets" => _targets,
        "excludes" => _excludes,
        "outputDir" => _outputDir,
        "unusedDays" => _unusedDays,
        "folderDepth" => _folderDepth,
        _ => null,
    };

    private void OnContentChanged(object? sender, EventArgs e) => LayoutContent();

    /// <summary>
    /// The mouse wheel goes to the focused window, so with focus on a button the page would not scroll.
    /// This filter sends the wheel to the page, unless the control under the cursor scrolls its own content
    /// (a list or summary with more text than fits); then that control gets the wheel.
    /// </summary>
    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != WmMouseWheel || IsDisposed || !IsHandleCreated) return false;
        // Only wheel messages for this window (not for a dialog or another window on this thread).
        var owner = Control.FromChildHandle(m.HWnd);
        if (owner is null || owner.FindForm() != this) return false;

        var hovered = ControlUnderCursor();
        if (hovered is not null && CanScrollInside(hovered))
        {
            m.HWnd = hovered.Handle;
            return false;
        }

        var delta = unchecked((short)((m.WParam.ToInt64() >> 16) & 0xFFFF));
        ScrollPage(-delta * WheelStepPixels / 120);
        return true;
    }

    /// <summary>The deepest control under the cursor, or null when the cursor is outside this window.</summary>
    private Control? ControlUnderCursor()
    {
        var point = PointToClient(Cursor.Position);
        if (!ClientRectangle.Contains(point)) return null;
        Control current = this;
        while (true)
        {
            var child = current.GetChildAtPoint(point);
            if (child is null || !child.Visible) return current == this ? null : current;
            point = new Point(point.X - child.Left, point.Y - child.Top);
            current = child;
        }
    }

    private static bool CanScrollInside(Control control) => control switch
    {
        ListBox list => list.PreferredHeight > list.ClientSize.Height,
        TextBox { Multiline: true } text => TextRenderer.MeasureText(text.Text, text.Font, new Size(text.ClientSize.Width, int.MaxValue), TextFormatFlags.WordBreak).Height > text.ClientSize.Height,
        _ => false,
    };

    private void ScrollPage(int pixels)
    {
        var bar = _scroll.VerticalScroll;
        var maximum = Math.Max(0, bar.Maximum - bar.LargeChange + 1);
        var target = Math.Clamp(bar.Value + pixels, 0, maximum);
        if (target != bar.Value) bar.Value = target;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Application.RemoveMessageFilter(this);
            _cts?.Dispose();
            _clock.Dispose();
            _errorProvider.Dispose();
        }
        base.Dispose(disposing);
    }

    private static bool IsAdministrator()
    {
        try
        {
            return WindowsSettingsReader.IsAdministrator();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void AddRow(TableLayoutPanel grid, string caption, Control content, Control? extra)
    {
        var row = grid.RowCount;
        grid.RowCount = row + 1;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 6, 12, 6) }, 0, row);
        grid.Controls.Add(content, 1, row);
        if (extra is not null) grid.Controls.Add(extra, 2, row);
    }

    private static FlowLayoutPanel Flow(params Control[] items)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        panel.Controls.AddRange(items);
        return panel;
    }

    private static FlowLayoutPanel Column(params Control[] items)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.TopDown };
        panel.Controls.AddRange(items);
        return panel;
    }
}
