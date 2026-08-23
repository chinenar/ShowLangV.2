using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using Accessibility;
using DrawingRectangle = System.Drawing.Rectangle;

namespace ShowLangNative;

internal enum AnchorKind
{
    Caret,
    Window,
}

internal readonly record struct AnchorTarget(
    DrawingRectangle Bounds,
    AnchorKind Kind,
    string Source);

internal static class CaretLocator
{
    private const int CacheLifetimeMilliseconds = 700;
    private const int AccessibleQueryIntervalMilliseconds = 120;
    private static readonly object CacheGate = new();
    private static IntPtr _cachedWindow;
    private static AnchorTarget _cachedTarget;
    private static long _cachedAt;
    private static IntPtr _lastRequestedWindow;
    private static long _lastQueryAt;
    private static int _queryRunning;

    internal static void Track(IntPtr foreground)
    {
        if (TryWin32Caret(foreground, out DrawingRectangle caret))
        {
            Store(
                foreground,
                new AnchorTarget(caret, AnchorKind.Caret, "Win32 caret"));
            return;
        }

        ScheduleAccessibleQuery(foreground);
    }

    internal static AnchorTarget Locate(IntPtr foreground)
    {
        if (TryWin32Caret(foreground, out DrawingRectangle caret))
        {
            AnchorTarget target = new(
                caret,
                AnchorKind.Caret,
                "Win32 caret");
            Store(foreground, target);
            return target;
        }

        return TryGetCached(foreground, out AnchorTarget cached)
            ? cached
            : CreateWindowFallback(foreground);
    }

