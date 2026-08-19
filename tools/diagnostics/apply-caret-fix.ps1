$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$sourceRoot = Join-Path $repoRoot 'src\ShowLang'
$caretPath = Join-Path $sourceRoot 'CaretLocator.cs'
$nativePath = Join-Path $sourceRoot 'NativeMethods.cs'
$caretText = [IO.File]::ReadAllText($caretPath)
$nativeText = [IO.File]::ReadAllText($nativePath)

$oldFallback = @'
    private static AnchorTarget CreateWindowFallback(IntPtr foreground)
    {
        if (NativeMethods.GetWindowRect(
                foreground,
                out NativeMethods.NativeRect window)
            && window.Width > 0
            && window.Height > 0)
        {
            DrawingRectangle bounds = DrawingRectangle.FromLTRB(
                window.Left,
                window.Top,
                window.Right,
                window.Bottom);
            return new AnchorTarget(
                bounds,
                AnchorKind.Window,
                "Window fallback");
        }

        Screen screen = Screen.FromHandle(foreground);
        return new AnchorTarget(
            screen.Bounds,
            AnchorKind.Window,
            "Screen fallback");
    }
'@

$newFallback = @'
    private static AnchorTarget CreateWindowFallback(IntPtr foreground)
    {
        if (!NativeMethods.IsIconic(foreground)
            && NativeMethods.GetWindowRect(
                foreground,
                out NativeMethods.NativeRect window)
            && window.Width > 0
            && window.Height > 0
            && window.Left > -30000
            && window.Top > -30000)
        {
            DrawingRectangle bounds = DrawingRectangle.FromLTRB(
                window.Left,
                window.Top,
                window.Right,
                window.Bottom);
            bool onScreen = Screen.AllScreens.Any(
                screen => screen.Bounds.IntersectsWith(bounds));
            if (onScreen)
            {
                return new AnchorTarget(
                    bounds,
                    AnchorKind.Window,
                    "Window fallback");
            }
        }

        Screen screen = Screen.FromHandle(foreground);
        return new AnchorTarget(
            screen.WorkingArea,
            AnchorKind.Window,
            "Screen fallback");
    }
'@

$oldCollapsed = @'
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
'@

$newCollapsed = @'
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
'@

$oldIsIconicAnchor = @'
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(
        IntPtr hWnd,
        out NativeRect rect);
'@

$newIsIconicAnchor = @'
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(
        IntPtr hWnd,
        out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr hWnd);
'@

if (-not $caretText.Contains($oldFallback)) {
    throw 'Fallback block not found.'
}
if (-not $caretText.Contains($oldCollapsed)) {
    throw 'Collapsed-range block not found.'
}
if (-not $nativeText.Contains($oldIsIconicAnchor)) {
    throw 'GetWindowRect anchor not found.'
}

$caretText = $caretText.Replace($oldFallback, $newFallback)
$caretText = $caretText.Replace($oldCollapsed, $newCollapsed)
$nativeText = $nativeText.Replace(
    $oldIsIconicAnchor,
    $newIsIconicAnchor)

[IO.File]::WriteAllText(
    $caretPath,
    $caretText,
    [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText(
    $nativePath,
    $nativeText,
    [Text.UTF8Encoding]::new($false))

'Caret and fallback fixes applied.'
