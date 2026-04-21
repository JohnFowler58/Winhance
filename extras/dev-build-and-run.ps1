# dev-build-and-run.ps1
#
# Bumps the Winhance.UI csproj Version / FileVersion / AssemblyVersion to
# today's date (if the current value is older), rebuilds in Debug/x64, and
# launches the fresh binary.
#
# Run from any working directory:
#   pwsh -File .\extras\dev-build-and-run.ps1
# or
#   & .\extras\dev-build-and-run.ps1

$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path -Parent $PSScriptRoot
$csprojPath = Join-Path $repoRoot 'src\Winhance.UI\Winhance.UI.csproj'
$exePath    = Join-Path $repoRoot 'src\Winhance.UI\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\Winhance.exe'
$msbuild    = Join-Path ${env:ProgramFiles} 'Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'

if (-not (Test-Path $csprojPath)) { throw "csproj not found: $csprojPath" }
if (-not (Test-Path $msbuild))    { throw "MSBuild not found: $msbuild" }

# --- Version bump ---------------------------------------------------------
# Use .NET APIs for IO so we preserve UTF-8-no-BOM. Windows PowerShell 5.1's
# Get-Content / Set-Content default to ANSI (Windows-1252) which mangles non-
# ASCII characters like the copyright symbol, and -Encoding UTF8 on 5.1 writes
# a BOM that git treats as a change.
$today        = Get-Date -Format 'yy.MM.dd'
$utf8NoBom    = New-Object System.Text.UTF8Encoding $false
$content      = [System.IO.File]::ReadAllText($csprojPath, [System.Text.Encoding]::UTF8)
$versionRegex = '<(Version|FileVersion|AssemblyVersion)>(\d{2}\.\d{2}\.\d{2})</\1>'

if ($content -match $versionRegex) {
    $current = $matches[2]
    if ([version]$today -gt [version]$current) {
        Write-Host "Bumping version $current -> $today" -ForegroundColor Cyan
        $newContent = $content -replace $versionRegex, ('<$1>' + $today + '</$1>')
        [System.IO.File]::WriteAllText($csprojPath, $newContent, $utf8NoBom)
    }
    else {
        Write-Host "Version already current: $current (today: $today) - not bumping" -ForegroundColor DarkGray
    }
}
else {
    Write-Host 'No Version tag found in csproj to bump.' -ForegroundColor Yellow
}

# --- Build ---------------------------------------------------------------
Push-Location $repoRoot
try {
    $buildErrors = & $msbuild `
        'src\Winhance.UI\Winhance.UI.csproj' `
        -p:Configuration=Debug -p:Platform=x64 -restore -v:q -nologo 2>&1 |
        Where-Object { $_ -match 'error ' }

    if ($buildErrors) {
        $buildErrors | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        throw 'Build failed.'
    }

    if (-not (Test-Path $exePath)) {
        throw "Build succeeded but exe not found at: $exePath"
    }

    Write-Host "Launching: $exePath" -ForegroundColor Green
    & $exePath
}
finally {
    Pop-Location
}
