[CmdletBinding()]
param(
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\ShowLang\ShowLangNative.csproj'
$output = Join-Path $PSScriptRoot 'app'
$executable = Join-Path $output 'ShowLang.exe'

if (-not (Test-Path -LiteralPath $project)) {
    throw "Project not found: $project"
}

Get-CimInstance Win32_Process |
    Where-Object { $_.ExecutablePath -eq $executable } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force }

New-Item -ItemType Directory -Force -Path $output | Out-Null

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -o $output

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "Published ShowLang to $executable"

if ($Run) {
    Start-Process -FilePath $executable
    Write-Host 'ShowLang started.'
}
