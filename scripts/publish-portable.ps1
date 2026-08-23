[CmdletBinding()]
param(
    [string]$OutputPath = (
        Join-Path $PSScriptRoot '..\dist\portable-win-x64')
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src\ShowLang\ShowLangNative.csproj'
$output = [IO.Path]::GetFullPath($OutputPath)

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $output | Out-Null

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $output

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "Portable build written to $output"
Write-Host 'Keep every file in this folder together when deploying.'
