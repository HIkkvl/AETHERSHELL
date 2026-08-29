"""
AetherShell Telegram Bot — заявки с лендинга прямо в чат.

Бот опрашивает новые заявки и присылает каждую с кнопками «Принять» и
«Отклонить». По нажатию сервер создаёт клуб с отдельной базой, аккаунт
владельца и письмо с доступом — заходить в кабинет для этого не нужно.
"""

import asyncio
import logging
import os
from datetime import datetime, timezone

import aiohttp
from aiogram import Bot, Dispatcher, F
from aiogram.client.default import DefaultBotProperties
from aiogram.enums import ParseMode
from aiogram.types import CallbackQuery, InlineKeyboardButton, InlineKeyboardMarkup
from dotenv import load_dotenv

load_dotenv()

BOT_TOKEN = (os.getenv("BOT_TOKEN") or os.getenv("TG_BOT_TOKEN") or "").strip()
CHAT_ID = (os.getenv("TG_CHAT_ID") or "").strip()
SERVER_URL = os.getenv("SERVER_URL", "http://localhost:5232").rstrip("/")
ADMIN_EMAIL = os.getenv("AETHER_ADMIN_EMAIL", "")
ADMIN_PASSWORD = os.getenv("AETHER_ADMIN_PASSWORD", "")
BOT_SECRET = os.getenv("TG_BOT_SECRET", "")
POLL_SECONDS = int(os.getenv("POLL_SECONDS", "60"))

logging.basicConfig(level=logging.INFO, format="%(asctime)s  %(message)s")
log = logging.getLogger(__name__)

bot: Bot | None = None
dp = Dispatcher()
if BOT_TOKEN:
    bot = Bot(token=BOT_TOKEN, default=DefaultBotProperties(parse_mode=ParseMode.HTML))

# Заявки, о которых уже сообщили. При перезапуске бот напомнит о необработанных
# ещё раз — это дешевле, чем хранить состояние.
_notified: set[int] = set()

# Одна сессия на весь процесс: её же используют и опрос, и обработчики кнопок.
_session: aiohttp.ClientSession | None = None


def _bot_headers() -> dict[str, str]:
    return {"X-Bot-Secret": BOT_SECRET} if BOT_SECRET else {}


async def _login(session: aiohttp.ClientSession) -> str | None:
    """Токен платформенного администратора: список заявок закрыт авторизацией."""
    if not ADMIN_EMAIL or not ADMIN_PASSWORD:
        log.error("AETHER_ADMIN_EMAIL и AETHER_ADMIN_PASSWORD не заданы — заявки читать нечем")
        return None

    try:
        async with session.post(
            f"{SERVER_URL}/api/account/login",
            json={"email": ADMIN_EMAIL, "password": ADMIN_PASSWORD},
            timeout=aiohttp.ClientTimeout(total=15),
        ) as resp:
            data = await resp.json(content_type=None)
            if resp.status != 200:
                log.error("Не удалось войти: %s %s", resp.status, data)
                return None
            return data.get("token")
    except Exception as e:
        log.error("Ошибка входа на сервер: %s", e)
        return None


async def _fetch_leads(session: aiohttp.ClientSession, token: str) -> list[dict] | None:
    try:
        async with session.get(
            f"{SERVER_URL}/api/leads?pendingOnly=true",
            headers={"Authorization": f"Bearer {token}"},
            timeout=aiohttp.ClientTimeout(total=15),
        ) as resp:
            if resp.status == 401:
                return None  # токен истёк, вызывающий перелогинится
            if resp.status != 200:
                log.error("Сервер вернул %s на список заявок", resp.status)
                return []
            return await resp.json(content_type=None)
    except Exception as e:
        log.error("Ошибка запроса заявок: %s", e)
        return []


async def _remember_message(lead_id: int, message_id: int) -> None:
    """Сохраняем id сообщения, чтобы после перезапуска бот знал, что уже отправлял."""
    if _session is None:
        return
    try:
        async with _session.patch(
            f"{SERVER_URL}/api/leads/{lead_id}/message",
            json={"messageId": message_id},
            headers=_bot_headers(),
            timeout=aiohttp.ClientTimeout(total=15),
        ) as resp:
            if resp.status != 200:
                log.warning("Не удалось сохранить messageId заявки %s: %s", lead_id, resp.status)
    except Exception as e:
        log.warning("Ошибка сохранения messageId заявки %s: %s", lead_id, e)


