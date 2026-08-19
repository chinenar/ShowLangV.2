using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ShowLangNative;

internal sealed class OverlayForm : Form
{
    private const int HideAfterMilliseconds = 1150;

    private readonly System.Windows.Forms.Timer _hideTimer;
    private readonly Font _languageFont;
    private string _language = string.Empty;

    internal OverlayForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.FromArgb(28, 30, 38);
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;

        _languageFont = new Font(
            "Segoe UI Variable Display",
            12F,
            FontStyle.Bold,
            GraphicsUnit.Point);

        _hideTimer = new System.Windows.Forms.Timer
        {
            Interval = HideAfterMilliseconds,
        };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };

        Size = new Size(58, 38);
        UpdateWindowRegion();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= NativeMethods.WsExTopMost
                | NativeMethods.WsExTransparent
                | NativeMethods.WsExToolWindow
                | NativeMethods.WsExNoActivate;
            parameters.ClassStyle |= NativeMethods.CsDropShadow;
            return parameters;
        }
    }

    internal void ShowLanguage(string language, AnchorTarget target)
    {
        _language = language;
        Size textSize = TextRenderer.MeasureText(
            language,
            _languageFont,
            Size.Empty,
            TextFormatFlags.NoPadding);

        Size = new Size(
            Math.Max(58, textSize.Width + 24),
            38);
        UpdateWindowRegion();

        Point location = CalculateLocation(target);
        Bounds = new Rectangle(location, Size);
        Invalidate();

        if (!Visible)
        {
            Show();
        }

        NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HwndTopMost,
            location.X,
            location.Y,
            Width,
            Height,
            NativeMethods.SwpNoActivate
                | NativeMethods.SwpShowWindow
                | NativeMethods.SwpNoOwnerZOrder);
        NativeMethods.ShowWindow(Handle, NativeMethods.SwShowNoActivate);
        Update();

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private Point CalculateLocation(AnchorTarget target)
    {
        Rectangle anchor = target.Bounds;
        Screen screen = Screen.FromRectangle(anchor);
        Rectangle area = screen.Bounds;
        int x;
        int y;

        if (target.Kind == AnchorKind.Caret)
        {
            x = anchor.Right + 8;
            y = anchor.Top - Height - 6;
            if (y < area.Top)
            {
                y = anchor.Bottom + 6;
            }

            if (x + Width > area.Right)
            {
                x = anchor.Left - Width - 8;
            }
        }
        else
        {
            x = anchor.Left + ((anchor.Width - Width) / 2);
            y = anchor.Top + 38;
        }

        int maximumX = Math.Max(
            area.Left + 4,
            area.Right - Width - 4);
        int maximumY = Math.Max(
            area.Top + 4,
            area.Bottom - Height - 4);
        x = Math.Clamp(x, area.Left + 4, maximumX);
        y = Math.Clamp(y, area.Top + 4, maximumY);
        return new Point(x, y);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        Rectangle rectangle = new(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = CreateRoundedPath(rectangle, 10);
        using SolidBrush background = new(BackColor);
        using Pen border = new(
            Color.FromArgb(110, 255, 255, 255),
            1F);

        e.Graphics.FillPath(background, path);
        e.Graphics.DrawPath(border, path);
        TextRenderer.DrawText(
            e.Graphics,
            _language,
            _languageFont,
            ClientRectangle,
            Color.White,
            TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPadding
                | TextFormatFlags.NoPrefix);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int preference = NativeMethods.DwmwcpRound;
        NativeMethods.DwmSetWindowAttribute(
            Handle,
            NativeMethods.DwmwaWindowCornerPreference,
            ref preference,
            sizeof(int));
    }

    private void UpdateWindowRegion()
    {
        using GraphicsPath path = CreateRoundedPath(
            new Rectangle(0, 0, Width, Height),
            10);
        Region? previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }

    private static GraphicsPath CreateRoundedPath(
        Rectangle rectangle,
        int radius)
    {
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

    protected override void WndProc(ref Message message)
    {
        const int WmNcHitTest = 0x0084;
        const int HtTransparent = -1;
        const int WmMouseActivate = 0x0021;
        const int MaNoActivate = 3;

        if (message.Msg == WmNcHitTest)
        {
            message.Result = new IntPtr(HtTransparent);
            return;
        }

        if (message.Msg == WmMouseActivate)
        {
            message.Result = new IntPtr(MaNoActivate);
            return;
        }

        base.WndProc(ref message);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hideTimer.Dispose();
            _languageFont.Dispose();
        }

        base.Dispose(disposing);
    }
}
