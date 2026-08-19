$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$path = Join-Path $repoRoot 'src\ShowLang\OverlayForm.cs'
$text = [IO.File]::ReadAllText($path)
$pattern = '(?s)    private Point CalculateLocation\(AnchorTarget target\)\r?\n    \{.*?\r?\n    \}\r?\n\r?\n    protected override void OnPaint'
$replacement = @'
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

            if (!TryPlaceOutsideWindowsSearch(
                    anchor,
                    screen,
                    ref x,
                    ref y))
            {
                if (y < area.Top)
                {
                    y = anchor.Bottom + 6;
                }

                if (x + Width > area.Right)
                {
                    x = anchor.Left - Width - 8;
                }
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

    private bool TryPlaceOutsideWindowsSearch(
        Rectangle anchor,
        Screen screen,
        ref int x,
        ref int y)
    {
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
        Rectangle workingArea = screen.WorkingArea;

        int aboveY = searchBounds.Top - Height - 6;
        if (aboveY >= workingArea.Top + 4)
        {
            y = aboveY;
            return true;
        }

        int leftX = searchBounds.Left - Width - 8;
        int rightX = searchBounds.Right + 8;
        bool leftFits = leftX >= workingArea.Left + 4;
        bool rightFits = rightX + Width <= workingArea.Right - 4;

        if (leftFits || rightFits)
        {
            int leftDistance = Math.Abs(
                anchor.Left - searchBounds.Left);
            int rightDistance = Math.Abs(
                searchBounds.Right - anchor.Right);
            x = leftFits && (!rightFits || leftDistance <= rightDistance)
                ? leftX
                : rightX;
            y = anchor.Top + ((anchor.Height - Height) / 2);

            int maximumY = Math.Max(
                workingArea.Top + 4,
                workingArea.Bottom - Height - 4);
            y = Math.Clamp(
                y,
                workingArea.Top + 4,
                maximumY);
            return true;
        }

        int belowY = searchBounds.Bottom + 6;
        if (belowY + Height <= workingArea.Bottom - 4)
        {
            y = belowY;
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
                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    protected override void OnPaint
'@
$matches = [regex]::Matches($text, $pattern).Count
if ($matches -ne 1) {
    throw "Expected one CalculateLocation block, found $matches."
}
$newText = [regex]::Replace($text, $pattern, $replacement)
[IO.File]::WriteAllText(
    $path,
    $newText,
    [Text.UTF8Encoding]::new($false))
Write-Output 'Windows Search positioning patch applied.'
