$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$sourceRoot = Join-Path $repoRoot 'src\ShowLang'
$caretPath = Join-Path $sourceRoot 'CaretLocator.cs'
$nativePath = Join-Path $sourceRoot 'NativeMethods.cs'
$caretText = [IO.File]::ReadAllText($caretPath)
$nativeText = [IO.File]::ReadAllText($nativePath)

$fallbackLines = @(
'    private static AnchorTarget CreateWindowFallback(IntPtr foreground)',
'    {',
'        if (!NativeMethods.IsIconic(foreground)',
'            && NativeMethods.GetWindowRect(',
'                foreground,',
'                out NativeMethods.NativeRect window)',
'            && window.Width > 0',
'            && window.Height > 0',
'            && window.Left > -30000',
'            && window.Top > -30000)',
'        {',
'            DrawingRectangle bounds = DrawingRectangle.FromLTRB(',
'                window.Left,',
'                window.Top,',
'                window.Right,',
'                window.Bottom);'
)
$fallbackLines += @(
'            bool onScreen = Screen.AllScreens.Any(',
'                screen => screen.Bounds.IntersectsWith(bounds));',
'            if (onScreen)',
'            {',
'                return new AnchorTarget(',
'                    bounds,',
'                    AnchorKind.Window,',
'                    "Window fallback");',
'            }',
'        }',
'',
'        Screen screen = Screen.FromHandle(foreground);',
'        return new AnchorTarget(',
'            screen.WorkingArea,',
'            AnchorKind.Window,',
'            "Screen fallback");',
'    }'
)
$newFallback = $fallbackLines -join "`n"
$fallbackPattern = '(?s)    private static AnchorTarget CreateWindowFallback\(IntPtr foreground\).*?(?=\r?\n    private static bool TryWin32Caret)'
$patchedCaret = [regex]::Replace(
    $caretText,
    $fallbackPattern,
    $newFallback,
    1)
if ($patchedCaret -eq $caretText) {
    throw 'Fallback regex replacement failed.'
}

$collapsedLines = @(
'    private static bool TryCollapsedRangeBounds(',
'        TextPatternRange range,',
'        out DrawingRectangle bounds)',
'    {',
'        if (TryCaretEdge(',
'                range.GetBoundingRectangles(),',
'                useRightEdge: false,',
'                out bounds))',
'        {',
'            return true;',
'        }',
'',
'        TextPatternRange character = range.Clone();',
'        character.ExpandToEnclosingUnit(TextUnit.Character);',
'        int fromStart = range.CompareEndpoints(',
'            TextPatternRangeEndpoint.Start,',
'            character,',
'            TextPatternRangeEndpoint.Start);',
'        int fromEnd = range.CompareEndpoints(',
'            TextPatternRangeEndpoint.Start,',
'            character,',
'            TextPatternRangeEndpoint.End);',
'        bool useRightEdge = fromEnd >= 0 || fromStart > 0;'
)
$collapsedLines += @(
'        if (TryCaretEdge(',
'                character.GetBoundingRectangles(),',
'                useRightEdge,',
'                out bounds))',
'        {',
'            return true;',
'        }',
'',
'        TextPatternRange nextCharacter = range.Clone();',
'        int movedForward = nextCharacter.MoveEndpointByUnit(',
'            TextPatternRangeEndpoint.End,',
'            TextUnit.Character,',
'            1);',
'        if (movedForward > 0',
'            && TryCaretEdge(',
'                nextCharacter.GetBoundingRectangles(),',
'                useRightEdge: false,',
'                out bounds))',
'        {',
'            return true;',
'        }',
'',
'        TextPatternRange previousCharacter = range.Clone();',
'        int movedBackward = previousCharacter.MoveEndpointByUnit(',
'            TextPatternRangeEndpoint.Start,',
'            TextUnit.Character,',
'            -1);'
)
$collapsedLines += @(
'        return movedBackward < 0',
'            && TryCaretEdge(',
'                previousCharacter.GetBoundingRectangles(),',
'                useRightEdge: true,',
'                out bounds);',
'    }'
)
$newCollapsed = $collapsedLines -join "`n"
$collapsedPattern = '(?s)    private static bool TryCollapsedRangeBounds\(.*?(?=\r?\n    private static bool TryCaretEdge)'
$patchedCollapsed = [regex]::Replace(
    $patchedCaret,
    $collapsedPattern,
    $newCollapsed,
    1)
if ($patchedCollapsed -eq $patchedCaret) {
    throw 'Collapsed-range regex replacement failed.'
}

$isIconicPattern = '(?s)(    \[DllImport\("user32\.dll"\)\]\r?\n    \[return: MarshalAs\(UnmanagedType\.Bool\)\]\r?\n    internal static extern bool GetWindowRect\(\r?\n        IntPtr hWnd,\r?\n        out NativeRect rect\);)'
$isIconicReplacement = '$1' + "`n`n" + @'
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr hWnd);
'@

if ($nativeText.Contains('internal static extern bool IsIconic')) {
    $patchedNative = $nativeText
}
else {
    $patchedNative = [regex]::Replace(
        $nativeText,
        $isIconicPattern,
        $isIconicReplacement,
        1)
    if ($patchedNative -eq $nativeText) {
        throw 'IsIconic regex replacement failed.'
    }
}

[IO.File]::WriteAllText(
    $caretPath,
    $patchedCollapsed,
    [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText(
    $nativePath,
    $patchedNative,
    [Text.UTF8Encoding]::new($false))

'Caret, Terminal, and minimized-window fixes applied.'
