# ============================================
# Spik Client - Скрипт удаления
# Запускать от имени Администратора!
# ============================================

param(
    [string]$InstallPath = "C:\AetherShell",
    [switch]$RemoveAutoLogin,
    [switch]$KeepFiles
)

$ErrorActionPreference = "Stop"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "   Удаление AetherShell Client" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Проверка прав администратора
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ОШИБКА: Запустите скрипт от имени Администратора!" -ForegroundColor Red
    exit 1
}

# Остановка и удаление Watchdog службы
Write-Host "[1/4] Остановка Watchdog службы..." -ForegroundColor Yellow
$serviceName = "AetherShell.Watchdog"
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
    Write-Host "   Служба удалена" -ForegroundColor Green
} else {
    Write-Host "   Служба не найдена" -ForegroundColor Gray
}

# Остановка клиента
Write-Host "[2/4] Остановка клиента..." -ForegroundColor Yellow
$clientProcess = Get-Process -Name "AetherShell.Client" -ErrorAction SilentlyContinue
if ($clientProcess) {
    $clientProcess | Stop-Process -Force
    Write-Host "   Клиент остановлен" -ForegroundColor Green
} else {
    Write-Host "   Клиент не запущен" -ForegroundColor Gray
}

# Удаление задачи автозапуска
Write-Host "[3/4] Удаление задачи автозапуска..." -ForegroundColor Yellow
$taskName = "AetherShell.ClientAutoStart"
$task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($task) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    Write-Host "   Задача удалена" -ForegroundColor Green
} else {
    Write-Host "   Задача не найдена" -ForegroundColor Gray
}

# Удаление автологина
if ($RemoveAutoLogin) {
    Write-Host "[4/4] Удаление автоматического входа..." -ForegroundColor Yellow
    $regPath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
    Set-ItemProperty -Path $regPath -Name "AutoAdminLogon" -Value "0" -Type String
    Remove-ItemProperty -Path $regPath -Name "DefaultPassword" -ErrorAction SilentlyContinue
    Write-Host "   Автовход отключен" -ForegroundColor Green
} else {
    Write-Host "[4/4] Пропуск удаления автовхода (не указан флаг -RemoveAutoLogin)" -ForegroundColor Gray
}

# Удаление файлов
if (-not $KeepFiles) {
    if (Test-Path $InstallPath) {
        Write-Host ""
        Write-Host "Удаление файлов из: $InstallPath" -ForegroundColor Yellow
        Remove-Item -Path $InstallPath -Recurse -Force
        Write-Host "   Файлы удалены" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "   Удаление завершено!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
