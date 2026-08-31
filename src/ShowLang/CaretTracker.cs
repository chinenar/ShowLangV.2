using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace ShowLangNative;

internal sealed class CaretTracker : IDisposable
{
    private const int MaintenanceIntervalMilliseconds = 50;
    private const uint RecoveryIdleMilliseconds = 650;
    private const long ProbeTimeoutMilliseconds = 2_500;
    private const long CacheLifetimeMilliseconds = 10 * 60_000;
    private const long TimeoutLogWindowMilliseconds = 60_000;
    private const long FailedProbeBackoffMilliseconds = 2_000;

    private readonly object _gate = new();
    private readonly Control _dispatcher;
    private readonly System.Windows.Forms.Timer _maintenanceTimer;
    private readonly NativeMethods.WinEventDelegate _winEventCallback;
    private readonly List<IntPtr> _hooks = new();

    private bool _paused = true;
    private bool _disposed;
    private IntPtr _foreground;
    private int _generation;
    private IntPtr _focusWindow;
    private int _focusObject;
    private int _focusChild;
    private IntPtr _cachedWindow;
    private int _cachedGeneration;
    private AnchorTarget _cachedTarget;
    private long _cachedAt;
    private uint _lastRecoveryInputTick;
    private bool _forceRecovery = true;
    private long _lastTimeoutLogAt;
    private long _nextProbeAllowedAt;
    private ProbeState? _activeProbe;

    internal CaretTracker(Control dispatcher)
    {
        CleanupStaleProbeFiles();
        _dispatcher = dispatcher;
        _winEventCallback = OnWinEvent;
        _maintenanceTimer = new System.Windows.Forms.Timer
        {
            Interval = MaintenanceIntervalMilliseconds,
        };
        _maintenanceTimer.Tick += (_, _) => MaintenanceTick();
    }

    internal void Resume()
    {
        if (_disposed || !_paused)
        {
            return;
        }

        _paused = false;
        InstallHooks();
        ObserveForeground(NativeMethods.GetForegroundWindow());
        _maintenanceTimer.Start();
    }

    internal void Pause()
    {
        if (_paused)
        {
            return;
        }

        _paused = true;
        _maintenanceTimer.Stop();
        RemoveHooks();
        lock (_gate)
        {
            _foreground = IntPtr.Zero;
            _generation++;
            _focusWindow = IntPtr.Zero;
            _focusObject = 0;
            _focusChild = 0;
            ClearCacheLocked();
            _forceRecovery = true;
            _lastRecoveryInputTick = 0;
            _nextProbeAllowedAt = 0;
        }
        CancelActiveProbe("paused");
    }

    internal void ObserveForeground(IntPtr foreground)
    {
        if (_paused || foreground == IntPtr.Zero)
        {
            return;
        }

        lock (_gate)
        {
            if (_foreground == foreground)
            {
                return;
            }

            _foreground = foreground;
            _generation++;
            _focusWindow = IntPtr.Zero;
            _focusObject = 0;
            _focusChild = 0;
            ClearCacheLocked();
            _forceRecovery = true;
            _lastRecoveryInputTick = 0;
            _nextProbeAllowedAt = 0;
        }

        CancelActiveProbe("foreground changed");
    }

    internal void NoteLanguageChange(IntPtr foreground)
    {
        ObserveForeground(foreground);
        lock (_gate)
        {
            _forceRecovery = true;
            _nextProbeAllowedAt = 0;
        }

        CancelActiveProbe("language changed");
    }

    internal AnchorTarget GetImmediateTarget(IntPtr foreground)
    {
        ObserveForeground(foreground);
        if (CaretLocator.TryLocateNative(foreground, out AnchorTarget native))
        {
            Store(foreground, native);
            MarkCurrentInputRecovered();
            return native;
        }

        lock (_gate)
        {
            bool usable = _cachedWindow == foreground
                && _cachedGeneration == _generation
                && Environment.TickCount64 - _cachedAt
                    <= CacheLifetimeMilliseconds;
            if (usable)
            {
                return _cachedTarget with
                {
                    Source = "Cached " + _cachedTarget.Source,
                };
            }
        }

        return CaretLocator.CreateScreenFallback(foreground);
    }

