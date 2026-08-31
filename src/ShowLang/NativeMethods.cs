using System.Runtime.InteropServices;

namespace ShowLangNative;

internal static class NativeMethods
{
    internal static readonly IntPtr HwndTopMost = new(-1);

    internal const int SwShowNoActivate = 4;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpShowWindow = 0x0040;
    internal const uint SwpNoOwnerZOrder = 0x0200;

    internal const int WsExTopMost = 0x00000008;
    internal const int WsExTransparent = 0x00000020;
    internal const int WsExLayered = 0x00080000;
    internal const int WsExToolWindow = 0x00000080;
    internal const int WsExNoActivate = 0x08000000;
    internal const int CsDropShadow = 0x00020000;

    internal const int DwmwaWindowCornerPreference = 33;
    internal const int DwmwcpRound = 2;
    internal const uint ObjidCaret = 0xFFFFFFF8;
    internal const int ObjidCaretSigned = -8;

    internal const uint EventSystemForeground = 0x0003;
    internal const uint EventObjectFocus = 0x8005;
    internal const uint EventObjectLocationChange = 0x800B;
    internal const uint EventObjectTextSelectionChanged = 0x8014;
    internal const uint WineventOutOfContext = 0x0000;
    internal const uint WineventSkipOwnProcess = 0x0002;

    internal const uint UlwAlpha = 0x00000002;
    internal const byte AcSrcOver = 0x00;
    internal const byte AcSrcAlpha = 0x01;

    internal static uint GetInputThreadId(IntPtr foreground)
    {
        uint threadId = GetWindowThreadProcessId(foreground, out _);
        if (threadId == 0)
        {
            return 0;
        }

        GuiThreadInfo info = new()
        {
            Size = (uint)Marshal.SizeOf<GuiThreadInfo>(),
        };
        if (GetGUIThreadInfo(threadId, ref info)
            && info.FocusWindow != IntPtr.Zero)
        {
            uint focusThreadId = GetWindowThreadProcessId(
                info.FocusWindow,
                out _);
            if (focusThreadId != 0)
            {
                return focusThreadId;
            }
        }

        return threadId;
    }

    internal static bool TryGetLastInput(
        out uint inputTick,
        out uint idleMilliseconds)
    {
        LastInputInfo info = new()
        {
            Size = (uint)Marshal.SizeOf<LastInputInfo>(),
        };
        if (!GetLastInputInfo(ref info))
        {
            inputTick = 0;
            idleMilliseconds = 0;
            return false;
        }

        inputTick = info.Time;
        idleMilliseconds = unchecked(
            (uint)Environment.TickCount - info.Time);
        return true;
    }
    internal delegate void WinEventDelegate(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(
        ref LastInputInfo info);

    [DllImport("user32.dll")]
    internal static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr eventHookModule,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(
        IntPtr hWnd,
        out uint processId);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetGUIThreadInfo(
        uint idThread,
        ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClientToScreen(
        IntPtr hWnd,
        ref NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(
        IntPtr hWnd,
        out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr hWnd);

    [DllImport("oleacc.dll")]
    internal static extern int AccessibleObjectFromWindow(
        IntPtr hWnd,
        uint objectId,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out object accessibleObject);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(
        IntPtr hWnd,
        int command);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int ReleaseDC(
        IntPtr hWnd,
        IntPtr hDc);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern IntPtr CreateCompatibleDC(IntPtr hDc);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteDC(IntPtr hDc);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern IntPtr SelectObject(
        IntPtr hDc,
        IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateLayeredWindow(
        IntPtr hWnd,
        IntPtr destinationDc,
        ref NativePoint destinationPoint,
        ref NativeSize size,
        IntPtr sourceDc,
        ref NativePoint sourcePoint,
        uint colorKey,
        ref BlendFunction blend,
        uint flags);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(
        IntPtr hWnd,
        int attribute,
        ref int value,
        int valueSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        internal uint Size;
        internal uint Time;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        internal int X;
        internal int Y;

        internal NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeSize
    {
        internal int Width;
        internal int Height;

        internal NativeSize(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct BlendFunction
    {
        internal byte BlendOp;
        internal byte BlendFlags;
        internal byte SourceConstantAlpha;
        internal byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal int Width => Right - Left;
        internal int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GuiThreadInfo
    {
        internal uint Size;
        internal uint Flags;
        internal IntPtr ActiveWindow;
        internal IntPtr FocusWindow;
        internal IntPtr CaptureWindow;
        internal IntPtr MenuOwnerWindow;
        internal IntPtr MoveSizeWindow;
        internal IntPtr CaretWindow;
        internal NativeRect CaretRect;
    }
}
