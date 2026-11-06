param(
    [string]$Version,
    [string]$GitHubRepo = "ferrumfeed/AsicFarm",
    [string]$ServerUpdatesDir = "$PSScriptRoot\publish\updates"
)

$ErrorActionPreference = "Stop"

function Pause-Before-Exit {
    Write-Host ""
    Read-Host "Press Enter to close"
}

try {
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = Read-Host "Enter version (e.g. 1.0.6)"
    }
    if ([string]::IsNullOrWhiteSpace($Version)) { throw "Version is required." }
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw "GitHub CLI (gh) not found. Install it: winget install --id GitHub.cli" }

    # --- Убиваем запущенные процессы клиента, чтобы освободить .dll ---
    $runningClients = Get-Process -Name "AsicFarm.Client" -ErrorAction SilentlyContinue
    if ($runningClients) {
        Write-Host "=== Killing running AsicFarm.Client processes ===" -ForegroundColor Yellow
        $runningClients | Stop-Process -Force
        Start-Sleep -Milliseconds 500
        Write-Host "  OK: Process(es) stopped" -ForegroundColor Green
    }

    # --- [1/5] Публикация клиента ---
    Write-Host "=== [1/5] Publishing client v$Version ===" -ForegroundColor Cyan
    $publishDir = Join-Path $PSScriptRoot "publish\Client"
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

    & dotnet publish (Join-Path $PSScriptRoot "src\AsicFarm.Client\AsicFarm.Client.csproj") -c Release -r win-x64 --self-contained false -o $publishDir -p:Version=$Version
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

    $zipName = "AsicFarm.Client-$Version.zip"
    $zipPath = Join-Path $PSScriptRoot "publish\$zipName"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
    Write-Host "  OK: ZIP created: $zipPath" -ForegroundColor Green

    # --- [2/5] Создание релиза на GitHub ---
    Write-Host "=== [2/5] Creating GitHub Release v$Version ===" -ForegroundColor Cyan
    $tagName = "v$Version"

    Write-Host "  Cleaning up old release $tagName (if exists)..." -ForegroundColor DarkGray
    $oldPref = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    gh release delete $tagName --repo $GitHubRepo --yes --cleanup-tag 2>&1 | Out-Null
    $ErrorActionPreference = $oldPref

    gh release create $tagName $zipPath --repo $GitHubRepo --title "AsicFarm Client $Version" --notes "Release $Version" --latest
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed." }
    Write-Host "  OK: Release created" -ForegroundColor Green

    # --- [3/5] Формирование URL ---
    Write-Host "=== [3/5] Building download URL ===" -ForegroundColor Cyan
    $downloadUrl = "https://github.com/$GitHubRepo/releases/download/$tagName/$zipName"
    Write-Host "  URL: $downloadUrl" -ForegroundColor Green

    Write-Host "  Verifying URL availability..." -ForegroundColor DarkGray
    $verified = $false
    for ($i = 1; $i -le 15; $i++) {
        try {
            $resp = Invoke-WebRequest -Uri $downloadUrl -Method Head -UseBasicParsing -TimeoutSec 10 -ErrorAction Stop
            if ($resp.StatusCode -eq 200) {
                $verified = $true
                $size = [math]::Round([int64]$resp.Headers['Content-Length'] / 1MB, 2)
                Write-Host "  OK: URL verified ($size MB)" -ForegroundColor Green
                break
            }
        } catch { }
        Start-Sleep -Seconds 2
    }
    if (-not $verified) {
        Write-Host "  WARNING: URL check failed. Continuing anyway." -ForegroundColor Yellow
    }

    # --- [4/5] Хеш и changelog ---
    Write-Host "=== [4/5] Calculating SHA1 & creating changelog ===" -ForegroundColor Cyan
    $hash = (Get-FileHash $zipPath -Algorithm SHA1).Hash
    if (-not (Test-Path $ServerUpdatesDir)) { New-Item -ItemType Directory -Path $ServerUpdatesDir -Force | Out-Null }
    $changelogName = "changelog-$Version.txt"
    $changelogPath = Join-Path $ServerUpdatesDir $changelogName
    "Release $Version`r`n- Automated update via GitHub Releases" | Out-File -FilePath $changelogPath -Encoding utf8
    Write-Host "  OK: Changelog: $changelogPath" -ForegroundColor Green

    # --- [5/5] latest.json ---
    Write-Host "=== [5/5] Generating latest.json ===" -ForegroundColor Cyan
    $latestJson = @{
        version = $Version
        url = $downloadUrl
        changelog = "http://89.125.129.150:5000/updates/$changelogName"
        mandatory = @{ value = $false; minVersion = "1.0.0.0"; mode = 0 }
        checksum = @{ value = $hash; hashingAlgorithm = "SHA1" }
    } | ConvertTo-Json -Depth 5

    $latestPath = Join-Path $ServerUpdatesDir "latest.json"
    $latestJson | Out-File -FilePath $latestPath -Encoding utf8
    Write-Host "  OK: JSON: $latestPath" -ForegroundColor Green

    # --- Итог ---
    Write-Host ""
    Write-Host "=== DONE ===" -ForegroundColor Green
    Write-Host "  Version:   $Version"
    Write-Host "  GitHub:    https://github.com/$GitHubRepo/releases/tag/$tagName"
    Write-Host "  Asset URL: $downloadUrl"
    Write-Host ""
    Write-Host "=== NEXT STEP: Copy to server ===" -ForegroundColor Yellow
    Write-Host "  Copy these 2 files to C:\Users\Admin\Desktop\server\updates on the SERVER (via RDP):" -ForegroundColor Yellow
    Write-Host "    $latestPath" -ForegroundColor White
    Write-Host "    $changelogPath" -ForegroundColor White
    Write-Host ""
    Write-Host "  Then restart AsicFarm.Server.exe (or wait ~10s - it reads latest.json on every request)." -ForegroundColor Yellow
}
catch {
    Write-Host ""
    Write-Host "=== ERROR ===" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    if ($_.ScriptStackTrace) { Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray }
}
finally { Pause-Before-Exit }