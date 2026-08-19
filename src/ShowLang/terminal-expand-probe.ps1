Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$element = [System.Windows.Automation.AutomationElement]::FocusedElement
$patternObject = $null
$result = [ordered]@{
    Name = $element.Current.Name
    Class = $element.Current.ClassName
    Pid = $element.Current.ProcessId
}

try {
    $found = $element.TryGetCurrentPattern(
        [System.Windows.Automation.TextPattern]::Pattern,
        [ref]$patternObject)
    $result.PatternFound = $found

    if ($found) {
        $pattern = [System.Windows.Automation.TextPattern]$patternObject
        $ranges = $pattern.GetSelection()
        $result.SelectionCount = $ranges.Length

        if ($ranges.Length -gt 0) {
            $range = $ranges[-1]
            $start = [System.Windows.Automation.Text.TextPatternRangeEndpoint]::Start
            $end = [System.Windows.Automation.Text.TextPatternRangeEndpoint]::End
            $result.Collapsed = ($range.CompareEndpoints(
                $start, $range, $end) -eq 0)

            $expanded = $range.Clone()
            $expanded.ExpandToEnclosingUnit(
                [System.Windows.Automation.Text.TextUnit]::Character)
            $result.ExpandedText = $expanded.GetText(20)
            $result.ExpandedRects = @(
                $expanded.GetBoundingRectangles() |
                    ForEach-Object {
                        "$($_.X),$($_.Y),$($_.Width),$($_.Height)"
                    })
            $result.CursorVsStart = $range.CompareEndpoints(
                $start, $expanded, $start)
            $result.CursorVsEnd = $range.CompareEndpoints(
                $start, $expanded, $end)
        }
    }
}
catch {
    $result.Error = $_.Exception.ToString()
}

[pscustomobject]$result | ConvertTo-Json -Depth 5 -Compress
