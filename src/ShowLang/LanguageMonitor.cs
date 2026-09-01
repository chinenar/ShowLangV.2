namespace ShowLangNative;

internal sealed class LanguageMonitor : IDisposable
{
    private const int CaretCaptureDelayMilliseconds = 45;
    private const int FocusCaptureDelayMilliseconds = 70;
    private const int FocusSuppressionMilliseconds = 300;
    private const int DuplicateFocusWindowMilliseconds = 350;

    private readonly OverlayForm _overlay;
    private readonly CaretWorkerClient _worker;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Threading.Timer _focusTimer;
    private readonly NativeMethods.WinEventDelegate _focusCallback;

    private IntPtr? _previousLayout;
    private LanguageChange? _pending;
    private IntPtr _focusHook;
    private bool _checking;
    private bool _processing;
    private bool _paused = true;
    private bool _disposed;
    private long _sequence;
    private long _focusSequence;
    private long _suppressFocusUntil;
    private int _focusQueryActive;
    private int _languageRequestActive;
    private IntPtr _lastFocusWindow;
    private IntPtr _lastFocusLayout;
    private Rectangle _lastFocusBounds;
    private long _lastFocusShownAt;
    private IntPtr _proxyForeground;
    private bool _foregroundHasEditableProxy;
    private bool _proxyLeftButtonWasDown;
    private bool _proxyFieldActive;
    private int _proxyHorizontalOffset;

    internal LanguageMonitor(
        OverlayForm overlay,
        CaretWorkerClient worker)
    {
        _overlay = overlay;
        _ = _overlay.Handle;
        _worker = worker;
        _focusCallback = OnFocusChanged;
        _timer = new System.Windows.Forms.Timer
        {
            Interval = 50,
        };
        _timer.Tick += (_, _) => ShowCurrent(force: false);
        _focusTimer = new System.Threading.Timer(
            _ => FocusTimerElapsed(),
            null,
            Timeout.Infinite,
            Timeout.Infinite);
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
        Interlocked.Exchange(ref _languageRequestActive, 0);
        StopFocusMonitoring(interruptQuery: true);
        _timer.Stop();
        _previousLayout = null;
        ResetEditableProxyTracking();
        _worker.Stop();
        _overlay.HideImmediately();
        AppLog.Write("STATE paused");
    }

    internal void Resume()
    {
        if (!_paused || _disposed)
        {
            return;
        }

        _previousLayout = null;
        ResetEditableProxyTracking();
        _paused = false;
        _worker.Start();
        InstallFocusHook();
        ShowCurrent(force: false);
        _timer.Start();
        AppLog.Write("STATE resumed");
    }

