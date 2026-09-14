using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace ShowLangNative;

internal enum PhotoshopTextState
{
    Unknown,
    Inactive,
    TextTool,
    Editing,
}

internal static class PhotoshopTextDetector
{
    private const int RpcServerCallRetryLater =
        unchecked((int)0x8001010A);
    private const int AnchorHeight = 24;

    internal static bool IsPhotoshopWindow(IntPtr foreground)
    {
        return TryGetPhotoshopRoot(foreground, out _);
    }

    internal static IntPtr NormalizeForeground(IntPtr foreground)
    {
        return TryGetPhotoshopRoot(foreground, out IntPtr root)
            ? root
            : foreground;
    }

    internal static bool TryCreateDocumentAnchor(
        IntPtr foreground,
        NativeMethods.NativePoint point,
        out AnchorTarget target)
    {
        target = default;
        if (!TryGetPhotoshopRoot(foreground, out IntPtr root))
        {
            return false;
        }

        foreground = root;
        NativeMethods.GetWindowThreadProcessId(
            foreground,
            out uint photoshopProcessId);
        if (photoshopProcessId == 0)
        {
            return false;
        }

        IntPtr window = NativeMethods.WindowFromPoint(point);
        bool sawView = false;
        bool sawDocument = false;
        for (int depth = 0;
             depth < 12 && window != IntPtr.Zero;
             depth++)
        {
            NativeMethods.GetWindowThreadProcessId(
                window,
                out uint processId);
            if (processId != photoshopProcessId)
            {
                return false;
            }

            string className = GetWindowClass(window);
            sawView |= string.Equals(
                className,
                "PSViewC",
                StringComparison.Ordinal);
            sawDocument |= string.Equals(
                className,
                "OWL.Document",
                StringComparison.Ordinal);

            if (window == foreground)
            {
                break;
            }

            window = NativeMethods.GetParent(window);
        }

        if (!sawView || !sawDocument)
        {
            return false;
        }

        Screen screen = Screen.FromPoint(
            new Point(point.X, point.Y));
        if (!screen.Bounds.Contains(point.X, point.Y))
        {
            return false;
        }

        target = new AnchorTarget(
            new Rectangle(
                point.X,
                point.Y - (AnchorHeight / 2),
                2,
                AnchorHeight),
            AnchorKind.Caret,
            "Photoshop text click");
        return true;
    }

    internal static PhotoshopTextState QueryState(
        IntPtr foreground)
    {
        if (!TryGetPhotoshopRoot(foreground, out IntPtr root))
        {
            return PhotoshopTextState.Inactive;
        }

        foreground = root;
        bool canvasFocused = IsCanvasFocused(foreground);
        object? application = null;
        try
        {
            Type? applicationType = Type.GetTypeFromProgID(
                "Photoshop.Application",
                throwOnError: false);
            if (applicationType is null)
            {
                return PhotoshopTextState.Unknown;
            }

            application = Activator.CreateInstance(applicationType);
            if (application is null)
            {
                return PhotoshopTextState.Unknown;
            }

            object? rawTool = applicationType.InvokeMember(
                "CurrentTool",
                BindingFlags.GetProperty,
                binder: null,
                target: application,
                args: null);
            string tool = Convert.ToString(rawTool) ?? string.Empty;

            if (IsTextTool(tool))
            {
                return PhotoshopTextState.TextTool;
            }

            if (string.IsNullOrWhiteSpace(tool) && canvasFocused)
            {
                return PhotoshopTextState.Editing;
            }

            return PhotoshopTextState.Inactive;
        }
        catch (TargetInvocationException exception)
            when (IsPhotoshopBusy(exception.InnerException)
                && canvasFocused)
        {
            return PhotoshopTextState.Editing;
        }
        catch (COMException exception)
            when (exception.HResult == RpcServerCallRetryLater
                && canvasFocused)
        {
            return PhotoshopTextState.Editing;
        }
        catch
        {
            return PhotoshopTextState.Unknown;
        }
        finally
        {
            if (application is not null
                && Marshal.IsComObject(application))
            {
                try
                {
                    Marshal.FinalReleaseComObject(application);
                }
                catch
                {
                }
            }
        }
    }

    private static bool IsCanvasFocused(IntPtr foreground)
    {
        uint threadId = NativeMethods.GetWindowThreadProcessId(
            foreground,
            out _);
        if (threadId == 0)
        {
            return false;
        }

        NativeMethods.GuiThreadInfo info = new()
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.GuiThreadInfo>(),
        };
        if (!NativeMethods.GetGUIThreadInfo(threadId, ref info)
            || info.FocusWindow == IntPtr.Zero)
        {
            return false;
        }

        return string.Equals(
            GetWindowClass(info.FocusWindow),
            "PSViewC",
            StringComparison.Ordinal);
    }

    private static bool IsTextTool(string tool)
    {
        return string.Equals(
                tool,
                "typeCreateOrEditTool",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                tool,
                "typeVerticalCreateOrEditTool",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPhotoshopBusy(Exception? exception)
    {
        while (exception is not null)
        {
            if (exception is COMException comException
                && comException.HResult == RpcServerCallRetryLater)
            {
                return true;
            }

            exception = exception.InnerException;
        }

        return false;
    }

    private static bool TryGetPhotoshopRoot(
        IntPtr window,
        out IntPtr root)
    {
        root = IntPtr.Zero;
        if (window == IntPtr.Zero)
        {
            return false;
        }

        IntPtr current = window;
        for (int depth = 0;
             depth < 8 && current != IntPtr.Zero;
             depth++)
        {
            if (string.Equals(
                    GetWindowClass(current),
                    "Photoshop",
                    StringComparison.Ordinal))
            {
                root = current;
                return true;
            }

            current = NativeMethods.GetWindow(
                current,
                NativeMethods.GwOwner);
        }

        current = window;
        for (int depth = 0;
             depth < 12 && current != IntPtr.Zero;
             depth++)
        {
            if (string.Equals(
                    GetWindowClass(current),
                    "Photoshop",
                    StringComparison.Ordinal))
            {
                root = current;
                return true;
            }

            current = NativeMethods.GetParent(current);
        }

        return false;
    }

    private static string GetWindowClass(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return string.Empty;
        }

        StringBuilder className = new(128);
        NativeMethods.GetClassName(
            window,
            className,
            className.Capacity);
        return className.ToString();
    }
}