async def _post_decision(lead_id: int, action: str) -> tuple[bool, str]:
    """Отправляет решение по заявке. Возвращает признак успеха и текст для чата."""
    if _session is None:
        return False, "Бот ещё не готов, попробуйте ещё раз"

    if not BOT_SECRET:
        return False, "TG_BOT_SECRET не задан — сервер не примет команду от бота"

    try:
        async with _session.post(
            f"{SERVER_URL}/api/leads/{lead_id}/{action}",
            headers=_bot_headers(),
            timeout=aiohttp.ClientTimeout(total=60),
        ) as resp:
            data = await resp.json(content_type=None) or {}

            if resp.status == 200:
                if action == "accept":
                    club = data.get("clubName") or "клуб"
                    mail = "письмо отправлено" if data.get("emailSent") else "письмо не отправлено"
                    return True, f"клуб «{club}» создан, {mail}"
                return True, "заявка отклонена"

            if resp.status == 403:
                return False, "сервер отклонил секрет бота"

            return False, str(data.get("error") or f"сервер вернул {resp.status}")
    except Exception as e:
        log.error("Ошибка обработки заявки %s: %s", lead_id, e)
        return False, "сервер недоступен"


def _keyboard(lead_id: int) -> InlineKeyboardMarkup:
    return InlineKeyboardMarkup(inline_keyboard=[[
        InlineKeyboardButton(text="✅ Принять", callback_data=f"lead:accept:{lead_id}"),
        InlineKeyboardButton(text="✖️ Отклонить", callback_data=f"lead:reject:{lead_id}"),
    ]])


def _format(lead: dict) -> str:
    lines = [
        f"🆕 <b>Заявка #{lead['id']}</b>",
        f"Клуб: <b>{lead.get('clubName') or '—'}</b>",
    ]
    if lead.get("phone"):
        lines.append(f"Телефон: {lead['phone']}")
    if lead.get("email"):
        lines.append(f"Email: {lead['email']}")
    if lead.get("comment"):
        lines.append(f"Комментарий: {lead['comment']}")

    if not lead.get("email"):
        lines.append("")
        lines.append("⚠️ Без email клуб автоматически не создать — нужен кабинет.")

    return "\n".join(lines)


@dp.callback_query(F.data.startswith("lead:"))
async def on_decision(callback: CallbackQuery) -> None:
    _, action, raw_id = callback.data.split(":", 2)
    lead_id = int(raw_id)

    await callback.answer("Обрабатываю...")

    ok, detail = await _post_decision(lead_id, action)

    who = callback.from_user.full_name or callback.from_user.username or "администратор"
    when = datetime.now(timezone.utc).astimezone().strftime("%d.%m.%Y %H:%M")
    original = callback.message.html_text if callback.message else ""

    if ok:
        verdict = "✅ Принята" if action == "accept" else "✖️ Отклонена"
        footer = f"{verdict} — {who}, {when}\n{detail}"
        markup = None
    else:
        footer = f"⚠️ Не удалось обработать: {detail}"
        markup = _keyboard(lead_id)

    try:
        await callback.message.edit_text(f"{original}\n\n{footer}", reply_markup=markup)
    except Exception as e:
        log.warning("Не удалось отредактировать сообщение заявки %s: %s", lead_id, e)


async def poll_leads() -> None:
    log.info("Опрос заявок каждые %d с, сервер %s", POLL_SECONDS, SERVER_URL)

    token: str | None = None

    while True:
        try:
            if token is None:
                token = await _login(_session)

            if token is not None:
                leads = await _fetch_leads(_session, token)

                if leads is None:
                    token = None  # перелогинимся на следующем круге
                else:
                    for lead in leads:
                        if lead["id"] in _notified:
                            continue
                        try:
                            assert bot is not None
                            message = await bot.send_message(
                                CHAT_ID, _format(lead), reply_markup=_keyboard(lead["id"])
                            )
                            _notified.add(lead["id"])
                            await _remember_message(lead["id"], message.message_id)
                        except Exception as e:
                            log.error("Не удалось отправить сообщение: %s", e)
        except Exception as e:
            log.error("Сбой в цикле опроса: %s", e)

        await asyncio.sleep(POLL_SECONDS)


async def main() -> None:
    global _session

    if not BOT_TOKEN or not CHAT_ID:
        log.error("BOT_TOKEN/TG_BOT_TOKEN или TG_CHAT_ID не заданы — бот ждёт настройки")
        while True:
            await asyncio.sleep(3600)

    assert bot is not None

    if not BOT_SECRET:
        log.warning("TG_BOT_SECRET не задан: кнопки будут отвечать ошибкой")

    async with aiohttp.ClientSession() as session:
        _session = session

        poller = asyncio.create_task(poll_leads())
        try:
            await dp.start_polling(bot)
        finally:
            poller.cancel()


if __name__ == "__main__":
    asyncio.run(main())
