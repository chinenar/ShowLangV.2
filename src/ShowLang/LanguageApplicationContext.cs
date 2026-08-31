namespace ShowLangNative;

internal sealed class LanguageApplicationContext : ApplicationContext
{
    private readonly ShowLangSettings _settings;
    private readonly OverlayForm _overlay;
    private readonly CaretTracker _caretTracker;
    private readonly LanguageMonitor _monitor;
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _showTestItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _resumeItem;
    private readonly Dictionary<int, ToolStripMenuItem> _scaleItems = new();
    private readonly Dictionary<int, ToolStripMenuItem> _opacityItems = new();
    private readonly System.Windows.Forms.Timer _previewTimer;

    internal LanguageApplicationContext()
    {
        _settings = ShowLangSettings.Load();
        _overlay = new OverlayForm(_settings);
        _caretTracker = new CaretTracker(_overlay);
        _monitor = new LanguageMonitor(_overlay, _caretTracker);

        _previewTimer = new System.Windows.Forms.Timer
        {
            Interval = 120,
        };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            if (!_monitor.IsPaused)
            {
                _monitor.ShowCurrent(force: true);
            }
        };

        _showTestItem = new ToolStripMenuItem(
            "Show test",
            null,
            (_, _) => _monitor.ShowCurrent(force: true));
        _pauseItem = new ToolStripMenuItem(
            "Pause",
            null,
            (_, _) => PauseMonitoring());
        _resumeItem = new ToolStripMenuItem(
            "Resume",
            null,
            (_, _) => ResumeMonitoring());

        ContextMenuStrip menu = new();
        menu.Items.Add(_showTestItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_resumeItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateAppearanceMenu());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "ShowLang",
            ContextMenuStrip = menu,
            Visible = true,
        };

        _monitor.Start();
        UpdateControlState();
        UpdateAppearanceChecks();
    }

    private ToolStripMenuItem CreateAppearanceMenu()
    {
        ToolStripMenuItem appearanceItem = new("Appearance");
        ToolStripMenuItem sizeItem = new("Size");
        AddScaleOption(sizeItem, 75, "Small (75%)");
        AddScaleOption(sizeItem, 100, "Normal (100%)");
        AddScaleOption(sizeItem, 125, "Large (125%)");
        AddScaleOption(sizeItem, 150, "Extra large (150%)");
        AddScaleOption(sizeItem, 200, "Huge (200%)");

        ToolStripMenuItem transparencyItem = new("Box transparency");
        AddOpacityOption(transparencyItem, 100, "0% (Opaque)");
        AddOpacityOption(transparencyItem, 85, "15%");
        AddOpacityOption(transparencyItem, 70, "30%");
        AddOpacityOption(transparencyItem, 55, "45%");
        AddOpacityOption(transparencyItem, 40, "60%");

        appearanceItem.DropDownItems.Add(sizeItem);
        appearanceItem.DropDownItems.Add(transparencyItem);
        appearanceItem.DropDownItems.Add(new ToolStripSeparator());
        appearanceItem.DropDownItems.Add(
            "Reset appearance",
            null,
            (_, _) => ResetAppearance());
        return appearanceItem;
    }

    private void AddScaleOption(
        ToolStripMenuItem parent,
        int scalePercent,
        string label)
    {
        ToolStripMenuItem item = new(label);
        item.Click += (_, _) => SetScale(scalePercent);
        _scaleItems.Add(scalePercent, item);
        parent.DropDownItems.Add(item);
    }

    private void AddOpacityOption(
        ToolStripMenuItem parent,
        int opacityPercent,
        string label)
    {
        ToolStripMenuItem item = new(label);
        item.Click += (_, _) => SetOpacity(opacityPercent);
        _opacityItems.Add(opacityPercent, item);
        parent.DropDownItems.Add(item);
    }

    private void SetScale(int scalePercent)
    {
        if (_settings.ScalePercent == scalePercent)
        {
            return;
        }

        _settings.ScalePercent = scalePercent;
        SaveAndApplyAppearance();
    }

    private void SetOpacity(int opacityPercent)
    {
        if (_settings.OpacityPercent == opacityPercent)
        {
            return;
        }

        _settings.OpacityPercent = opacityPercent;
        SaveAndApplyAppearance();
    }

    private void ResetAppearance()
    {
        _settings.ResetAppearance();
        SaveAndApplyAppearance();
    }

    private void SaveAndApplyAppearance()
    {
        _settings.Save();
        _overlay.ApplyAppearance(
            _settings.ScalePercent,
            _settings.OpacityPercent);
        UpdateAppearanceChecks();
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void UpdateAppearanceChecks()
    {
        foreach ((int value, ToolStripMenuItem item) in _scaleItems)
        {
            item.Checked = value == _settings.ScalePercent;
        }

        foreach ((int value, ToolStripMenuItem item) in _opacityItems)
        {
            item.Checked = value == _settings.OpacityPercent;
        }
    }

    private void PauseMonitoring()
    {
        _previewTimer.Stop();
        _monitor.Pause();
        UpdateControlState();
    }

    private void ResumeMonitoring()
    {
        _monitor.Resume();
        UpdateControlState();
    }

    private void UpdateControlState()
    {
        bool paused = _monitor.IsPaused;
        _pauseItem.Enabled = !paused;
        _resumeItem.Enabled = paused;
        _showTestItem.Enabled = !paused;
        _trayIcon.Text = paused
            ? "ShowLang (Paused)"
            : "ShowLang";
    }

    protected override void ExitThreadCore()
    {
        _previewTimer.Stop();
        _previewTimer.Dispose();
        _monitor.Dispose();
        _caretTracker.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.ContextMenuStrip?.Dispose();
        _trayIcon.Dispose();
        _overlay.Dispose();
        base.ExitThreadCore();
    }
}
