namespace ShowLangNative;

internal sealed class LanguageMonitor : IDisposable
{
    private readonly OverlayForm _overlay;
    private readonly CaretTracker _tracker;
    private readonly System.Windows.Forms.Timer _timer;
    private IntPtr? _previousLayout;
    private bool _checking;
    private bool _paused = true;

    internal LanguageMonitor(
        OverlayForm overlay,
        CaretTracker tracker)
    {
        _overlay = overlay;
        _tracker = tracker;
        _timer = new System.Windows.Forms.Timer
        {
            Interval = 20,
        };
        _timer.Tick += (_, _) => ShowCurrent(force: false);
    }

    internal bool IsPaused => _paused;

    internal void Start()
    {
        Resume();
    }

    internal void Pause()
    {
        if (_paused)
        {
            return;
        }

        _paused = true;
        _timer.Stop();
        _previousLayout = null;
        _tracker.Pause();
        _overlay.HideImmediately();
        AppLog.Write("STATE paused");
    }

    internal void Resume()
    {
        if (!_paused)
        {
            return;
        }

        _previousLayout = null;
        _paused = false;
        _tracker.Resume();
        ShowCurrent(force: false);
        _timer.Start();
        AppLog.Write("STATE resumed");
    }

    internal void ShowCurrent(bool force)
    {
        if (_paused || _checking)
        {
            return;
        }

        _checking = true;
        try
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return;
            }

            _tracker.ObserveForeground(foreground);
            uint threadId = NativeMethods.GetInputThreadId(foreground);
            if (threadId == 0)
            {
                return;
            }

            IntPtr layout = NativeMethods.GetKeyboardLayout(threadId);
            if (!force && _previousLayout == layout)
            {
                return;
            }

            bool firstReading = _previousLayout is null;
            _previousLayout = layout;
            if (firstReading && !force)
            {
                return;
            }

            ushort languageId = unchecked(
                (ushort)((long)layout & 0xFFFF));
            string language = LanguageNames.FromLanguageId(languageId);

            // Never wait for UI Automation on the input path. A native
            // caret or the last verified background snapshot is used now.
            _tracker.NoteLanguageChange(foreground);
            AnchorTarget target =
                _tracker.GetImmediateTarget(foreground);
            _overlay.ShowLanguage(language, target);

            NativeMethods.GetWindowThreadProcessId(
                foreground,
                out uint processId);
            AppLog.Write(
                $"SHOW language={language} source={target.Source} "
                + $"bounds={target.Bounds} hwnd=0x{foreground.ToInt64():X} "
                + $"pid={processId}");
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }
        finally
        {
            _checking = false;
        }
    }

    public void Dispose()
    {
        _paused = true;
        _timer.Stop();
        _tracker.Pause();
        _overlay.HideImmediately();
        _timer.Dispose();
    }
}
