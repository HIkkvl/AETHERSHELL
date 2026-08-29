# ============================================
# Скрипт автоматического бэкапа PostgreSQL
# Для компьютерного клуба SPIK
# ============================================

# Настройки (измените под свои параметры)
$PG_HOST = "localhost"
$PG_PORT = "5432"
$PG_DATABASE = "SpikClubDb"
$PG_USER = "postgres"
$PG_PASSWORD = "ВАШ_ПАРОЛЬ"  # Измените на реальный пароль

# Папка для бэкапов
$BACKUP_DIR = "C:\SpikBackups"
$DAYS_TO_KEEP = 7  # Хранить бэкапы за последние N дней

# Создаем папку если не существует
if (!(Test-Path $BACKUP_DIR)) {
    New-Item -ItemType Directory -Path $BACKUP_DIR -Force
}

# Формируем имя файла с датой
$DATE = Get-Date -Format "yyyy-MM-dd_HH-mm"
$BACKUP_FILE = "$BACKUP_DIR\spik_backup_$DATE.sql"

# Устанавливаем пароль для pg_dump
$env:PGPASSWORD = $PG_PASSWORD

# Выполняем бэкап
Write-Host "Начинаем бэкап базы данных..." -ForegroundColor Cyan
& "C:\Program Files\PostgreSQL\16\bin\pg_dump.exe" `
    -h $PG_HOST `
    -p $PG_PORT `
    -U $PG_USER `
    -d $PG_DATABASE `
    -F p `
    -f $BACKUP_FILE

if ($LASTEXITCODE -eq 0) {
    Write-Host "Бэкап успешно создан: $BACKUP_FILE" -ForegroundColor Green
    
    # Сжимаем бэкап
    $ZIP_FILE = "$BACKUP_FILE.zip"
    Compress-Archive -Path $BACKUP_FILE -DestinationPath $ZIP_FILE -Force
    Remove-Item $BACKUP_FILE
    Write-Host "Бэкап сжат: $ZIP_FILE" -ForegroundColor Green
    
    # Удаляем старые бэкапы
    $CUTOFF_DATE = (Get-Date).AddDays(-$DAYS_TO_KEEP)
    Get-ChildItem -Path $BACKUP_DIR -Filter "spik_backup_*.zip" | 
        Where-Object { $_.LastWriteTime -lt $CUTOFF_DATE } | 
        ForEach-Object {
            Write-Host "Удаляем старый бэкап: $($_.Name)" -ForegroundColor Yellow
            Remove-Item $_.FullName
        }
} else {
    Write-Host "ОШИБКА: Бэкап не удался!" -ForegroundColor Red
}

# Очищаем переменную пароля
$env:PGPASSWORD = ""

Write-Host ""
Write-Host "============================================"
Write-Host "Для автоматизации добавьте в Планировщик задач Windows:"
Write-Host 'schtasks /create /tn "SpikDBBackup" /tr "powershell -ExecutionPolicy Bypass -File C:\SpikServer\scripts\backup-db.ps1" /sc daily /st 03:00'
Write-Host "============================================"
