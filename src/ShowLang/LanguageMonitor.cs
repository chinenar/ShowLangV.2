namespace ShowLangNative;

internal sealed class LanguageMonitor : IDisposable
{
    private readonly OverlayForm _overlay;
    private readonly System.Windows.Forms.Timer _timer;
    private IntPtr? _previousLayout;
    private bool _checking;

    internal LanguageMonitor(OverlayForm overlay)
    {
        _overlay = overlay;
        _timer = new System.Windows.Forms.Timer
        {
            Interval = 20,
        };
        _timer.Tick += (_, _) => ShowCurrent(force: false);
    }

    internal void Start()
    {
        ShowCurrent(force: false);
        _timer.Start();
    }

    internal void ShowCurrent(bool force)
    {
        if (_checking)
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

            CaretLocator.Track(foreground);
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
            AnchorTarget target = CaretLocator.Locate(foreground);
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
        _timer.Stop();
        _timer.Dispose();
    }
}
