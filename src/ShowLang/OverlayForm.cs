using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ShowLangNative;

internal sealed class OverlayForm : Form
{
    private const int HideAfterMilliseconds = 1150;

    private readonly System.Windows.Forms.Timer _hideTimer;
    private Font _languageFont;
    private int _scalePercent;
    private int _backgroundOpacityPercent;
    private string _language = string.Empty;

    internal OverlayForm(ShowLangSettings settings)
    {
        _scalePercent = Math.Clamp(settings.ScalePercent, 60, 200);
        _backgroundOpacityPercent = Math.Clamp(
            settings.OpacityPercent,
            40,
            100);
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.FromArgb(28, 30, 38);
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;

        _languageFont = CreateLanguageFont(_scalePercent);
        Opacity = 1D;

        _hideTimer = new System.Windows.Forms.Timer
        {
            Interval = HideAfterMilliseconds,
        };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };

        Size = new Size(
            ScaleValue(58),
            ScaleValue(38));
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= NativeMethods.WsExTopMost
                | NativeMethods.WsExTransparent
                | NativeMethods.WsExLayered
                | NativeMethods.WsExToolWindow
                | NativeMethods.WsExNoActivate;
            parameters.ClassStyle |= NativeMethods.CsDropShadow;
            return parameters;
        }
    }

    internal void ShowLanguage(string language, AnchorTarget target)
    {
        _language = language;
        UpdateWindowSize();

        Point location = CalculateLocation(target);
        Bounds = new Rectangle(location, Size);

        if (!Visible)
        {
            Show();
        }

        LayeredWindowRenderer.Render(
            Handle,
            location,
            Size,
            _language,
            _languageFont,
            _scalePercent,
            _backgroundOpacityPercent);

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

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    internal void HideImmediately()
    {
        _hideTimer.Stop();
        Hide();
    }

    internal void ApplyAppearance(
        int scalePercent,
        int opacityPercent)
    {
        scalePercent = Math.Clamp(scalePercent, 60, 200);
        opacityPercent = Math.Clamp(opacityPercent, 40, 100);

        if (_scalePercent != scalePercent)
        {
            _scalePercent = scalePercent;
            Font previousFont = _languageFont;
            _languageFont = CreateLanguageFont(_scalePercent);
            previousFont.Dispose();
        }

        _backgroundOpacityPercent = opacityPercent;
        Opacity = 1D;
        UpdateWindowSize();

        if (Visible)
        {
            LayeredWindowRenderer.Render(
                Handle,
                Location,
                Size,
                _language,
                _languageFont,
                _scalePercent,
                _backgroundOpacityPercent);
        }
    }

    private void UpdateWindowSize()
    {
        Size textSize = TextRenderer.MeasureText(
            _language,
            _languageFont,
            Size.Empty,
            TextFormatFlags.NoPadding);

        Size = new Size(
            Math.Max(
                ScaleValue(58),
                textSize.Width + ScaleValue(24)),
            ScaleValue(38));
    }

    private int ScaleValue(int value)
    {
        return Math.Max(
            1,
            (int)Math.Round(value * _scalePercent / 100D));
    }

    private static Font CreateLanguageFont(int scalePercent)
    {
        return new Font(
            "Segoe UI Variable Display",
            12F * scalePercent / 100F,
            FontStyle.Bold,
            GraphicsUnit.Point);
    }

    private Point CalculateLocation(AnchorTarget target)
    {
        Rectangle anchor = target.Bounds;
        Screen screen = Screen.FromRectangle(anchor);

        if (target.Kind != AnchorKind.Caret)
        {
            Rectangle workingArea = screen.WorkingArea;
            int margin = ScaleValue(16);
            return new Point(
                Math.Max(
                    workingArea.Left + 4,
                    workingArea.Right - Width - margin),
                Math.Max(
                    workingArea.Top + 4,
                    workingArea.Bottom - Height - margin));
        }

        if (TryPlaceOutsideWindowsSearch(
                target,
                out Point searchLocation))
        {
            return searchLocation;
        }

        Rectangle area = screen.Bounds;
        int horizontalGap = ScaleValue(8);
        int verticalGap = ScaleValue(6);
        int x = anchor.Right + horizontalGap;
        int y = anchor.Top - Height - verticalGap;

        if (y < area.Top)
        {
            y = anchor.Bottom + verticalGap;
        }

        if (x + Width > area.Right)
        {
            x = anchor.Left - Width - horizontalGap;
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

    private bool TryPlaceOutsideWindowsSearch(
        AnchorTarget target,
        out Point location)
    {
        location = Point.Empty;
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(
            foreground,
            out uint processId);
        if (processId == 0 || !IsWindowsSearchProcess(processId))
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(
                foreground,
                out NativeMethods.NativeRect window)
            || window.Width <= 0
            || window.Height <= 0)
        {
            return false;
        }

        Rectangle searchBounds = Rectangle.FromLTRB(
            window.Left,
            window.Top,
            window.Right,
            window.Bottom);
        Screen searchScreen = Screen.FromRectangle(searchBounds);
        Rectangle workingArea = searchScreen.WorkingArea;
        Rectangle screenBounds = searchScreen.Bounds;

        Rectangle anchor = target.Bounds;
        Rectangle allowedAnchor = searchBounds;
        allowedAnchor.Inflate(16, 16);
        bool usableCaret = target.Kind == AnchorKind.Caret
            && allowedAnchor.IntersectsWith(anchor);

        int anchorCenterY = usableCaret
            ? anchor.Top + (anchor.Height / 2)
            : searchBounds.Top + Math.Min(56, searchBounds.Height / 3);
        int sideY = anchorCenterY - (Height / 2);
        int minimumY = Math.Max(
            workingArea.Top + 4,
            searchBounds.Top + 8);
        int maximumY = Math.Min(
            workingArea.Bottom - Height - 4,
            searchBounds.Bottom - Height - 8);
        if (maximumY < minimumY)
        {
            minimumY = workingArea.Top + 4;
            maximumY = Math.Max(
                minimumY,
                workingArea.Bottom - Height - 4);
        }

        sideY = Math.Clamp(sideY, minimumY, maximumY);
        int leftX = searchBounds.Left - Width - 8;
        int rightX = searchBounds.Right + 8;
        bool leftFits = leftX >= workingArea.Left + 4;
        bool rightFits = rightX + Width <= workingArea.Right - 4;

        if (leftFits || rightFits)
        {
            bool useLeft = leftFits;
            if (leftFits && rightFits && usableCaret)
            {
                int leftDistance = Math.Abs(
                    anchor.Left - searchBounds.Left);
                int rightDistance = Math.Abs(
                    searchBounds.Right - anchor.Right);
                useLeft = leftDistance <= rightDistance;
            }
            else if (!leftFits)
            {
                useLeft = false;
            }

            location = new Point(
                useLeft ? leftX : rightX,
                sideY);
            return true;
        }

        int preferredX = usableCaret
            ? anchor.Right + 8
            : searchBounds.Left + ((searchBounds.Width - Width) / 2);
        int minimumX = screenBounds.Left + 4;
        int maximumX = Math.Max(
            minimumX,
            screenBounds.Right - Width - 4);
        preferredX = Math.Clamp(preferredX, minimumX, maximumX);

        int aboveY = searchBounds.Top - Height - 6;
        if (aboveY >= screenBounds.Top + 4)
        {
            location = new Point(preferredX, aboveY);
            return true;
        }

        int belowY = searchBounds.Bottom + 6;
        if (belowY + Height <= screenBounds.Bottom - 4)
        {
            location = new Point(preferredX, belowY);
            return true;
        }

        return false;
    }

    private static bool IsWindowsSearchProcess(uint processId)
    {
        try
        {
            using System.Diagnostics.Process process =
                System.Diagnostics.Process.GetProcessById((int)processId);
            string name = process.ProcessName;
            return string.Equals(
                    name,
                    "SearchHost",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    name,
                    "SearchApp",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    name,
                    "StartMenuExperienceHost",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    name,
                    "ShellExperienceHost",
                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        Rectangle rectangle = new(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = CreateRoundedPath(
            rectangle,
            ScaleValue(10));
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
            ScaleValue(10));
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
