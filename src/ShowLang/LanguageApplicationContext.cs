namespace ShowLangNative;

internal sealed class LanguageApplicationContext : ApplicationContext
{
    private readonly OverlayForm _overlay;
    private readonly LanguageMonitor _monitor;
    private readonly NotifyIcon _trayIcon;

    internal LanguageApplicationContext()
    {
        _overlay = new OverlayForm();
        _monitor = new LanguageMonitor(_overlay);

        ContextMenuStrip menu = new();
        menu.Items.Add("Show test", null, (_, _) => _monitor.ShowCurrent(force: true));
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