    private void MaintenanceTick()
    {
        if (_paused || _disposed)
        {
            return;
        }

        IntPtr foreground = NativeMethods.GetForegroundWindow();
        ObserveForeground(foreground);
        if (!NativeMethods.TryGetLastInput(
                out uint inputTick,
                out uint idleMilliseconds))
        {
            return;
        }

        ProbeState? active;
        lock (_gate)
        {
            active = _activeProbe;
        }

        if (active is not null)
        {
            bool exited;
            try
            {
                exited = active.Process.HasExited;
            }
            catch
            {
                exited = true;
            }

            if (exited)
            {
                CompleteProbe(active);
                return;
            }

            if (inputTick != active.InputTick)
            {
                TerminateProbe(active, "new user input", backoff: false);
            }
            else if (foreground != active.Window)
            {
                TerminateProbe(active, "foreground changed", backoff: false);
            }
            else if (Environment.TickCount64 - active.StartedAt
                >= ProbeTimeoutMilliseconds)
            {
                TerminateProbe(active, "timeout", backoff: true);
            }

            return;
        }

        if (idleMilliseconds < RecoveryIdleMilliseconds
            || foreground == IntPtr.Zero)
        {
            return;
        }

        int generation;
        long now = Environment.TickCount64;
        lock (_gate)
        {
            bool hasUsableCache = _cachedWindow == foreground
                && _cachedGeneration == _generation
                && now - _cachedAt <= CacheLifetimeMilliseconds;
            bool recoveryNeeded = _forceRecovery
                || (!hasUsableCache
                    && _lastRecoveryInputTick != inputTick);
            if (_activeProbe is not null
                || !recoveryNeeded
                || now < _nextProbeAllowedAt)
            {
                return;
            }

            generation = _generation;
            _forceRecovery = false;
            _lastRecoveryInputTick = inputTick;
        }

        StartProbe(foreground, generation, inputTick);
    }

    private void StartProbe(
        IntPtr foreground,
        int generation,
        uint inputTick)
    {
        string executable = Environment.ProcessPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(executable))
        {
            return;
        }

        string outputDirectory = GetProbeDirectory();
        Directory.CreateDirectory(outputDirectory);
        string outputPath = Path.Combine(
            outputDirectory,
            Guid.NewGuid().ToString("N") + ".json");

        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        startInfo.ArgumentList.Add(CaretProbeMode.Command);
        startInfo.ArgumentList.Add(
            foreground.ToInt64().ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(outputPath);

        Process process = new()
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        ProbeState state = new(
            process,
            outputPath,
            foreground,
            generation,
            inputTick,
            Environment.TickCount64);
        process.Exited += (_, _) => PostProbeCompletion(state);

        lock (_gate)
        {
            if (_paused || _disposed || _activeProbe is not null)
            {
                process.Dispose();
                DeleteResultFile(outputPath);
                return;
            }

            _activeProbe = state;
        }

        try
        {
            if (!process.Start())
            {
                FailToStartProbe(state);
            }
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
            FailToStartProbe(state);
        }
    }

    private void FailToStartProbe(ProbeState state)
    {
        Interlocked.Exchange(ref state.CompletionClaimed, 1);
        lock (_gate)
        {
            if (ReferenceEquals(_activeProbe, state))
            {
                _activeProbe = null;
                _forceRecovery = false;
                _lastRecoveryInputTick = state.InputTick;
                _nextProbeAllowedAt = Environment.TickCount64
                    + FailedProbeBackoffMilliseconds;
            }
        }

        state.Process.Dispose();
        DeleteResultFile(state.OutputPath);
    }

    private void PostProbeCompletion(ProbeState state)
    {
        if (Volatile.Read(ref state.CompletionClaimed) != 0)
        {
            return;
        }

        try
        {
            if (!_dispatcher.IsDisposed
                && _dispatcher.IsHandleCreated)
            {
                _dispatcher.BeginInvoke(() => CompleteProbe(state));
            }
        }
        catch
        {
            // The maintenance timer will observe HasExited and complete it.
        }
    }