    internal void ShowCurrent(bool force)
    {
        if (IsInactive || _checking)
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

            CheckEditableProxyClick(foreground);

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

            Interlocked.Exchange(ref _languageRequestActive, 1);
            CancelFocusForLanguageChange();

            ushort languageId = unchecked(
                (ushort)((long)layout & 0xFFFF));
            LanguageChange change = new(
                ++_sequence,
                foreground,
                layout,
                LanguageNames.FromLanguageId(languageId));

            if (TryGetEditableProxyLanguageTarget(
                    foreground,
                    out AnchorTarget proxyTarget))
            {
                _pending = null;
                Show(change, proxyTarget);
                Interlocked.Exchange(ref _languageRequestActive, 0);
                return;
            }

            if (CaretLocator.TryLocateNative(
                    foreground,
                    out AnchorTarget native))
            {
                _pending = null;
                Show(change, native);
                Interlocked.Exchange(ref _languageRequestActive, 0);
                return;
            }

            _pending = change;
            if (!_processing)
            {
                _ = ProcessPendingAsync();
            }
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
            if (!_processing && _pending is null)
            {
                Interlocked.Exchange(ref _languageRequestActive, 0);
            }
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
            while (!IsInactive && _pending is LanguageChange change)
            {
                _pending = null;
                await Task.Delay(CaretCaptureDelayMilliseconds);
                if (IsInactive)
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

                if (IsInactive)
                {
                    return;
                }

                if (_pending is LanguageChange newer)
                {
                    if (newer.Foreground == change.Foreground)
                    {
                        if (accessible is not null)
                        {
                            change = newer;
                            _pending = null;
                        }
                        else
                        {
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
            if (!IsInactive && _pending is not null)
            {
                _ = ProcessPendingAsync();
            }
            else
            {
                Interlocked.Exchange(ref _languageRequestActive, 0);
            }
        }
    }

    private void CheckEditableProxyClick(IntPtr foreground)
    {
        if (_proxyForeground != foreground)
        {
            _proxyForeground = foreground;
            _foregroundHasEditableProxy =
                CaretLocator.HasEditableProxy(foreground);
            _proxyLeftButtonWasDown = false;
            _proxyFieldActive = false;
            _proxyHorizontalOffset = 0;
        }

        if (!_foregroundHasEditableProxy)
        {
            return;
        }

        short state = NativeMethods.GetAsyncKeyState(
            NativeMethods.VkLeftButton);
        bool isDown = (state & 0x8000) != 0;
        bool clickCompleted = !isDown
            && (_proxyLeftButtonWasDown || (state & 0x0001) != 0);
        _proxyLeftButtonWasDown = isDown;
        if (!clickCompleted)
        {
            return;
        }

        if (!CaretLocator.TryLocateEditableProxyTarget(
                foreground,
                out AnchorTarget target,
                out int horizontalOffset))
        {
            _proxyFieldActive = false;
            _proxyHorizontalOffset = 0;
            return;
        }

        _proxyFieldActive = true;
        _proxyHorizontalOffset = horizontalOffset;

        uint threadId = NativeMethods.GetInputThreadId(foreground);
        if (threadId == 0)
        {
            return;
        }

        IntPtr layout = NativeMethods.GetKeyboardLayout(threadId);
        ushort languageId = unchecked(
            (ushort)((long)layout & 0xFFFF));
        LanguageChange change = new(
            0,
            foreground,
            layout,
            LanguageNames.FromLanguageId(languageId));
        if (IsDuplicateFocusShow(change, target))
        {
            return;
        }

        Show(
            change,
            target with
            {
                Source = "Focus " + target.Source,
            });
    }

    private bool TryGetEditableProxyLanguageTarget(
        IntPtr foreground,
        out AnchorTarget target)
    {
        target = default;
        if (!_foregroundHasEditableProxy
            || _proxyForeground != foreground)
        {
            return false;
        }

        if (_proxyFieldActive
            && CaretLocator.TryLocateEditableProxyTarget(
                foreground,
                _proxyHorizontalOffset,
                out target))
        {
            return true;
        }

        if (!CaretLocator.TryLocateEditableProxyTarget(
                foreground,
                out target,
                out int horizontalOffset))
        {
            return false;
        }

        _proxyFieldActive = true;
        _proxyHorizontalOffset = horizontalOffset;
        return true;
    }

    private void ResetEditableProxyTracking()
    {
        _proxyForeground = IntPtr.Zero;
        _foregroundHasEditableProxy = false;
        _proxyLeftButtonWasDown = false;
        _proxyFieldActive = false;
        _proxyHorizontalOffset = 0;
    }

    private void InstallFocusHook()
    {
        if (_focusHook != IntPtr.Zero || IsInactive)
        {
            return;
        }

        _focusHook = NativeMethods.SetWinEventHook(
            NativeMethods.EventObjectFocus,
            NativeMethods.EventObjectFocus,
            IntPtr.Zero,
            _focusCallback,
            0,
            0,
            NativeMethods.WineventOutOfContext
                | NativeMethods.WineventSkipOwnProcess);

        if (_focusHook == IntPtr.Zero)
        {
            AppLog.Write("FOCUS hook installation failed");
        }
    }

    private void StopFocusMonitoring(bool interruptQuery)
    {
        Interlocked.Increment(ref _focusSequence);
        try
        {
            _focusTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
        }

        if (_focusHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_focusHook);
            _focusHook = IntPtr.Zero;
        }

        if (interruptQuery
            && Volatile.Read(ref _focusQueryActive) != 0)
        {
            _worker.InterruptCurrentQuery();
        }
    }

    private void CancelFocusForLanguageChange()
    {
        Volatile.Write(
            ref _suppressFocusUntil,
            Environment.TickCount64 + FocusSuppressionMilliseconds);
        Interlocked.Increment(ref _focusSequence);
        try
        {
            _focusTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
        }

        if (Volatile.Read(ref _focusQueryActive) != 0)
        {
            _worker.InterruptCurrentQuery();
        }
    }

    private void OnFocusChanged(
        IntPtr hook,
        uint eventType,
        IntPtr hWnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime)
    {
        if (eventType != NativeMethods.EventObjectFocus
            || IsInactive
            || Environment.TickCount64
                < Volatile.Read(ref _suppressFocusUntil))
        {
            return;
        }

        Interlocked.Increment(ref _focusSequence);
        try
        {
            _focusTimer.Change(
                FocusCaptureDelayMilliseconds,
                Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void FocusTimerElapsed()
    {
        long sequence = Volatile.Read(ref _focusSequence);
        _ = ProcessFocusAsync(sequence);
    }

    private async Task ProcessFocusAsync(long sequence)
    {
        if (IsInactive
            || Volatile.Read(ref _languageRequestActive) != 0
            || Interlocked.CompareExchange(
                ref _focusQueryActive,
                1,
                0) != 0)
        {
            return;
        }

        try
        {
            if (!IsFocusRequestCurrent(sequence))
            {
                return;
            }

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
            ushort languageId = unchecked(
                (ushort)((long)layout & 0xFFFF));
            LanguageChange change = new(
                0,
                foreground,
                layout,
                LanguageNames.FromLanguageId(languageId));

            if (CaretLocator.TryLocateNative(
                    foreground,
                    out AnchorTarget native))
            {
                PostFocusShow(sequence, change, native);
                return;
            }

            AnchorTarget? accessible =
                await _worker.QueryAsync(foreground)
                    .ConfigureAwait(false);
            if (accessible is not null)
            {
                PostFocusShow(sequence, change, accessible.Value);
            }
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }
        finally
        {
            Interlocked.Exchange(ref _focusQueryActive, 0);
            long latest = Volatile.Read(ref _focusSequence);
            if (!IsInactive
                && latest != sequence
                && Volatile.Read(ref _languageRequestActive) == 0
                && Environment.TickCount64
                    >= Volatile.Read(ref _suppressFocusUntil))
            {
                try
                {
                    _focusTimer.Change(
                        FocusCaptureDelayMilliseconds,
                        Timeout.Infinite);
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }
    }

    private bool IsFocusRequestCurrent(long sequence)
    {
        return !IsInactive
            && sequence == Volatile.Read(ref _focusSequence)
            && Volatile.Read(ref _languageRequestActive) == 0
            && Environment.TickCount64
                >= Volatile.Read(ref _suppressFocusUntil);
    }

    private void PostFocusShow(
        long sequence,
        LanguageChange change,
        AnchorTarget target)
    {
        if (!IsFocusRequestCurrent(sequence)
            || !IsStillCurrent(change)
            || _overlay.IsDisposed
            || !_overlay.IsHandleCreated)
        {
            return;
        }

        void ShowOnUiThread()
        {
            if (!IsFocusRequestCurrent(sequence)
                || !IsStillCurrent(change)
                || IsDuplicateFocusShow(change, target))
            {
                return;
            }

            AnchorTarget focusTarget = target with
            {
                Source = "Focus " + target.Source,
            };
            Show(change, focusTarget);
        }

        try
        {
            if (_overlay.InvokeRequired)
            {
                _overlay.BeginInvoke((Action)ShowOnUiThread);
            }
            else
            {
                ShowOnUiThread();
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private bool IsDuplicateFocusShow(
        LanguageChange change,
        AnchorTarget target)
    {
        long now = Environment.TickCount64;
        bool duplicate = _lastFocusWindow == change.Foreground
            && _lastFocusLayout == change.Layout
            && _lastFocusBounds == target.Bounds
            && now - _lastFocusShownAt
                <= DuplicateFocusWindowMilliseconds;

        if (!duplicate)
        {
            _lastFocusWindow = change.Foreground;
            _lastFocusLayout = change.Layout;
            _lastFocusBounds = target.Bounds;
            _lastFocusShownAt = now;
        }

        return duplicate;
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

    private bool IsInactive =>
        Volatile.Read(ref _paused) || Volatile.Read(ref _disposed);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _paused = true;
        _sequence++;
        _pending = null;
        Interlocked.Exchange(ref _languageRequestActive, 0);
        StopFocusMonitoring(interruptQuery: true);
        _timer.Stop();
        _worker.Stop();
        _overlay.HideImmediately();
        _timer.Dispose();
        _focusTimer.Dispose();
    }

    private readonly record struct LanguageChange(
        long Sequence,
        IntPtr Foreground,
        IntPtr Layout,
        string Language);
}
