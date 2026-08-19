Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$element = [System.Windows.Automation.AutomationElement]::FocusedElement
$patternObject = $null
$result = [ordered]@{
    Name = $element.Current.Name
    Class = $element.Current.ClassName
}

try {
    $found = $element.TryGetCurrentPattern(
        [System.Windows.Automation.TextPattern]::Pattern,
        [ref]$patternObject)
    if (-not $found) { throw 'TextPattern unavailable' }

    $pattern = [System.Windows.Automation.TextPattern]$patternObject
    $cursor = $pattern.GetSelection()[-1]
    $cursor.ScrollIntoView($false)
    Start-Sleep -Milliseconds 250

    $visible = $pattern.GetVisibleRanges()[0]
    $text = $visible.GetText(-1)
    $rectangles = $visible.GetBoundingRectangles()

    $result.TextLength = $text.Length
    $result.CRCount = ([regex]::Matches($text, "`r")).Count
    $result.LFCount = ([regex]::Matches($text, "`n")).Count
    $result.LiteralSlashR = ([regex]::Matches($text, '\\r')).Count
    $result.LiteralSlashN = ([regex]::Matches($text, '\\n')).Count

    $lines = [regex]::Split($text, "`r?`n")
    $result.LineCount = $lines.Length
    $result.LineLengths = @($lines | Select-Object -First 8 |
        ForEach-Object { $_.Length })
    $result.MaxLineLength = ($lines | Measure-Object Length -Maximum).Maximum
    $result.MinLineLength = ($lines | Measure-Object Length -Minimum).Minimum
    $result.RectangleCount = $rectangles.Length
    $result.FirstRectangles = @(
        $rectangles | Select-Object -First 4 |
            ForEach-Object {
                "$($_.X),$($_.Y),$($_.Width),$($_.Height)"
            })

    $start = [System.Windows.Automation.Text.TextPatternRangeEndpoint]::Start
    $end = [System.Windows.Automation.Text.TextPatternRangeEndpoint]::End
    $result.CursorVsVisibleStart = $cursor.CompareEndpoints(
        $start, $visible, $start)
    $result.CursorVsVisibleEnd = $cursor.CompareEndpoints(
        $start, $visible, $end)

    if ($result.CursorVsVisibleStart -ge 0 -and
        $result.CursorVsVisibleEnd -le 0) {
        $prefixRange = $visible.Clone()
        $prefixRange.MoveEndpointByRange($end, $cursor, $start)
        $prefix = $prefixRange.GetText(-1)
        $prefixLines = [regex]::Split($prefix, "`r?`n")
        $result.CursorInsideVisible = $true
        $result.CursorRow = $prefixLines.Length - 1
        $result.CursorColumn = $prefixLines[-1].Length
        $result.PrefixLength = $prefix.Length
    }
    else {
        $result.CursorInsideVisible = $false
    }
}
catch {
    $result.Error = $_.Exception.ToString()
}

[pscustomobject]$result | ConvertTo-Json -Depth 6 -Compress
