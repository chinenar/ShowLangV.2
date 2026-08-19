$ErrorActionPreference = 'Stop'
$startupFile = Join-Path `
    ([Environment]::GetFolderPath('Startup')) `
    'ShowLang.bat'

if (-not (Test-Path -LiteralPath $startupFile)) {
    Write-Host 'No ShowLang startup entry was found.'
    exit 0
}

$content = Get-Content -LiteralPath $startupFile -Raw
if ($content -notmatch 'ShowLangV\.2\\app\\ShowLang\.exe') {
    throw "The startup file does not point to this project: $startupFile"
}

Remove-Item -LiteralPath $startupFile -Force
Write-Host "Startup entry removed: $startupFile"
