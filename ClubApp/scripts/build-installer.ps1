# ============================================
# Сборка установщика Spik Client
# ============================================

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$outputDir = Join-Path $projectRoot "Installer"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "   Сборка установщика AetherShell Client" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Создаём выходную папку
if (Test-Path $outputDir) {
    Remove-Item -Recurse -Force $outputDir
}
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

# 1. Сборка клиента
Write-Host "[1/4] Сборка AetherShell.Client..." -ForegroundColor Yellow
$clientProject = Join-Path $projectRoot "ClubApp\ClubApp\SpikClient.csproj"
Push-Location (Join-Path $projectRoot "ClubApp\ClubApp")
& msbuild /p:Configuration=Release /p:Platform=AnyCPU /verbosity:quiet
Pop-Location
Write-Host "   Готово" -ForegroundColor Green

# 2. Сборка Watchdog
Write-Host "[2/4] Сборка AetherShell.Watchdog..." -ForegroundColor Yellow
$watchdogProject = Join-Path $projectRoot "ClubApp\SpikWatchdog"
Push-Location $watchdogProject
dotnet publish -c Release -r win-x64 --self-contained -o (Join-Path $outputDir "WatchdogFiles") /p:PublishSingleFile=false -v quiet
Pop-Location
Write-Host "   Готово" -ForegroundColor Green

# 3. Копирование файлов клиента
Write-Host "[3/4] Копирование файлов клиента..." -ForegroundColor Yellow
$clientBin = Join-Path $projectRoot "ClubApp\ClubApp\bin\Release"
$clientFilesDir = Join-Path $outputDir "ClientFiles"
New-Item -ItemType Directory -Path $clientFilesDir -Force | Out-Null
Copy-Item -Path "$clientBin\*" -Destination $clientFilesDir -Recurse -Force
Write-Host "   Готово" -ForegroundColor Green

# 4. Сборка установщика
Write-Host "[4/4] Сборка AetherShell.Installer..." -ForegroundColor Yellow
$installerProject = Join-Path $projectRoot "ClubApp\SpikInstaller"
Push-Location $installerProject
dotnet publish -c Release -r win-x64 --self-contained -o $outputDir /p:PublishSingleFile=true -v quiet
Pop-Location
Write-Host "   Готово" -ForegroundColor Green

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "   Сборка завершена!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "Файлы установщика: $outputDir" -ForegroundColor White
Write-Host ""
Write-Host "Содержимое:" -ForegroundColor Cyan
Get-ChildItem $outputDir -File | ForEach-Object { Write-Host "  - $($_.Name)" -ForegroundColor White }
Write-Host ""
Write-Host "Для установки на ПК клуба:" -ForegroundColor Yellow
Write-Host "  1. Скопируйте всю папку Installer на ПК" -ForegroundColor White
Write-Host "  2. Запустите AetherShell.Installer.exe от Администратора" -ForegroundColor White