    private void CompleteProbe(ProbeState state)
    {
        if (Interlocked.Exchange(ref state.CompletionClaimed, 1) != 0)
        {
            return;
        }

        bool current;
        lock (_gate)
        {
            current = ReferenceEquals(_activeProbe, state);
            if (current)
            {
                _activeProbe = null;
            }
        }

        if (!current
            || Volatile.Read(ref state.TerminationRequested) != 0)
        {
            CleanupProbe(state);
            return;
        }

        try
        {
            if (state.Process.ExitCode != 0
                || !File.Exists(state.OutputPath))
            {
                return;
            }

            CaretProbePayload? payload = JsonSerializer.Deserialize<
                CaretProbePayload>(File.ReadAllText(state.OutputPath));
            if (payload is null || !payload.Success)
            {
                lock (_gate)
                {
                    _nextProbeAllowedAt = Environment.TickCount64
                        + FailedProbeBackoffMilliseconds;
                }
                return;
            }

            if (!NativeMethods.TryGetLastInput(
                    out uint inputTick,
                    out _)
                || inputTick != state.InputTick
                || NativeMethods.GetForegroundWindow() != state.Window)
            {
                return;
            }

            AnchorTarget target = payload.ToAnchorTarget();
            lock (_gate)
            {
                if (!_paused
                    && !_disposed
                    && _foreground == state.Window
                    && _generation == state.Generation)
                {
                    StoreLocked(state.Window, target);
                    _nextProbeAllowedAt = 0;
                }
            }
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }
        finally
        {
            CleanupProbe(state);
        }
    }

