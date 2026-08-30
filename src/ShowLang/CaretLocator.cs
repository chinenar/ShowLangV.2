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
    private const int AccessibleQueryTimeoutMilliseconds = 100;
    private static readonly object OneShotQueryGate = new();
    private static Task<AnchorTarget?>? _activeOneShotQuery;

    internal static async Task<AnchorTarget> LocateForLanguageChangeAsync(
        IntPtr foreground)
    {
        if (TryWin32Caret(foreground, out DrawingRectangle caret))
        {
            return new AnchorTarget(
                caret,
                AnchorKind.Caret,
                "Win32 caret");
        }

        Task<AnchorTarget?> query;
        lock (OneShotQueryGate)
        {
            if (_activeOneShotQuery is { IsCompleted: false })
            {
                return CreateWindowFallback(
                    foreground,
                    "Screen corner fallback (caret query busy)");
            }

            query = Task.Run(() => QueryAccessibleTarget(foreground));
            _activeOneShotQuery = query;
        }

        try
        {
            AnchorTarget? target = await query
                .WaitAsync(TimeSpan.FromMilliseconds(
                    AccessibleQueryTimeoutMilliseconds))
                .ConfigureAwait(false);
            if (target is AnchorTarget found
                && NativeMethods.GetForegroundWindow() == foreground)
            {
                return found;
            }
        }
        catch (TimeoutException)
        {
            AppLog.Write(
                $"CARET timeout after {AccessibleQueryTimeoutMilliseconds}ms "
                + $"hwnd=0x{foreground.ToInt64():X}");
            return CreateWindowFallback(
                foreground,
                "Screen corner fallback (caret timeout)");
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }
        finally
        {
            if (query.IsCompleted)
            {
                lock (OneShotQueryGate)
                {
                    if (ReferenceEquals(_activeOneShotQuery, query))
                    {
                        _activeOneShotQuery = null;
                    }
                }
            }
        }

        return CreateWindowFallback(foreground);
    }

    private static AnchorTarget? QueryAccessibleTarget(
        IntPtr foreground)
    {
        try
        {
            if (TryMsaaCaret(foreground, out DrawingRectangle bounds))
            {
                return new AnchorTarget(
                    bounds,
                    AnchorKind.Caret,
                    "MSAA caret");
            }

            if (TryAutomationCaret(foreground, out bounds))
            {
                return new AnchorTarget(
                    bounds,
                    AnchorKind.Caret,
                    "UI Automation caret");
            }
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }

        return null;
    }

    private static AnchorTarget CreateWindowFallback(
        IntPtr foreground,
        string source = "Screen corner fallback")
    {
        Screen screen = Screen.FromHandle(foreground);
        return new AnchorTarget(
            screen.WorkingArea,
            AnchorKind.Window,
            source);
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

            if (!TryCollapsedRangeBounds(focused, textPattern, range, out bounds))
            {
                return false;
            }

            bounds = NormalizeAutomationCaret(bounds);
            return IsPlausibleCaret(bounds, foreground)
                && IsCaretInsideFocusedElement(bounds, focused);
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
        AutomationElement focused,
        TextPattern textPattern,
        TextPatternRange range,
        out DrawingRectangle bounds)
    {
        TextPatternRange document = textPattern.DocumentRange;
        bool atDocumentStart = range.CompareEndpoints(
            TextPatternRangeEndpoint.Start,
            document,
            TextPatternRangeEndpoint.Start) == 0;
        bool atDocumentEnd = range.CompareEndpoints(
            TextPatternRangeEndpoint.Start,
            document,
            TextPatternRangeEndpoint.End) == 0;

        bool browserCaretWorkaround = NeedsBrowserCaretWorkaround(focused);
        if (!browserCaretWorkaround
            && TryCaretEdgeWithinFocused(
                focused,
                range.GetBoundingRectangles(),
                useRightEdge: false,
                out bounds))
        {
            return true;
        }

        if (browserCaretWorkaround
            && IsSuspiciousCollapsedEnd(
                focused,
                range,
                atDocumentStart,
                atDocumentEnd)
            && TryPrefixRangeEnd(
                focused,
                document,
                range,
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
            && TryCaretEdgeWithinFocused(
                focused,
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
        if (movedBackward < 0
            && TryCaretEdgeWithinFocused(
                focused,
                previousCharacter.GetBoundingRectangles(),
                useRightEdge: true,
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
        if (TryCaretEdgeWithinFocused(
                focused,
                character.GetBoundingRectangles(),
                useRightEdge,
                out bounds))
        {
            return true;
        }

        return TryEmptyEditableAnchor(
            focused,
            document,
            out bounds);
    }
    private static bool NeedsBrowserCaretWorkaround(
        AutomationElement focused)
    {
        try
        {
            string className = focused.Current.ClassName;
            return string.Equals(
                    className,
                    "AddressTextfieldView",
                    StringComparison.Ordinal)
                || string.Equals(
                    className,
                    "OmniboxViewViews",
                    StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSuspiciousCollapsedEnd(
        AutomationElement focused,
        TextPatternRange caret,
        bool atDocumentStart,
        bool atDocumentEnd)
    {
        if (!atDocumentEnd || atDocumentStart)
        {
            return false;
        }

        System.Windows.Rect elementBounds = focused.Current.BoundingRectangle;
        if (elementBounds.IsEmpty
            || elementBounds.Height <= 0
            || elementBounds.Height > 80)
        {
            return false;
        }

        foreach (System.Windows.Rect rectangle in caret.GetBoundingRectangles())
        {
            if (!rectangle.IsEmpty
                && IsUsableNumber(rectangle.Left)
                && Math.Abs(rectangle.Left - elementBounds.Left) <= 8)
            {
                return true;
            }
        }

        return false;
    }
    private static bool TryPrefixRangeEnd(
        AutomationElement focused,
        TextPatternRange document,
        TextPatternRange caret,
        out DrawingRectangle bounds)
    {
        TextPatternRange prefix = document.Clone();
        prefix.MoveEndpointByRange(
            TextPatternRangeEndpoint.End,
            caret,
            TextPatternRangeEndpoint.Start);
        return TryCaretEdgeWithinFocused(
            focused,
            prefix.GetBoundingRectangles(),
            useRightEdge: true,
            out bounds);
    }
    private static bool TryCaretEdgeWithinFocused(
        AutomationElement focused,
        System.Windows.Rect[] rectangles,
        bool useRightEdge,
        out DrawingRectangle bounds)
    {
        bounds = default;
        System.Windows.Rect focusedBounds;
        try
        {
            focusedBounds = focused.Current.BoundingRectangle;
        }
        catch
        {
            focusedBounds = System.Windows.Rect.Empty;
        }

        for (int index = rectangles.Length - 1; index >= 0; index--)
        {
            if (!TryCaretEdge(
                    new[] { rectangles[index] },
                    useRightEdge,
                    out DrawingRectangle candidate))
            {
                continue;
            }

            if (focusedBounds.IsEmpty
                || IsCaretInsideBounds(candidate, focusedBounds))
            {
                bounds = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryEmptyEditableAnchor(
        AutomationElement focused,
        TextPatternRange document,
        out DrawingRectangle bounds)
    {
        bounds = default;
        try
        {
            string text = document.GetText(4);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            System.Windows.Rect element = focused.Current.BoundingRectangle;
            if (element.IsEmpty
                || !IsUsableNumber(element.Left)
                || !IsUsableNumber(element.Top)

                || !IsUsableNumber(element.Width)
                || !IsUsableNumber(element.Height)
                || element.Width < 2
                || element.Height < 4
                || element.Height > 400)
            {
                return false;
            }

            int visualHeight = Math.Clamp(
                (int)Math.Ceiling(element.Height),
                16,
                30);
            int top = (int)Math.Floor(
                element.Top + ((element.Height - visualHeight) / 2));
            bounds = new DrawingRectangle(
                (int)Math.Floor(element.Left),
                top,
                2,
                visualHeight);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsCaretInsideFocusedElement(
        DrawingRectangle caret,
        AutomationElement focused)

    {
        try
        {
            System.Windows.Rect focusedBounds =
                focused.Current.BoundingRectangle;
            return focusedBounds.IsEmpty
                || IsCaretInsideBounds(caret, focusedBounds);
        }
        catch
        {
            return true;
        }
    }

    private static bool IsCaretInsideBounds(
        DrawingRectangle caret,
        System.Windows.Rect element)
    {
        DrawingRectangle allowed = DrawingRectangle.FromLTRB(
            (int)Math.Floor(element.Left),
            (int)Math.Floor(element.Top),
            (int)Math.Ceiling(element.Right),
            (int)Math.Ceiling(element.Bottom));
        allowed.Inflate(10, 10);
        return allowed.IntersectsWith(caret);
    }

    private static DrawingRectangle NormalizeAutomationCaret(
        DrawingRectangle bounds)
    {
        const int maximumVisualCaretHeight = 30;
        if (bounds.Height <= maximumVisualCaretHeight)
        {
            return bounds;
        }

        int visualHeight = maximumVisualCaretHeight;
        int visualTop = bounds.Top
            + ((bounds.Height - visualHeight) / 2);
        return new DrawingRectangle(
            bounds.Left,
            visualTop,
            bounds.Width,
            visualHeight);
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
