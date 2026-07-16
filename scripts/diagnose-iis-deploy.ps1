# Run on the IIS server (as Administrator). Usage:
#   .\diagnose-iis-deploy.ps1 -PublishPath "D:\Projects\Claudetest\ClaudeTest-Backend\Publish" -SiteName "claudetest-BE"

param(
    [Parameter(Mandatory = $true)]
    [string]$PublishPath,
    [string]$SiteName = "claudetest-BE"
)

$ErrorActionPreference = "Continue"
Write-Host "=== IIS deploy diagnostics ===" -ForegroundColor Cyan
Write-Host "Publish path: $PublishPath"
Write-Host ""

# 1. Required files
$required = @(
    "web.config",
    "ClaudeCertPractice.Api.dll",
    "appsettings.json",
    "Data\exam-guide.json",
    "Data\questions-ankush.json",
    "Data\questions-yagnesh.json",
    "Data\questions-nilesh.json"
Write-Host "--- Required files ---" -ForegroundColor Yellow
foreach ($rel in $required) {
    $full = Join-Path $PublishPath $rel
    $ok = Test-Path $full
    Write-Host ("  [{0}] {1}" -f ($(if ($ok) { "OK" } else { "MISSING" })), $rel)
}

# 2. .NET runtimes
Write-Host ""
Write-Host "--- .NET runtimes ---" -ForegroundColor Yellow
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) {
    Write-Host "  dotnet: $($dotnet.Source)"
    dotnet --list-runtimes
} else {
    Write-Host "  dotnet NOT in PATH" -ForegroundColor Red
}

# 3. Hosting bundle module
Write-Host ""
Write-Host "--- IIS ASP.NET Core module ---" -ForegroundColor Yellow
$appcmd = Join-Path $env:windir "system32\inetsrv\appcmd.exe"
if (Test-Path $appcmd) {
    & $appcmd list modules | Select-String "AspNetCore"
    if (-not $?) { Write-Host "  AspNetCoreModuleV2 not found - install .NET 8 Hosting Bundle" -ForegroundColor Red }
} else {
    Write-Host "  IIS not installed (appcmd missing)" -ForegroundColor Red
}

# 4. Site / app pool
Write-Host ""
Write-Host "--- IIS site & app pool ---" -ForegroundColor Yellow
if (Test-Path $appcmd) {
    & $appcmd list site "$SiteName" /text:*
    $pool = (& $appcmd list site "$SiteName" /text:applicationPool 2>$null)
    if ($pool) {
        Write-Host "  App pool: $pool"
        & $appcmd list apppool "$pool" /text:*
    }
}

# 5. Run app directly (no IIS)
Write-Host ""
Write-Host "--- Direct run test (no IIS) ---" -ForegroundColor Yellow
Push-Location $PublishPath
$env:ASPNETCORE_ENVIRONMENT = "Production"
$job = Start-Job -ScriptBlock {
    param($p)
    Set-Location $p
    $env:ASPNETCORE_ENVIRONMENT = "Production"
    & dotnet ".\ClaudeCertPractice.Api.dll" --urls "http://127.0.0.1:5099" 2>&1
} -ArgumentList $PublishPath
Start-Sleep -Seconds 5
try {
    $r = Invoke-WebRequest "http://127.0.0.1:5099/api/quiz/metadata" -UseBasicParsing -TimeoutSec 5
    Write-Host "  API test: HTTP $($r.StatusCode) OK" -ForegroundColor Green
} catch {
    Write-Host "  API test FAILED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  Job output:"
    Receive-Job $job | Select-Object -Last 30
}
Stop-Job $job -ErrorAction SilentlyContinue
Remove-Job $job -ErrorAction SilentlyContinue
Pop-Location

# 6. Stdout logs
Write-Host ""
Write-Host "--- IIS stdout logs ---" -ForegroundColor Yellow
$logsDir = Join-Path $PublishPath "logs"
if (Test-Path $logsDir) {
    Get-ChildItem $logsDir -Filter "stdout*.log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object {
        Write-Host "  Latest: $($_.FullName)" -ForegroundColor Cyan
        Get-Content $_.FullName -Tail 40
    }
} else {
    Write-Host "  No logs folder. Create '$logsDir' and grant Modify to IIS AppPool\$SiteName" -ForegroundColor Red
}

# 7. Windows Event Log (recent .NET errors)
Write-Host ""
Write-Host "--- Recent Application event log (.NET) ---" -ForegroundColor Yellow
Get-WinEvent -FilterHashtable @{ LogName = "Application"; Level = 2; StartTime = (Get-Date).AddHours(-2) } -MaxEvents 15 -ErrorAction SilentlyContinue |
    Where-Object { $_.ProviderName -match "\.NET|IIS|AspNetCore" } |
    ForEach-Object { Write-Host "  [$($_.TimeCreated)] $($_.Message.Substring(0, [Math]::Min(200, $_.Message.Length)))..." }

Write-Host ""
Write-Host "Done." -ForegroundColor Cyan
