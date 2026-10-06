# Process-level regression smoke test against the built Release exe. Complements the unit tests
# in tests/MyVNC.Rfb.Tests (which check the RFB protocol library in isolation) by checking the
# things only a running process can reveal: does it start, stay responsive, avoid runaway
# resource growth, and correctly enforce single-instance behavior. This exact script would have
# caught the SingleInstance tight-loop bug (multi-GB memory growth in seconds) from this
# project's history if it had existed then.
#
# Usage: powershell -ExecutionPolicy Bypass -File tooling\scripts\smoke-test.ps1
# Exit code 0 = all checks passed, non-zero = a check failed (message printed).

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$exePath = Join-Path $repoRoot "src\MyVNC.App\bin\Release\net10.0-windows\MyVNC.App.exe"

$failures = @()
function Check($name, [scriptblock]$body) {
    Write-Host "  $name... " -NoNewline
    try {
        & $body
        Write-Host "OK" -ForegroundColor Green
    } catch {
        Write-Host "FAIL: $($_.Exception.Message)" -ForegroundColor Red
        $script:failures += "$name : $($_.Exception.Message)"
    }
}

if (-not (Test-Path $exePath)) {
    throw "Release build not found at $exePath - run: dotnet build MyVNC.slnx -c Release"
}

Write-Host "Cleaning up any existing MyVNC processes..."
Get-Process -Name "MyVNC.App" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

Write-Host "`n[1] First launch"
$proc1 = Start-Process -FilePath $exePath -PassThru
Start-Sleep -Seconds 3

Check "process is running" {
    $p = Get-Process -Id $proc1.Id -ErrorAction SilentlyContinue
    if (-not $p) { throw "process exited immediately" }
}

Check "main window appeared" {
    $p = Get-Process -Id $proc1.Id -ErrorAction Stop
    if ([string]::IsNullOrEmpty($p.MainWindowTitle)) { throw "no MainWindowTitle" }
}

Check "window is responding" {
    $p = Get-Process -Id $proc1.Id -ErrorAction Stop
    if (-not $p.Responding) { throw "window not responding" }
}

$memBefore = (Get-Process -Id $proc1.Id -ErrorAction Stop).WorkingSet64
Write-Host "  (memory after launch: $([math]::Round($memBefore/1MB,1)) MB)"

Write-Host "`n[2] Second launch (single-instance behavior)"
$proc2 = Start-Process -FilePath $exePath -PassThru
Start-Sleep -Seconds 2

Check "second process exits (forwarded to first instance)" {
    $p2 = Get-Process -Id $proc2.Id -ErrorAction SilentlyContinue
    if ($p2 -and -not $p2.HasExited) { throw "second process is still running - single-instance forwarding failed" }
}

Check "exactly one MyVNC process remains" {
    $running = Get-Process -Name "MyVNC.App" -ErrorAction SilentlyContinue
    $count = @($running).Count
    if ($count -ne 1) { throw "expected 1 process, found $count" }
}

Write-Host "`n[3] No runaway resource growth (this exact check would have caught the pipe-loop bug)"
Start-Sleep -Seconds 5
Check "memory stayed within 2x of post-launch baseline" {
    $p = Get-Process -Name "MyVNC.App" -ErrorAction Stop | Select-Object -First 1
    $memAfter = $p.WorkingSet64
    Write-Host "  (memory after 5s idle: $([math]::Round($memAfter/1MB,1)) MB)" -NoNewline
    if ($memAfter -gt $memBefore * 2 + 50MB) { throw "memory grew from $([math]::Round($memBefore/1MB,1))MB to $([math]::Round($memAfter/1MB,1))MB" }
}

Write-Host "`n[4] Clean shutdown"
Check "process terminates on kill" {
    Get-Process -Name "MyVNC.App" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    $remaining = Get-Process -Name "MyVNC.App" -ErrorAction SilentlyContinue
    if ($remaining) { throw "process still running after Stop-Process" }
}

Write-Host ""
if ($failures.Count -eq 0) {
    Write-Host "All smoke tests passed." -ForegroundColor Green
    exit 0
} else {
    Write-Host "$($failures.Count) check(s) failed:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
