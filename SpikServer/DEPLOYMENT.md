# 🚀 Руководство по развёртыванию SpikServer

## 📋 Требования

- Windows Server 2019/2022 или Windows 10/11
- .NET 10.0 Runtime
- PostgreSQL 15+
- SSL сертификат (для HTTPS)

---

## 1️⃣ Подготовка сервера

### Установка PostgreSQL

1. Скачайте PostgreSQL: https://www.postgresql.org/download/windows/
2. Установите с настройками по умолчанию
3. Запомните пароль для пользователя `postgres`
4. Создайте базу данных:

```sql
CREATE DATABASE SpikClubDb;
```

### Установка .NET Runtime

```powershell
# Скачайте и установите .NET 10.0 Runtime
winget install Microsoft.DotNet.Runtime.10
```

---

## 2️⃣ Настройка переменных окружения

Откройте PowerShell от имени Администратора:

```powershell
# JWT секретный ключ (сгенерируйте свой!)
[Environment]::SetEnvironmentVariable("SPIK_JWT_SECRET", "ВашСуперСекретныйКлюч_МинимумЗОСимволов!", "Machine")

# Строка подключения к БД
[Environment]::SetEnvironmentVariable("SPIK_DB_CONNECTION", "Host=localhost;Port=5432;Database=SpikClubDb;Username=postgres;Password=ВАШПАРОЛЬ", "Machine")

# Разрешённые домены для CORS (через запятую)
[Environment]::SetEnvironmentVariable("SPIK_CORS_ORIGINS", "https://admin.yourclub.com,https://yourclub.com", "Machine")
```

---

## 3️⃣ Публикация приложения

В папке проекта выполните:

```powershell
cd SpikServer/SpikServer
dotnet publish -c Release -o C:\SpikServer
```

---

## 4️⃣ Настройка HTTPS

### Вариант A: Самоподписанный сертификат (для тестов)

```powershell
# Создаём сертификат
$cert = New-SelfSignedCertificate -DnsName "localhost", "yourserver.local" -CertStoreLocation "cert:\LocalMachine\My"

# Экспортируем в PFX
$password = ConvertTo-SecureString -String "YourPassword" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath "C:\SpikServer\server.pfx" -Password $password
```

### Вариант B: Let's Encrypt (для production)

1. Установите win-acme: https://www.win-acme.com/
2. Получите сертификат:
```powershell
wacs.exe --target manual --host admin.yourclub.com --store pfxfile --pfxfilepath C:\SpikServer\server.pfx
```

### Конфигурация Kestrel

Создайте файл `C:\SpikServer\appsettings.Production.json`:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:80"
      },
      "Https": {
        "Url": "https://0.0.0.0:443",
        "Certificate": {
          "Path": "C:\\SpikServer\\server.pfx",
          "Password": "YourPassword"
        }
      }
    }
  },
  "Logging": {
    "LogLevel": {
      "Default": "Warning"
    }
  }
}
```

---

## 5️⃣ Установка как Windows Service

```powershell
# Запустите от имени Администратора
powershell -ExecutionPolicy Bypass -File C:\SpikServer\scripts\install-service.ps1
```

Или вручную:

```powershell
# Создаём службу
New-Service -Name "SpikClubServer" -BinaryPathName "C:\SpikServer\SpikServer.exe" -DisplayName "Spik Club Server" -StartupType Automatic

# Запускаем
Start-Service SpikClubServer
```

---

## 6️⃣ Настройка автоматических бэкапов

```powershell
# Добавляем задачу в планировщик (ежедневно в 3:00)
schtasks /create /tn "SpikDBBackup" /tr "powershell -ExecutionPolicy Bypass -File C:\SpikServer\scripts\backup-db.ps1" /sc daily /st 03:00 /ru SYSTEM
```

---

## 7️⃣ Настройка Firewall

```powershell
# Разрешаем порты 80 и 443
New-NetFirewallRule -DisplayName "SpikServer HTTP" -Direction Inbound -Port 80 -Protocol TCP -Action Allow
New-NetFirewallRule -DisplayName "SpikServer HTTPS" -Direction Inbound -Port 443 -Protocol TCP -Action Allow
```

---

## 8️⃣ Развёртывание Admin Panel

```powershell
cd admin-panel

# Установите зависимости
npm install

# Сборка для production
npm run build

# Результат в папке dist/ - разместите на веб-сервере (nginx/IIS)
```

### Настройка API URL в админке

Перед сборкой обновите `admin-panel/src/api.ts`:

```typescript
const API_BASE = "https://yourserver.com/api";
const SIGNALR_URL = "https://yourserver.com/clubhub";
```

---

## 🔧 Управление службой

| Команда | Действие |
|---------|----------|
| `Get-Service SpikClubServer` | Статус службы |
| `Start-Service SpikClubServer` | Запустить |
| `Stop-Service SpikClubServer` | Остановить |
| `Restart-Service SpikClubServer` | Перезапустить |

---

## 📊 Мониторинг

Логи службы:
```powershell
Get-EventLog -LogName Application -Source SpikClubServer -Newest 50
```

---

## ⚠️ Чек-лист перед запуском

- [ ] PostgreSQL установлен и база создана
- [ ] Переменные окружения настроены
- [ ] SSL сертификат установлен
- [ ] Firewall настроен
- [ ] Служба запущена и работает
- [ ] Бэкап настроен
- [ ] Admin Panel развёрнута
- [ ] Тестовый вход работает

---

## 🆘 Troubleshooting

**Служба не запускается:**
```powershell
# Проверьте логи
Get-EventLog -LogName Application -Newest 20 | Where-Object {$_.Source -like "*SpikClubServer*"}
```

**Ошибка подключения к БД:**
- Проверьте переменную `SPIK_DB_CONNECTION`
- Убедитесь что PostgreSQL запущен
- Проверьте firewall для порта 5432

**Ошибка SSL:**
- Проверьте путь к сертификату
- Проверьте пароль сертификата
- Убедитесь что сертификат не истёк
