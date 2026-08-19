namespace ShowLangNative;

internal sealed class LanguageApplicationContext : ApplicationContext
{
    private readonly OverlayForm _overlay;
    private readonly LanguageMonitor _monitor;
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _showTestItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _resumeItem;

    internal LanguageApplicationContext()
    {
        _overlay = new OverlayForm();
        _monitor = new LanguageMonitor(_overlay);

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
    }

    private void PauseMonitoring()
    {
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
        _monitor.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.ContextMenuStrip?.Dispose();
        _trayIcon.Dispose();
        _overlay.Dispose();
        base.ExitThreadCore();
    }
}
