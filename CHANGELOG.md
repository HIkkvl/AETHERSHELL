# Changelog

Формат основан на [Keep a Changelog](https://keepachangelog.com/ru/1.1.0/), версионирование — [Semantic Versioning](https://semver.org/lang/ru/).

## [2.0.0] — не выпущено

Два сервера слиты в один мультитенантный. `AetherShell.Central` растворился в `SpikServer`:
реестр клубов, аккаунты владельцев и выдача установщиков переехали внутрь, синхронизировать
между базами больше нечего.

### Добавлено

- **Мультитенантность**: сущности `Account` (PlatformAdmin / Owner) и `Club`, `ClubId` во всех клубных таблицах, составные уникальные индексы `(ClubId, Username)`, `(ClubId, Email)`, `(ClubId, HardwareId)` и глобальные query-фильтры в `AppDbContext`.
- **`ClubScopeMiddleware`**: клуб запроса определяется по `X-Club-Id` или `X-Club-Key` с проверкой прав; владелец и платформенный админ получают роль `Super` внутри своего клуба.
- **`[RequireClub]`**: клубные эндпоинты отказывают запросу без контекста клуба вместо выполнения по всей базе.
- **Один origin**: лендинг (`/`), кабинет (`/cabinet`) и админ-панель (`/panel`) отдаются тем же сервером, что и API; адреса прослушивания задаются явно через `SPIK_URLS`.
- **Провижининг клуба одной кнопкой** в кабинете: `Account` + `Club` + `EnrollmentKey`, пароль на экране и письмом.
- **`GET /api/clubs/{id}/installer`**: zip из `SpikInstaller.exe` и готового `server.config` с `SERVER_URL` и `CLUB_KEY`.
- **`POST /api/Auth/logout`**: посетитель завершает свою сессию сам, без админских прав.
- **`HardwareId`**: стабильный GUID вместо MAC как идентификатор ПК; MAC остался справочным полем.
- **Docker-образ сервера**, собирающий админ-панель внутри сборки.

### Изменено

- SignalR-группы стали клубными: `{clubId}:Admins` и `{clubId}:{pcId}`; `SessionWorker` и `ComputerStatusService` работают в разрезе клубов.
- `server.config` перешёл на `SERVER_URL` (полный https-URL) и `CLUB_KEY` вместо `SERVER_IP` / `SERVER_PORT`; установщик больше не спрашивает IP.
- Шелл передаёт токен и ключ клуба заголовками, а не в query-строке хаба.
- Телеграм-бот сведён к уведомлениям: заявка с лендинга больше ничего не создаёт.
- `docker-compose` перестал монтировать том поверх `/app`, данные вынесены в `/data`, добавлен PostgreSQL.

### Удалено

- Проекты `AetherShell.Central`, `AetherShell.Central.Tests`, `central-dashboard`, `SpikServerConfig`.
- Heartbeat, `TenantComputer`-зеркало и обязательная проверка Central с `Environment.Exit(1)` при старте клубного сервера.
- Секреты из `appsettings.json`: подключение к БД, ключ JWT и SMTP берутся из переменных окружения.

## [1.0.0] — 2026-04-06

### Добавлено

- **AetherShell.Central**: центральный API — регистрация клубов через заявки (лиды), управление тенантами, API-ключи, heartbeat от клубных серверов, личный кабинет владельца клуба.
- **telegram-bot**: Telegram-бот на aiogram 3 для обработки кнопок «Принять» / «Отклонить» заявок.
- **SpikServer**: серверная часть клуба — управление ПК, пользователями, сессиями, тарифами, заказами; SignalR hub для real-time связи с десктопными клиентами; подключение к Central через heartbeat.
- **admin-panel**: React-панель управления клубом (компьютеры, пользователи, тарифы, заказы, баннеры, чат).
- **central-dashboard**: React-панель суперадмина Central.
- **ClubApp**: десктопная WinForms-оболочка для ПК клуба.
- **SpikInstaller**: установщик клубного сервера.
- **SpikServerConfig**: графический конфигуратор SpikServer.
- **AetherMain**: лендинг и страница личного кабинета.
- **Docker Compose** для развёртывания Central + бот.
- **CI/CD**: GitHub Actions — сборка всех компонентов, интеграционные тесты Central, Docker build.
- **EF Core Migrations** для Central (замена ручных SQL-скриптов).
- **Rate limiting** на эндпоинтах авторизации и публичных API Central.
- **Health check** эндпоинт `/healthz` в Central.
- **JWT issuer validation** в Central.
- Корневой README с описанием архитектуры, деплоя и переменных окружения.