    private void TerminateProbe(
        ProbeState state,
        string reason,
        bool backoff)
    {
        if (Interlocked.Exchange(ref state.TerminationRequested, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref state.CompletionClaimed, 1);
        long now = Environment.TickCount64;
        lock (_gate)
        {
            if (ReferenceEquals(_activeProbe, state))
            {
                _activeProbe = null;
            }

            if (backoff)
            {
                _forceRecovery = false;
                _lastRecoveryInputTick = state.InputTick;
                _nextProbeAllowedAt = now
                    + FailedProbeBackoffMilliseconds;
            }
        }

        if (backoff
            && now - _lastTimeoutLogAt >= TimeoutLogWindowMilliseconds)
        {
            _lastTimeoutLogAt = now;
            AppLog.Write(
                $"CARET recovery process timed out "
                + $"hwnd=0x{state.Window.ToInt64():X}");
        }

        ThreadPool.QueueUserWorkItem(
            _ =>
            {
                try
                {
                    if (!state.Process.HasExited)
                    {
                        state.Process.Kill(entireProcessTree: true);
                    }

                    state.Process.WaitForExit(500);
                }
                catch
                {
                }
                finally
                {
                    CleanupProbe(state);
                }
            });
    }
    private void CancelActiveProbe(string reason)
    {
        ProbeState? active;
        lock (_gate)
        {
            active = _activeProbe;
        }

        if (active is not null)
        {
            TerminateProbe(active, reason, backoff: false);
        }
    }

    private static string GetProbeDirectory()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "ShowLang",
            "caret-probes");
    }

    private static void CleanupStaleProbeFiles()
    {
        try
        {
            string directory = GetProbeDirectory();
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (string path in Directory.EnumerateFiles(directory))
            {
                DeleteResultFile(path);
            }
        }
        catch
        {
        }
    }
    private static void CleanupProbe(ProbeState state)
    {
        try
        {
            state.Process.Dispose();
        }
        catch
        {
        }

        DeleteResultFile(state.OutputPath);
    }

    private static void DeleteResultFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
    private void Store(IntPtr foreground, AnchorTarget target)
    {
        lock (_gate)
        {
            if (_foreground == foreground)
            {
                StoreLocked(foreground, target);
            }
        }
    }

    private void StoreLocked(IntPtr foreground, AnchorTarget target)
    {
        _cachedWindow = foreground;
        _cachedGeneration = _generation;
        _cachedTarget = target;
        _cachedAt = Environment.TickCount64;
    }

    private void MarkCurrentInputRecovered()
    {
        if (!NativeMethods.TryGetLastInput(
                out uint inputTick,
                out _))
        {
            return;
        }

        lock (_gate)
        {
            _forceRecovery = false;
            _lastRecoveryInputTick = inputTick;
        }
    }
    private void ClearCacheLocked()
    {
        _cachedWindow = IntPtr.Zero;
        _cachedGeneration = 0;
        _cachedTarget = default;
        _cachedAt = 0;
    }

    private void InstallHooks()
    {
        if (_hooks.Count != 0)
        {
            return;
        }

        AddHook(
            NativeMethods.EventSystemForeground,
            NativeMethods.EventSystemForeground);
        AddHook(
            NativeMethods.EventObjectFocus,
            NativeMethods.EventObjectFocus);
        AddHook(
            NativeMethods.EventObjectLocationChange,
            NativeMethods.EventObjectLocationChange);
        AddHook(
            NativeMethods.EventObjectTextSelectionChanged,
            NativeMethods.EventObjectTextSelectionChanged);
    }

    private void AddHook(uint eventMin, uint eventMax)
    {
        IntPtr hook = NativeMethods.SetWinEventHook(
            eventMin,
            eventMax,
            IntPtr.Zero,
            _winEventCallback,
            0,
            0,
            NativeMethods.WineventOutOfContext
                | NativeMethods.WineventSkipOwnProcess);
        if (hook != IntPtr.Zero)
        {
            _hooks.Add(hook);
        }
    }

    private void RemoveHooks()
    {
        foreach (IntPtr hook in _hooks)
        {
            NativeMethods.UnhookWinEvent(hook);
        }

        _hooks.Clear();
    }

    private void OnWinEvent(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime)
    {
        if (_paused || _disposed)
        {
            return;
        }

        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (eventType == NativeMethods.EventSystemForeground)
        {
            ObserveForeground(foreground);
            return;
        }

        if (foreground == IntPtr.Zero
            || !BelongsToForegroundProcess(hwnd, foreground))
        {
            return;
        }

        if (eventType == NativeMethods.EventObjectLocationChange
            && idObject == NativeMethods.ObjidCaretSigned)
        {
            if (CaretLocator.TryLocateNative(
                    foreground,
                    out AnchorTarget target))
            {
                Store(foreground, target);
                MarkCurrentInputRecovered();
            }
            else
            {
                lock (_gate)
                {
                    _forceRecovery = true;
                }
            }
            return;
        }

        if (eventType == NativeMethods.EventObjectFocus)
        {
            bool changed;
            lock (_gate)
            {
                changed = _focusWindow != hwnd
                    || _focusObject != idObject
                    || _focusChild != idChild;
                if (changed)
                {
                    _focusWindow = hwnd;
                    _focusObject = idObject;
                    _focusChild = idChild;
                    _generation++;
                    ClearCacheLocked();
                    _forceRecovery = true;
                    _lastRecoveryInputTick = 0;
                }
            }

            if (changed)
            {
                CancelActiveProbe("focus changed");
            }
            return;
        }

        if (eventType
            == NativeMethods.EventObjectTextSelectionChanged)
        {
            lock (_gate)
            {
                _forceRecovery = true;
            }
        }
    }

    private static bool BelongsToForegroundProcess(
        IntPtr hwnd,
        IntPtr foreground)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(
            hwnd,
            out uint eventProcessId);
        NativeMethods.GetWindowThreadProcessId(
            foreground,
            out uint foregroundProcessId);
        return eventProcessId != 0
            && eventProcessId == foregroundProcessId;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _paused = true;
        _maintenanceTimer.Stop();
        _maintenanceTimer.Dispose();
        RemoveHooks();
        CancelActiveProbe("disposed");
    }

    private sealed class ProbeState
    {
        internal ProbeState(
            Process process,
            string outputPath,
            IntPtr window,
            int generation,
            uint inputTick,
            long startedAt)
        {
            Process = process;
            OutputPath = outputPath;
            Window = window;
            Generation = generation;
            InputTick = inputTick;
            StartedAt = startedAt;
        }

        internal Process Process { get; }
        internal string OutputPath { get; }
        internal IntPtr Window { get; }
        internal int Generation { get; }
        internal uint InputTick { get; }
        internal long StartedAt { get; }
        internal int TerminationRequested;
        internal int CompletionClaimed;
    }
}
