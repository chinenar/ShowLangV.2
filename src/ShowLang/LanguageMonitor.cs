namespace ShowLangNative;

internal sealed class LanguageMonitor : IDisposable
{
    private const int CaretCaptureDelayMilliseconds = 45;

    private readonly OverlayForm _overlay;
    private readonly CaretWorkerClient _worker;
    private readonly System.Windows.Forms.Timer _timer;

    private IntPtr? _previousLayout;
    private LanguageChange? _pending;
    private bool _checking;
    private bool _processing;
    private bool _paused = true;
    private long _sequence;

    internal LanguageMonitor(
        OverlayForm overlay,
        CaretWorkerClient worker)
    {
        _overlay = overlay;
        _worker = worker;
        _timer = new System.Windows.Forms.Timer
        {
            Interval = 50,
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
        _sequence++;
        _pending = null;
        _timer.Stop();
        _previousLayout = null;
        _worker.Stop();
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
        _worker.Start();
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
            LanguageChange change = new(
                ++_sequence,
                foreground,
                layout,
                LanguageNames.FromLanguageId(languageId));

            if (CaretLocator.TryLocateNative(
                    foreground,
                    out AnchorTarget native))
            {
                _pending = null;
                Show(change, native);
                return;
            }

            // Accessibility work starts only for this actual language
            // change. No caret cache is warmed while the user is idle.
            _pending = change;
            if (!_processing)
            {
                _ = ProcessPendingAsync();
            }
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

    private async Task ProcessPendingAsync()
    {
        if (_processing)
        {
            return;
        }

        _processing = true;
        try
        {
            while (!_paused && _pending is LanguageChange change)
            {
                _pending = null;
                await Task.Delay(CaretCaptureDelayMilliseconds);
                if (_paused)
                {
                    return;
                }

                if (_pending is LanguageChange newerBeforeQuery)
                {
                    if (newerBeforeQuery.Foreground
                        == change.Foreground)
                    {
                        change = newerBeforeQuery;
                        _pending = null;
                    }
                    else
                    {
                        continue;
                    }
                }

                AnchorTarget? accessible =
                    await _worker.QueryAsync(change.Foreground);

                if (_paused)
                {
                    return;
                }

                if (_pending is LanguageChange newer)
                {
                    if (newer.Foreground == change.Foreground)
                    {
                        if (accessible is not null)
                        {
                            // A valid caret from the same focused field can
                            // be reused for the newest rapid layout switch.
                            change = newer;
                            _pending = null;
                        }
                        else
                        {
                            // The switcher may temporarily own focus. Retry
                            // only for the newer language-change event.
                            _pending = newer;
                            continue;
                        }
                    }
                    else
                    {
                        continue;
                    }
                }

                if (change.Sequence != _sequence
                    || !IsStillCurrent(change))
                {
                    continue;
                }

                AnchorTarget target = accessible
                    ?? CaretLocator.CreateScreenFallback(
                        change.Foreground,
                        "Screen corner fallback (no caret on change)");
                Show(change, target);
            }
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }
        finally
        {
            _processing = false;
            if (!_paused && _pending is not null)
            {
                _ = ProcessPendingAsync();
            }
        }
    }

    private static bool IsStillCurrent(LanguageChange change)
    {
        if (NativeMethods.GetForegroundWindow() != change.Foreground)
        {
            return false;
        }

        uint threadId = NativeMethods.GetInputThreadId(change.Foreground);
        return threadId != 0
            && NativeMethods.GetKeyboardLayout(threadId) == change.Layout;
    }

    private void Show(LanguageChange change, AnchorTarget target)
    {
        _overlay.ShowLanguage(change.Language, target);

        NativeMethods.GetWindowThreadProcessId(
            change.Foreground,
            out uint processId);
        AppLog.Write(
            $"SHOW language={change.Language} source={target.Source} "
            + $"bounds={target.Bounds} "
            + $"hwnd=0x{change.Foreground.ToInt64():X} "
            + $"pid={processId}");
    }

    public void Dispose()
    {
        _paused = true;
        _sequence++;
        _pending = null;
        _timer.Stop();
        _worker.Stop();
        _overlay.HideImmediately();
        _timer.Dispose();
    }

    private readonly record struct LanguageChange(
        long Sequence,
        IntPtr Foreground,
        IntPtr Layout,
        string Language);
}
