# ============================================
# Spik Client - Скрипт установки
# Запускать от имени Администратора!
# ============================================

param(
    [string]$InstallPath = "C:\AetherShell",
    [string]$Username = "",
    [string]$Password = "",
    [switch]$SetupAutoLogin,
    [switch]$InstallWatchdog
)

$ErrorActionPreference = "Stop"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "   Установка AetherShell Client" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Проверка прав администратора
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ОШИБКА: Запустите скрипт от имени Администратора!" -ForegroundColor Red
    exit 1
}

# Создание папки установки
Write-Host "[1/5] Создание папки установки..." -ForegroundColor Yellow
if (-not (Test-Path $InstallPath)) {
    New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
}

# Копирование файлов
Write-Host "[2/5] Копирование файлов..." -ForegroundColor Yellow
$sourcePath = Split-Path -Parent $PSScriptRoot
$clientBin = Join-Path $sourcePath "ClubApp\bin\Release\net48"

if (-not (Test-Path $clientBin)) {
    $clientBin = Join-Path $sourcePath "ClubApp\bin\Debug\net48"
}

if (Test-Path $clientBin) {
    Copy-Item -Path "$clientBin\*" -Destination $InstallPath -Recurse -Force
    Write-Host "   Файлы скопированы из: $clientBin" -ForegroundColor Green
} else {
    Write-Host "   ПРЕДУПРЕЖДЕНИЕ: Папка сборки не найдена. Скопируйте файлы вручную." -ForegroundColor Yellow
}

# Копирование Watchdog
if ($InstallWatchdog) {
    $watchdogBin = Join-Path $sourcePath "SpikWatchdog\bin\Release\net8.0-windows"
    if (-not (Test-Path $watchdogBin)) {
        $watchdogBin = Join-Path $sourcePath "SpikWatchdog\bin\Debug\net8.0-windows"
    }
    if (Test-Path $watchdogBin) {
        Copy-Item -Path "$watchdogBin\*" -Destination $InstallPath -Recurse -Force
        Write-Host "   Watchdog скопирован" -ForegroundColor Green
    }
}

# Настройка автозапуска через Task Scheduler
Write-Host "[3/5] Настройка автозапуска..." -ForegroundColor Yellow

$clientExe = Join-Path $InstallPath "AetherShell.Client.exe"
$taskName = "AetherShell.ClientAutoStart"

# Удаляем старую задачу если есть
Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue

# Создаём задачу на запуск при входе любого пользователя
$action = New-ScheduledTaskAction -Execute $clientExe -WorkingDirectory $InstallPath
$trigger = New-ScheduledTaskTrigger -AtLogOn
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
$principal = New-ScheduledTaskPrincipal -GroupId "BUILTIN\Users" -RunLevel Highest

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null
Write-Host "   Задача автозапуска создана: $taskName" -ForegroundColor Green

# Установка Watchdog как службы
if ($InstallWatchdog) {
    Write-Host "[4/5] Установка Watchdog службы..." -ForegroundColor Yellow
    
    $watchdogExe = Join-Path $InstallPath "AetherShell.Watchdog.exe"
    $serviceName = "AetherShell.Watchdog"
    
    # Останавливаем и удаляем если существует
    $existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($existingService) {
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
        sc.exe delete $serviceName | Out-Null
        Start-Sleep -Seconds 2
    }
    
    # Создаём службу
    New-Service -Name $serviceName `
        -BinaryPathName $watchdogExe `
        -DisplayName "AetherShell Client Watchdog" `
        -Description "Мониторинг и автоперезапуск AetherShell Client" `
        -StartupType Automatic | Out-Null
    
    # Настраиваем восстановление службы при сбое
    sc.exe failure $serviceName reset=86400 actions=restart/5000/restart/10000/restart/30000 | Out-Null
    
    # Запускаем службу
    Start-Service -Name $serviceName
    Write-Host "   Служба Watchdog установлена и запущена" -ForegroundColor Green
} else {
    Write-Host "[4/5] Пропуск установки Watchdog (не указан флаг -InstallWatchdog)" -ForegroundColor Gray
}

# Настройка автологина
if ($SetupAutoLogin) {
    Write-Host "[5/5] Настройка автоматического входа..." -ForegroundColor Yellow
    
    if ([string]::IsNullOrEmpty($Username)) {
        $Username = Read-Host "Введите имя пользователя для автовхода"
    }
    if ([string]::IsNullOrEmpty($Password)) {
        $securePassword = Read-Host "Введите пароль" -AsSecureString
        $Password = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword))
    }
    
    $regPath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
    Set-ItemProperty -Path $regPath -Name "AutoAdminLogon" -Value "1" -Type String
    Set-ItemProperty -Path $regPath -Name "DefaultUserName" -Value $Username -Type String
    Set-ItemProperty -Path $regPath -Name "DefaultPassword" -Value $Password -Type String
    Set-ItemProperty -Path $regPath -Name "DefaultDomainName" -Value $env:COMPUTERNAME -Type String
    
    Write-Host "   Автовход настроен для пользователя: $Username" -ForegroundColor Green
    Write-Host "   ВНИМАНИЕ: Пароль хранится в реестре!" -ForegroundColor Yellow
} else {
    Write-Host "[5/5] Пропуск настройки автовхода (не указан флаг -SetupAutoLogin)" -ForegroundColor Gray
}

# Финальные инструкции
Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "   Установка завершена!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "Клиент установлен в: $InstallPath" -ForegroundColor White
Write-Host ""
Write-Host "Дополнительные действия:" -ForegroundColor Cyan
Write-Host "1. Отредактируйте AppConstants.cs - укажите IP сервера" -ForegroundColor White
Write-Host "2. Пересоберите клиент: dotnet build -c Release" -ForegroundColor White
Write-Host "3. Перезагрузите ПК для проверки автозапуска" -ForegroundColor White
Write-Host ""

if (-not $SetupAutoLogin) {
    Write-Host "Для настройки автовхода запустите:" -ForegroundColor Yellow
    Write-Host "  .\install-client.ps1 -SetupAutoLogin -Username 'user' -Password 'pass'" -ForegroundColor Gray
}
