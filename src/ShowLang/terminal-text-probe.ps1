Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$element = [System.Windows.Automation.AutomationElement]::FocusedElement
$patternObject = $null
$result = [ordered]@{
    Name = $element.Current.Name
    Class = $element.Current.ClassName
    Pid = $element.Current.ProcessId
    ElementRect = "$($element.Current.BoundingRectangle)"
}

try {
    $found = $element.TryGetCurrentPattern(
        [System.Windows.Automation.TextPattern]::Pattern,
        [ref]$patternObject)
    $result.PatternFound = $found
    if (-not $found) { throw 'TextPattern unavailable' }

    $pattern = [System.Windows.Automation.TextPattern]$patternObject
    $cursor = $pattern.GetSelection()[-1]
    $document = $pattern.DocumentRange
    $visibleRanges = $pattern.GetVisibleRanges()
    $result.VisibleCount = $visibleRanges.Length

    $start = [System.Windows.Automation.Text.TextPatternRangeEndpoint]::Start
    $end = [System.Windows.Automation.Text.TextPatternRangeEndpoint]::End

    $beforeCursor = $document.Clone()
    $beforeCursor.MoveEndpointByRange($end, $cursor, $start)
    $textBefore = $beforeCursor.GetText(-1)
    $result.DocumentCharsBeforeCursor = $textBefore.Length
    $result.DocumentLinesBeforeCursor = ($textBefore -split "`n", -1).Length
    $result.DocumentColumn = ($textBefore -split "`n", -1)[-1].Length

    if ($visibleRanges.Length -gt 0) {
        $visible = $visibleRanges[0]
        $visibleText = $visible.GetText(-1)
        $result.VisibleTextLength = $visibleText.Length
        $result.VisibleLineCount = ($visibleText -split "`n", -1).Length
        $result.VisibleTail = if ($visibleText.Length -gt 240) {
            $visibleText.Substring($visibleText.Length - 240)
        } else { $visibleText }

        $result.VisibleRects = @(
            $visible.GetBoundingRectangles() |
                ForEach-Object {
                    "$($_.X),$($_.Y),$($_.Width),$($_.Height)"
                })

        $cursorVsVisibleStart = $cursor.CompareEndpoints(
            $start, $visible, $start)
        $cursorVsVisibleEnd = $cursor.CompareEndpoints(
            $start, $visible, $end)
        $result.CursorVsVisibleStart = $cursorVsVisibleStart
        $result.CursorVsVisibleEnd = $cursorVsVisibleEnd

        if ($cursorVsVisibleStart -ge 0 -and $cursorVsVisibleEnd -le 0) {
            $visibleBeforeCursor = $visible.Clone()
            $visibleBeforeCursor.MoveEndpointByRange($end, $cursor, $start)
            $visiblePrefix = $visibleBeforeCursor.GetText(-1)
            $parts = $visiblePrefix -split "`n", -1
            $result.VisibleRow = $parts.Length - 1
            $result.VisibleColumn = $parts[-1].Length
            $result.VisibleCharsBeforeCursor = $visiblePrefix.Length
        }
    }
}
catch {
    $result.Error = $_.Exception.ToString()
}

[pscustomobject]$result | ConvertTo-Json -Depth 6 -Compress
