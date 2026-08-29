/**
 * Cloudflare Worker — прокси для отправки заявок в Telegram.
 *
 * Альтернатива серверному bot.py (только отправка, без кнопок).
 * Если нужны кнопки «Принять/Отклонить», всё равно понадобится bot.py.
 *
 * Настройка:
 *   1. Создайте Worker на dash.cloudflare.com
 *   2. Добавьте переменные окружения (Settings → Variables):
 *      BOT_TOKEN  — токен бота
 *      CHAT_ID    — ваш chat_id
 *   3. Вставьте этот код и нажмите Deploy
 *   4. На фронтенде укажите URL воркера как PROXY_URL
 */

export default {
  async fetch(request, env) {
    if (request.method === "OPTIONS") {
      return new Response(null, {
        status: 204,
        headers: corsHeaders(),
      });
    }

    if (request.method !== "POST") {
      return jsonResponse({ ok: false, error: "method not allowed" }, 405);
    }

    let data;
    try {
      data = await request.json();
    } catch {
      return jsonResponse({ ok: false, error: "invalid json" }, 400);
    }

    const club = String(data.club || "").trim();
    const phone = String(data.phone || "").trim();
    const email = String(data.email || "").trim();

    if (!club) return jsonResponse({ ok: false, error: "club is required" }, 400);
    if (!phone && !email) return jsonResponse({ ok: false, error: "phone or email required" }, 400);

    const now = new Date().toLocaleString("ru-RU", { timeZone: "Europe/Moscow" });
    const text = [
      "🔔 <b>Новая заявка AetherShell</b>",
      "",
      `🏢 <b>Клуб:</b> ${escapeHtml(club)}`,
      `📞 <b>Телефон:</b> ${escapeHtml(phone) || "—"}`,
      `📧 <b>Почта:</b> ${escapeHtml(email) || "—"}`,
      "",
      `🕐 ${now}`,
    ].join("\n");

    const tgRes = await fetch(
      `https://api.telegram.org/bot${env.BOT_TOKEN}/sendMessage`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          chat_id: env.CHAT_ID,
          text,
          parse_mode: "HTML",
        }),
      }
    );

    if (!tgRes.ok) {
      const err = await tgRes.text();
      return jsonResponse({ ok: false, error: err }, 502);
    }

    return jsonResponse({ ok: true });
  },
};

function corsHeaders() {
  return {
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Headers": "Content-Type",
    "Access-Control-Allow-Methods": "POST, OPTIONS",
  };
}

function jsonResponse(obj, status = 200) {
  return new Response(JSON.stringify(obj), {
    status,
    headers: {
      "Content-Type": "application/json",
      ...corsHeaders(),
    },
  });
}

function escapeHtml(s) {
  return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}
