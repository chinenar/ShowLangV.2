[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$element = [Windows.Automation.AutomationElement]::FocusedElement
if ($null -eq $element) { throw 'No focused UI Automation element.' }

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("Captured: $(Get-Date -Format o)")
$lines.Add("ControlType: $($element.Current.ControlType.ProgrammaticName)")
$lines.Add("Name: $($element.Current.Name)")
$lines.Add("ClassName: $($element.Current.ClassName)")
$lines.Add("AutomationId: $($element.Current.AutomationId)")
$lines.Add("ProcessId: $($element.Current.ProcessId)")
$lines.Add("HasKeyboardFocus: $($element.Current.HasKeyboardFocus)")
$lines.Add("IsKeyboardFocusable: $($element.Current.IsKeyboardFocusable)")
$lines.Add("Bounds: $($element.Current.BoundingRectangle)")

$valueObject = $null
if ($element.TryGetCurrentPattern([Windows.Automation.ValuePattern]::Pattern, [ref]$valueObject)) {
    $value = [Windows.Automation.ValuePattern]$valueObject
    $lines.Add("ValuePattern.ReadOnly: $($value.Current.IsReadOnly)")
}
$textObject = $null
if ($element.TryGetCurrentPattern([Windows.Automation.TextPattern]::Pattern, [ref]$textObject)) {
    $text = [Windows.Automation.TextPattern]$textObject
    $readOnly = $text.DocumentRange.GetAttributeValue([Windows.Automation.TextPattern]::IsReadOnlyAttribute)
    $lines.Add("TextPattern.ReadOnly: $readOnly")
    $selection = $text.GetSelection()
    $lines.Add("SelectionCount: $($selection.Count)")
    for ($i = 0; $i -lt $selection.Count; $i++) {
        $range = $selection[$i]
        $collapsed = $range.CompareEndpoints(
            [Windows.Automation.Text.TextPatternRangeEndpoint]::Start,
            $range,
            [Windows.Automation.Text.TextPatternRangeEndpoint]::End) -eq 0
        $rects = $range.GetBoundingRectangles()
        $sample = $range.GetText(80).Replace("`r", '\r').Replace("`n", '\n')
        $lines.Add("Selection[$i].Collapsed: $collapsed")
        $lines.Add("Selection[$i].Text: $sample")
        $lines.Add("Selection[$i].Rects: $($rects -join ' | ')")
    }
} else {
    $lines.Add('TextPattern: unsupported')
}
$probeProject = Join-Path $PSScriptRoot '..\UiaProbeRunner\UiaProbeRunner.csproj'
try {
    $textPattern2 = (& dotnet run --project $probeProject -c Release --no-restore 2>&1 | Out-String).Trim()
    $lines.Add("TextPattern2: $textPattern2")
} catch {
    $lines.Add("TextPattern2 probe error: $($_.Exception.Message)")
}

$directory = Join-Path $env:LOCALAPPDATA 'ShowLang\diagnostics'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$file = Join-Path $directory ("focused-target-{0}.txt" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
$lines | Set-Content -LiteralPath $file -Encoding UTF8
$lines | ForEach-Object { Write-Host $_ }
Write-Host "Saved: $file"