    private static void ScheduleAccessibleQuery(IntPtr foreground)
    {
        long now = Environment.TickCount64;
        bool windowChanged = foreground != _lastRequestedWindow;
        if (!windowChanged
            && now - _lastQueryAt < AccessibleQueryIntervalMilliseconds)
        {
            return;
        }

        _lastRequestedWindow = foreground;
        _lastQueryAt = now;
        if (Interlocked.CompareExchange(ref _queryRunning, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(() => QueryAccessibleCaret(foreground));
    }

    private static void QueryAccessibleCaret(IntPtr foreground)
    {
        try
        {
            DrawingRectangle bounds;
            string source;
            if (TryMsaaCaret(foreground, out bounds))
            {
                source = "MSAA caret";
            }
            else if (TryAutomationCaret(foreground, out bounds))
            {
                source = "UI Automation caret";
            }
            else
            {
                Invalidate(foreground);
                return;
            }

            if (NativeMethods.GetForegroundWindow() == foreground)
            {
                Store(
                    foreground,
                    new AnchorTarget(bounds, AnchorKind.Caret, source));
            }
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }
        finally
        {
            Interlocked.Exchange(ref _queryRunning, 0);
        }
    }

    private static void Store(IntPtr foreground, AnchorTarget target)
    {
        lock (CacheGate)
        {
            _cachedWindow = foreground;
            _cachedTarget = target;
            _cachedAt = Environment.TickCount64;
        }
    }

    private static void Invalidate(IntPtr foreground)
    {
        lock (CacheGate)
        {
            if (_cachedWindow != foreground)
            {
                return;
            }

            _cachedWindow = IntPtr.Zero;
            _cachedTarget = default;
            _cachedAt = 0;
        }
    }

    private static bool TryGetCached(
        IntPtr foreground,
        out AnchorTarget target)
    {
        lock (CacheGate)
        {
            bool fresh = _cachedWindow == foreground
                && Environment.TickCount64 - _cachedAt
                    <= CacheLifetimeMilliseconds;
            target = fresh ? _cachedTarget : default;
            return fresh;
        }
    }

    private static AnchorTarget CreateWindowFallback(IntPtr foreground)
    {
        Screen screen = Screen.FromHandle(foreground);
        return new AnchorTarget(
            screen.WorkingArea,
            AnchorKind.Window,
            "Screen corner fallback");
    }
    private static bool TryWin32Caret(
        IntPtr foreground,
        out DrawingRectangle bounds)
    {
        bounds = default;
        uint threadId = NativeMethods.GetInputThreadId(foreground);
        if (threadId == 0)
        {
            return false;
        }

        NativeMethods.GuiThreadInfo info = new()
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.GuiThreadInfo>(),
        };

        if (!NativeMethods.GetGUIThreadInfo(threadId, ref info)
            || info.CaretWindow == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.NativePoint topLeft = new(
            info.CaretRect.Left,
            info.CaretRect.Top);
        NativeMethods.NativePoint bottomRight = new(
            info.CaretRect.Right,
            info.CaretRect.Bottom);

        if (!NativeMethods.ClientToScreen(info.CaretWindow, ref topLeft)
            || !NativeMethods.ClientToScreen(info.CaretWindow, ref bottomRight))
        {
            return false;
        }

        bounds = new DrawingRectangle(
            topLeft.X,
            topLeft.Y,
            Math.Max(2, bottomRight.X - topLeft.X),
            Math.Max(16, bottomRight.Y - topLeft.Y));
        return IsPlausibleCaret(bounds, foreground);
    }

    private static bool TryMsaaCaret(
        IntPtr foreground,
        out DrawingRectangle bounds)
    {
        bounds = default;
        object? accessibleObject = null;
        try
        {
            Guid iid = typeof(IAccessible).GUID;
            int result = NativeMethods.AccessibleObjectFromWindow(
                IntPtr.Zero,
                NativeMethods.ObjidCaret,
                ref iid,
                out accessibleObject!);

            if (result < 0 || accessibleObject is not IAccessible accessible)
            {
                return false;
            }

            accessible.accLocation(
                out int left,
                out int top,
                out int width,
                out int height,
                0);
            bounds = new DrawingRectangle(
                left,
                top,
                Math.Max(2, width),
                Math.Max(16, height));
            return IsPlausibleCaret(bounds, foreground);
        }
        catch (Exception exception) when (
            exception is COMException
            or ArgumentException
            or InvalidCastException)
        {
            return false;
        }
        finally
        {
            if (accessibleObject is not null
                && Marshal.IsComObject(accessibleObject))
            {
                Marshal.FinalReleaseComObject(accessibleObject);
            }
        }
    }

    private static bool TryAutomationCaret(
        IntPtr foreground,
        out DrawingRectangle bounds)
    {
        bounds = default;
        try
        {
            AutomationElement? focused = AutomationElement.FocusedElement;
            if (focused is null)
            {
                return false;
            }

            NativeMethods.GetWindowThreadProcessId(
                foreground,
                out uint foregroundProcessId);
            if (foregroundProcessId == 0)
            {
                return false;
            }

            if (!focused.TryGetCurrentPattern(
                    TextPattern.Pattern,
                    out object? patternObject)
                || patternObject is not TextPattern textPattern)
            {
                return false;
            }

            if (!IsEditableTextTarget(focused, textPattern, foreground))
            {
                return false;
            }

            TextPatternRange[] ranges = textPattern.GetSelection();
            if (ranges.Length == 0)
            {
                return false;
            }

            TextPatternRange range = ranges[^1];
            bool collapsed = range.CompareEndpoints(
                TextPatternRangeEndpoint.Start,
                range,
                TextPatternRangeEndpoint.End) == 0;
            if (!collapsed)
            {
                return false;
            }

            return TryCollapsedRangeBounds(range, out bounds)
                && IsPlausibleCaret(bounds, foreground);
        }
        catch (Exception exception) when (
            exception is ElementNotAvailableException
            or InvalidOperationException
            or COMException)
        {
            return false;
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
            return false;
        }
    }

    private static bool IsEditableTextTarget(
        AutomationElement focused,
        TextPattern textPattern,
        IntPtr foreground)
    {
        try
        {
            if (focused.Current.ControlType == ControlType.Edit)
            {
                return true;
            }

            if (focused.TryGetCurrentPattern(
                    ValuePattern.Pattern,
                    out object? valuePatternObject)
                && valuePatternObject is ValuePattern valuePattern
                && !valuePattern.Current.IsReadOnly)
            {
                return true;
            }

            object readOnlyValue = textPattern.DocumentRange.GetAttributeValue(
                TextPattern.IsReadOnlyAttribute);
            if (readOnlyValue is bool isReadOnly && !isReadOnly)
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(foreground, out uint processId);
            if (processId == 0)
            {
                return false;
            }

            using System.Diagnostics.Process process =
                System.Diagnostics.Process.GetProcessById((int)processId);
            string name = process.ProcessName;
            return string.Equals(name, "WindowsTerminal", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "OpenConsole", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "conhost", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCollapsedRangeBounds(
        TextPatternRange range,
        out DrawingRectangle bounds)
    {
        if (TryCaretEdge(
                range.GetBoundingRectangles(),
                useRightEdge: false,
                out bounds))
        {
            return true;
        }

        TextPatternRange character = range.Clone();
        character.ExpandToEnclosingUnit(TextUnit.Character);
        int fromStart = range.CompareEndpoints(
            TextPatternRangeEndpoint.Start,
            character,
            TextPatternRangeEndpoint.Start);
        int fromEnd = range.CompareEndpoints(
            TextPatternRangeEndpoint.Start,
            character,
            TextPatternRangeEndpoint.End);
        bool useRightEdge = fromEnd >= 0 || fromStart > 0;
        if (TryCaretEdge(
                character.GetBoundingRectangles(),
                useRightEdge,
                out bounds))
        {
            return true;
        }

        TextPatternRange nextCharacter = range.Clone();
        int movedForward = nextCharacter.MoveEndpointByUnit(
            TextPatternRangeEndpoint.End,
            TextUnit.Character,
            1);
        if (movedForward > 0
            && TryCaretEdge(
                nextCharacter.GetBoundingRectangles(),
                useRightEdge: false,
                out bounds))
        {
            return true;
        }

        TextPatternRange previousCharacter = range.Clone();
        int movedBackward = previousCharacter.MoveEndpointByUnit(
            TextPatternRangeEndpoint.Start,
            TextUnit.Character,
            -1);
        return movedBackward < 0
            && TryCaretEdge(
                previousCharacter.GetBoundingRectangles(),
                useRightEdge: true,
                out bounds);
    }
    private static bool TryCaretEdge(
        System.Windows.Rect[] rectangles,
        bool useRightEdge,
        out DrawingRectangle bounds)
    {
        bounds = default;
        for (int index = rectangles.Length - 1; index >= 0; index--)
        {
            System.Windows.Rect rectangle = rectangles[index];
            if (rectangle.IsEmpty
                || !IsUsableNumber(rectangle.X)
                || !IsUsableNumber(rectangle.Y)
                || !IsUsableNumber(rectangle.Width)
                || !IsUsableNumber(rectangle.Height)
                || rectangle.Width < 0
                || rectangle.Height <= 0)
            {
                continue;
            }

            int left = useRightEdge
                ? (int)Math.Ceiling(rectangle.Right)
                : (int)Math.Floor(rectangle.Left);
            bounds = new DrawingRectangle(
                left,
                (int)Math.Floor(rectangle.Top),
                2,
                Math.Max(16, (int)Math.Ceiling(rectangle.Height)));
            return true;
        }

        return false;
    }

    private static bool IsPlausibleCaret(
        DrawingRectangle bounds,
        IntPtr foreground)
    {
        if (bounds.Width < 1
            || bounds.Width > 96
            || bounds.Height < 4
            || bounds.Height > 240)
        {
            return false;
        }

        bool onScreen = Screen.AllScreens.Any(
            screen => screen.Bounds.IntersectsWith(bounds));
        if (!onScreen)
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(
                foreground,
                out NativeMethods.NativeRect window))
        {
            return true;
        }

        DrawingRectangle allowed = DrawingRectangle.FromLTRB(
            window.Left,
            window.Top,
            window.Right,
            window.Bottom);
        allowed.Inflate(160, 160);
        return allowed.IntersectsWith(bounds);
    }

    private static bool IsUsableNumber(double value)
    {
        return !double.IsNaN(value)
            && !double.IsInfinity(value)
            && value >= -100000
            && value <= 100000;
    }
}
