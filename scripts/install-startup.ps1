$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$executable = Join-Path $repoRoot 'app\ShowLang.exe'

if (-not (Test-Path -LiteralPath $executable)) {
    throw "Build ShowLang first. Executable not found: $executable"
}

$startupDirectory = [Environment]::GetFolderPath('Startup')
$startupFile = Join-Path $startupDirectory 'ShowLang.bat'
$content = @(
    '@echo off'
    "start `"`" `"$executable`""
    'exit /b 0'
) -join "`r`n"

[IO.File]::WriteAllText(
    $startupFile,
    $content + "`r`n",
    [Text.UTF8Encoding]::new($false))

Write-Host "Startup entry installed: $startupFile"
