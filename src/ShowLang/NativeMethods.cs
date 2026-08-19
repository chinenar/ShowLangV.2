using System.Runtime.InteropServices;

namespace ShowLangNative;

internal static class NativeMethods
{
    internal static readonly IntPtr HwndTopMost = new(-1);

    internal const int SwShowNoActivate = 4;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpShowWindow = 0x0040;

    internal const int WsExTransparent = 0x00000020;
    internal const int WsExToolWindow = 0x00000080;
    internal const int WsExLayered = 0x00080000;
    internal const int WsExNoActivate = 0x08000000;
    internal const int CsDropShadow = 0x00020000;

    internal const int DwmwaWindowCornerPreference = 33;
    internal const int DwmwcpRound = 2;

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

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

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(
        IntPtr hWnd,
        int attribute,
        ref int value,
        int valueSize);

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
