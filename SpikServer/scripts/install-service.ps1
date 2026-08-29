# ============================================
# Установка SpikServer как Windows Service
# ============================================

# Требует запуска от имени Администратора!

$SERVICE_NAME = "SpikClubServer"
$SERVICE_DISPLAY = "Spik Club Server"
$SERVICE_DESC = "Сервер управления компьютерным клубом SPIK"

# Путь к опубликованному приложению
$APP_PATH = "C:\SpikServer\SpikServer.exe"

# Проверяем права администратора
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")
if (-not $isAdmin) {
    Write-Host "ОШИБКА: Запустите скрипт от имени Администратора!" -ForegroundColor Red
    exit 1
}

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "Установка SpikServer как Windows Service" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

# Проверяем существование файла
if (!(Test-Path $APP_PATH)) {
    Write-Host ""
    Write-Host "ОШИБКА: Файл $APP_PATH не найден!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Сначала опубликуйте приложение командой:" -ForegroundColor Yellow
    Write-Host "dotnet publish -c Release -o C:\SpikServer" -ForegroundColor White
    exit 1
}

# Останавливаем службу если существует
$existingService = Get-Service -Name $SERVICE_NAME -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "Останавливаем существующую службу..." -ForegroundColor Yellow
    Stop-Service -Name $SERVICE_NAME -Force -ErrorAction SilentlyContinue
    sc.exe delete $SERVICE_NAME
    Start-Sleep -Seconds 2
}

# Создаем службу
Write-Host "Создаем службу $SERVICE_NAME..." -ForegroundColor Cyan
New-Service -Name $SERVICE_NAME `
    -BinaryPathName $APP_PATH `
    -DisplayName $SERVICE_DISPLAY `
    -Description $SERVICE_DESC `
    -StartupType Automatic

# Настраиваем автоматический перезапуск при сбое
sc.exe failure $SERVICE_NAME reset= 86400 actions= restart/5000/restart/10000/restart/30000

# Запускаем службу
Write-Host "Запускаем службу..." -ForegroundColor Cyan
Start-Service -Name $SERVICE_NAME

# Проверяем статус
$service = Get-Service -Name $SERVICE_NAME
if ($service.Status -eq 'Running') {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Green
    Write-Host "Служба успешно установлена и запущена!" -ForegroundColor Green
    Write-Host "============================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Команды управления:" -ForegroundColor Cyan
    Write-Host "  Остановить:  Stop-Service $SERVICE_NAME" -ForegroundColor White
    Write-Host "  Запустить:   Start-Service $SERVICE_NAME" -ForegroundColor White
    Write-Host "  Перезапуск:  Restart-Service $SERVICE_NAME" -ForegroundColor White
    Write-Host "  Статус:      Get-Service $SERVICE_NAME" -ForegroundColor White
    Write-Host "  Удалить:     sc.exe delete $SERVICE_NAME" -ForegroundColor White
} else {
    Write-Host ""
    Write-Host "ОШИБКА: Служба не запустилась. Проверьте логи." -ForegroundColor Red
}
