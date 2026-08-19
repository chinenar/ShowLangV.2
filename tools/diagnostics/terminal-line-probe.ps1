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
    $start = [System.Windows.Automation.Text.TextPatternRangeEndpoint]::Start
    $end = [System.Windows.Automation.Text.TextPatternRangeEndpoint]::End

    foreach ($unitName in @('Character','Word','Line','Paragraph')) {
        $unit = [System.Enum]::Parse(
            [System.Windows.Automation.Text.TextUnit], $unitName)
        $expanded = $cursor.Clone()
        $expanded.ExpandToEnclosingUnit($unit)
        $text = $expanded.GetText(300)
        $rectangles = @(
            $expanded.GetBoundingRectangles() |
                ForEach-Object {
                    "$($_.X),$($_.Y),$($_.Width),$($_.Height)"
                })
        $result[$unitName] = [ordered]@{
            TextLength = $text.Length
            Text = $text
            Rectangles = $rectangles
            CursorVsStart = $cursor.CompareEndpoints(
                $start, $expanded, $start)
            CursorVsEnd = $cursor.CompareEndpoints(
                $start, $expanded, $end)
        }
    }
}
catch {
    $result.Error = $_.Exception.ToString()
}

[pscustomobject]$result | ConvertTo-Json -Depth 8 -Compress
