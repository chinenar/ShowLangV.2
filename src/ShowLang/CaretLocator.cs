using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using DrawingRectangle = System.Drawing.Rectangle;

namespace ShowLangNative;

internal enum AnchorKind
{
    Caret,
    Element,
    Window,
}

internal readonly record struct AnchorTarget(
    DrawingRectangle Bounds,
    AnchorKind Kind,
    string Source);

internal static class CaretLocator
{
    internal static AnchorTarget Locate(IntPtr foreground)
    {
        if (TryAutomationCaret(out DrawingRectangle caret))
        {
            return new AnchorTarget(caret, AnchorKind.Caret, "UI Automation");
        }

        if (TryWin32Caret(foreground, out caret))
        {
            return new AnchorTarget(caret, AnchorKind.Caret, "Win32 caret");
        }

        if (TryFocusedElement(out DrawingRectangle element))
        {
            return new AnchorTarget(element, AnchorKind.Element, "Focused element");
        }

        if (NativeMethods.GetWindowRect(foreground, out NativeMethods.NativeRect window)
            && window.Width > 0
            && window.Height > 0)
        {
            DrawingRectangle bounds = DrawingRectangle.FromLTRB(
                window.Left,
                window.Top,
                window.Right,
                window.Bottom);
            return new AnchorTarget(bounds, AnchorKind.Window, "Foreground window");
        }

        Screen screen = Screen.FromHandle(foreground);
        return new AnchorTarget(screen.Bounds, AnchorKind.Window, "Active screen");
    }

    private static bool TryAutomationCaret(out DrawingRectangle bounds)
    {
        bounds = default;
        try
        {
            AutomationElement? focused = AutomationElement.FocusedElement;
            if (focused is null)
            {
                return false;
            }


            if (focused.TryGetCurrentPattern(
                    TextPattern.Pattern,
                    out object? patternObject)
                && patternObject is TextPattern textPattern)
            {
                TextPatternRange[] ranges = textPattern.GetSelection();
                if (ranges.Length > 0
                    && TryRangeBounds(ranges[^1], out bounds))
                {
                    return true;
                }
            }
        }
        catch (Exception exception) when (
            exception is ElementNotAvailableException
            or InvalidOperationException
            or COMException)
        {
            AppLog.Write(exception);
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }

        return false;
    }

    private static bool TryRangeBounds(
        TextPatternRange range,
        out DrawingRectangle bounds)
    {
        if (TryLastRectangle(range.GetBoundingRectangles(), out bounds))
        {
            return true;
        }

        TextPatternRange expanded = range.Clone();
        int moved = expanded.MoveEndpointByUnit(
            TextPatternRangeEndpoint.End,
            TextUnit.Character,
            1);

        if (moved == 0)
        {
            expanded.MoveEndpointByUnit(
                TextPatternRangeEndpoint.Start,
                TextUnit.Character,
                -1);
        }

        return TryLastRectangle(expanded.GetBoundingRectangles(), out bounds);
    }

    private static bool TryLastRectangle(
        System.Windows.Rect[] rectangles,
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
                || rectangle.Height < 0)
            {
                continue;
            }

            int left = (int)Math.Floor(rectangle.X);
            int top = (int)Math.Floor(rectangle.Y);
            int width = Math.Max(2, (int)Math.Ceiling(rectangle.Width));
            int height = Math.Max(18, (int)Math.Ceiling(rectangle.Height));
            bounds = new DrawingRectangle(left, top, width, height);
            return true;
        }

        return false;
    }
    private static bool IsUsableNumber(double value)
    {
        return !double.IsNaN(value)
            && !double.IsInfinity(value)
            && value >= -100000
            && value <= 100000;
    }

    private static bool TryWin32Caret(
        IntPtr foreground,
        out DrawingRectangle bounds)
    {
        bounds = default;
        uint threadId = NativeMethods.GetWindowThreadProcessId(
            foreground,
            out _);

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

        int width = Math.Max(2, bottomRight.X - topLeft.X);
        int height = Math.Max(18, bottomRight.Y - topLeft.Y);
        bounds = new DrawingRectangle(
            topLeft.X,
            topLeft.Y,
            width,
            height);
        return true;
    }

    private static bool TryFocusedElement(out DrawingRectangle bounds)
    {
        bounds = default;
        try
        {
            AutomationElement? focused = AutomationElement.FocusedElement;
            if (focused is null)
            {
                return false;
            }

            System.Windows.Rect rectangle = focused.Current.BoundingRectangle;
            if (rectangle.IsEmpty
                || !IsUsableNumber(rectangle.X)
                || !IsUsableNumber(rectangle.Y)
                || !IsUsableNumber(rectangle.Width)
                || !IsUsableNumber(rectangle.Height)
                || rectangle.Width < 2
                || rectangle.Height < 2)
            {
                return false;
            }

            bounds = new DrawingRectangle(
                (int)Math.Floor(rectangle.X),
                (int)Math.Floor(rectangle.Y),
                Math.Max(2, (int)Math.Ceiling(rectangle.Width)),
                Math.Max(2, (int)Math.Ceiling(rectangle.Height)));
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
            return false;
        }
    }
}
