using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace ShowLangNative;

internal static class LayeredWindowRenderer
{
    internal static void Render(
        IntPtr handle,
        Point location,
        Size size,
        string text,
        Font font,
        int scalePercent,
        int backgroundOpacityPercent)
    {
        if (handle == IntPtr.Zero
            || size.Width <= 0
            || size.Height <= 0)
        {
            return;
        }

        using Bitmap bitmap = new(
            size.Width,
            size.Height,
            PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            Rectangle rectangle = new(
                0,
                0,
                size.Width - 1,
                size.Height - 1);
            int radius = Math.Max(
                1,
                (int)Math.Round(10 * scalePercent / 100D));
            using GraphicsPath path = CreateRoundedPath(
                rectangle,
                radius);

            int backgroundAlpha = Math.Clamp(
                (int)Math.Round(255 * backgroundOpacityPercent / 100D),
                0,
                255);
            int borderAlpha = Math.Clamp(
                (int)Math.Round(110 * backgroundOpacityPercent / 100D),
                30,
                110);

            using SolidBrush background = new(
                Color.FromArgb(backgroundAlpha, 28, 30, 38));
            using Pen border = new(
                Color.FromArgb(borderAlpha, 255, 255, 255),
                Math.Max(1F, scalePercent / 100F));
            graphics.FillPath(background, path);
            graphics.DrawPath(border, path);

            using SolidBrush textBrush = new(Color.White);
            using StringFormat format = new()
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.None,
                FormatFlags = StringFormatFlags.NoWrap,
            };
            graphics.DrawString(
                text,
                font,
                textBrush,
                new RectangleF(0, 0, size.Width, size.Height),
                format);
            graphics.Flush();
        }

        PushBitmap(handle, bitmap, location);
    }

    private static void PushBitmap(
        IntPtr handle,
        Bitmap bitmap,
        Point location)
    {
        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            throw new Win32Exception(
                System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        }

        IntPtr memoryDc = IntPtr.Zero;
        IntPtr bitmapHandle = IntPtr.Zero;
        IntPtr previousObject = IntPtr.Zero;

        try
        {
            memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero)
            {
                throw new Win32Exception(
                    System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            }
            bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
            previousObject = NativeMethods.SelectObject(
                memoryDc,
                bitmapHandle);

            NativeMethods.NativePoint destinationPoint = new(
                location.X,
                location.Y);
            NativeMethods.NativePoint sourcePoint = new(0, 0);
            NativeMethods.NativeSize size = new(
                bitmap.Width,
                bitmap.Height);
            NativeMethods.BlendFunction blend = new()
            {
                BlendOp = NativeMethods.AcSrcOver,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = NativeMethods.AcSrcAlpha,
            };

            if (!NativeMethods.UpdateLayeredWindow(
                    handle,
                    screenDc,
                    ref destinationPoint,
                    ref size,
                    memoryDc,
                    ref sourcePoint,
                    0,
                    ref blend,
                    NativeMethods.UlwAlpha))
            {
                throw new Win32Exception(
                    System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            if (previousObject != IntPtr.Zero)
            {
                NativeMethods.SelectObject(memoryDc, previousObject);
            }

            if (bitmapHandle != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(bitmapHandle);
            }

            if (memoryDc != IntPtr.Zero)
            {
                NativeMethods.DeleteDC(memoryDc);
            }

            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static GraphicsPath CreateRoundedPath(
        Rectangle rectangle,
        int radius)
    {
        int maximumRadius = Math.Max(
            1,
            Math.Min(rectangle.Width, rectangle.Height) / 2);
        radius = Math.Clamp(radius, 1, maximumRadius);
        int diameter = radius * 2;

        GraphicsPath path = new();
        Rectangle arc = new(
            rectangle.Location,
            new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = rectangle.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = rectangle.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = rectangle.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}
