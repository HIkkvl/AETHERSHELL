# AetherShell — Telegram-бот заявок

Бот присылает новые заявки с кнопками «Принять» и «Отклонить». По «Принять»
сервер создаёт клуб с отдельной базой, аккаунт владельца и отправляет ему письмо
с доступом — то же самое, что кнопка «Подключить клуб» в кабинете.

## Как это работает

```
Браузер (форма «Оставить заявку»)
    │  POST /api/leads  {clubName, phone, email}
    ▼
AetherShell.Server — сохраняет заявку в базу
    ▲
    │  GET /api/leads?pendingOnly=true  (раз в POLL_SECONDS)
bot.py (Python, polling)
    │
    ▼
Telegram — сообщение с кнопками в TG_CHAT_ID
    │
    │  POST /api/leads/{id}/accept  (заголовок X-Bot-Secret)
    ▼
AetherShell.Server — создаёт клуб и шлёт письмо владельцу
```

Список заявок закрыт авторизацией, поэтому бот входит платформенным аккаунтом
(`AETHER_ADMIN_EMAIL` / `AETHER_ADMIN_PASSWORD`) и читает его по токену.

Нажатия кнопок подтверждаются не токеном, а общим секретом `TG_BOT_SECRET`:
такое же значение должно стоять у сервера. Если секрет не задан, кнопки будут
отвечать ошибкой, а заявки останутся необработанными.

Заявку без email автоматически принять нельзя — аккаунт владельца создавать не
на что, бот отметит это прямо в сообщении.

Об уже отправленных заявках бот помнит только в памяти: после перезапуска он
напомнит о необработанных ещё раз. Это дешевле, чем хранить состояние, и заявка
точно не потеряется.

## Создание бота через @BotFather

1. Откройте Telegram → найдите **@BotFather**.
2. Отправьте `/newbot`.
3. Введите имя бота, например: `AetherShell Заявки`.
4. Введите username (заканчивается на `bot`), например: `aethershell_leads_bot`.
5. Скопируйте **токен** из ответа — это `BOT_TOKEN`.

## Как узнать chat_id

- **Личные сообщения:** напишите **@userinfobot** — он вернёт ваш числовой ID.
- **Группа:** добавьте бота в группу, отправьте сообщение, откройте
  `https://api.telegram.org/bot<ТОКЕН>/getUpdates` и найдите `"chat": {"id": -100XXXXXXXXXX}`.

## Настройка

Скопируйте `.env.example` в `.env` и заполните:

```
BOT_TOKEN=123456789:AAHxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
TG_CHAT_ID=-1001234567890
SERVER_URL=http://server:5232
AETHER_ADMIN_EMAIL=admin@example.com
AETHER_ADMIN_PASSWORD=...
TG_BOT_SECRET=одинаковое-значение-с-сервером
POLL_SECONDS=60
```

## Запуск

Через Docker Compose бот поднимается вместе с сервером:

```bash
docker compose up -d
```

Отдельно:

```bash
cd telegram-bot
pip install -r requirements.txt
python bot.py
```

## Файлы

```
telegram-bot/
├── bot.py                  ← опрос заявок, кнопки и обработка решений
├── cloudflare-worker.js    ← необязательный прокси формы напрямую в Telegram
├── .env
├── requirements.txt
└── README.md
```
