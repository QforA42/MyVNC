# Publishes a self-contained MyVNC.App build and compiles it into a Windows installer via Inno
# Setup. Run from anywhere; paths below are resolved relative to this script's own location.
#
# Requires Inno Setup (ISCC.exe) — install with:
#   winget install --id JRSoftware.InnoSetup -e

$ErrorActionPreference = "Stop"

$installerDir = $PSScriptRoot
$repoRoot = Split-Path (Split-Path $installerDir -Parent) -Parent
$publishDir = Join-Path $installerDir "publish"
$iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"

if (-not (Test-Path $iscc)) {
    $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
}
if (-not $iscc -or -not (Test-Path $iscc)) {
    throw "ISCC.exe not found. Install Inno Setup: winget install --id JRSoftware.InnoSetup -e"
}

Write-Host "Publishing self-contained win-x64 build..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish (Join-Path $repoRoot "src\MyVNC.App\MyVNC.App.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=false -p:PublishSingleFile=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$version = (Get-Content (Join-Path $repoRoot "VERSION")).Trim()
Write-Host "Compiling installer (version $version)..." -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$version" (Join-Path $installerDir "MyVNC.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC compile failed" }

Write-Host "Done - see tooling\installer\output\" -ForegroundColor Green
