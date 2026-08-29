using AetherShell.Server.Hubs;
using AetherShell.Server.Services;
using Microsoft.EntityFrameworkCore;
using AetherShell.Server.Data;
using AetherShell.Server.Middleware;
using AetherShell.Server.Models;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using ServerSettings = AetherShell.Server.Data.ServerSettings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Поддержка запуска как Windows Service
builder.Host.UseWindowsService();

// Подхватываем .env рядом с проектом/compose (не перезаписывает уже заданные переменные).
// В Docker compose уже инжектит env; это нужно для локального dotnet run и забытых экспортов.
LoadDotEnvFiles();

// ===== ЗАГРУЗКА НАСТРОЕК ИЗ server-settings.json =====
// Сначала ищем рядом с exe (папка публикации или bin\Debug\net10.0)
var baseDir = AppContext.BaseDirectory;
var serverSettingsPath = Path.Combine(baseDir, "server-settings.json");

// При запуске из Visual Studio exe лежит в bin\Debug\net10.0,
// а файл настроек обычно правят в папке проекта.
if (!File.Exists(serverSettingsPath))
{
    var projectDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));
    var altPath = Path.Combine(projectDir, "server-settings.json");
    if (File.Exists(altPath))
    {
        serverSettingsPath = altPath;
        Console.WriteLine($"[Config] Используется server-settings.json из папки проекта: {projectDir}");
    }
}

ServerSettings? serverSettings = null;
if (File.Exists(serverSettingsPath))
{
    try
    {
        var json = File.ReadAllText(serverSettingsPath);
        serverSettings = System.Text.Json.JsonSerializer.Deserialize<ServerSettings>(json);
        Console.WriteLine("[Config] Загружены настройки из server-settings.json: " + serverSettingsPath);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Config] Ошибка чтения server-settings.json: {ex.Message}");
    }
}

// ===== КОНФИГУРАЦИЯ =====
// Секреты живут только в переменных окружения и в server-settings.json (он в .gitignore),
// в appsettings.json их нет намеренно: этот файл лежит в репозитории.
var jwtSecret = Environment.GetEnvironmentVariable("SPIK_JWT_SECRET");
if (string.IsNullOrEmpty(jwtSecret)) jwtSecret = serverSettings?.JwtSecretKey;

if (string.IsNullOrEmpty(jwtSecret))
{
    Console.WriteLine("[Config] ОШИБКА: JWT-ключ не настроен.");
    Console.WriteLine("[Config] Задайте SPIK_JWT_SECRET или JwtSecretKey в server-settings.json.");
    Console.WriteLine("[Config] Пример: скопируйте server-settings.example.json → server-settings.json");
    Environment.Exit(1);
}

Console.WriteLine("[Config] JWT ключ: настроен");

string? dbConnection = Environment.GetEnvironmentVariable("SPIK_DB_CONNECTION");
if (!string.IsNullOrEmpty(dbConnection))
{
    Console.WriteLine("[Config] Подключение к БД: из переменной окружения");
}
else if (serverSettings != null && !string.IsNullOrEmpty(serverSettings.DbPassword))
{
    dbConnection = $"Host={serverSettings.DbHost};Port={serverSettings.DbPort};Database={serverSettings.DbName};Username={serverSettings.DbUser};Password={serverSettings.DbPassword}";
    Console.WriteLine("[Config] Подключение к БД: из server-settings.json");
}

if (string.IsNullOrEmpty(dbConnection))
{
    Console.WriteLine("[Config] ОШИБКА: строка подключения к БД не настроена.");
    Console.WriteLine("[Config] Задайте SPIK_DB_CONNECTION или DbPassword в server-settings.json.");
    Console.WriteLine("[Config] Пример: скопируйте server-settings.example.json → server-settings.json");
    Environment.Exit(1);
}

// ===== АДРЕСА ПРОСЛУШИВАНИЯ =====
// Задаём явно: иначе Kestrel садится на дефолтный 5000, и ServerPort из настроек ни на что не влиял.
var listenUrls = Environment.GetEnvironmentVariable("SPIK_URLS")
    ?? serverSettings?.Urls
    ?? "http://0.0.0.0:5232";
builder.WebHost.UseUrls(listenUrls.Split(',', StringSplitOptions.RemoveEmptyEntries)
    .Select(u => u.Trim()).ToArray());
Console.WriteLine($"[Config] Слушаем: {listenUrls}");

// ===== CORS =====
// Лендинг, кабинет и панель отдаются с того же origin, что и API, поэтому CORS
// нужен только для `npm run dev` админки. Список задаётся явно, без AllowAnyOrigin.
var corsOriginsRaw = Environment.GetEnvironmentVariable("SPIK_CORS_ORIGINS")
    ?? serverSettings?.CorsOrigins
    ?? "";
var corsOrigins = corsOriginsRaw
    .Split(',', StringSplitOptions.RemoveEmptyEntries)
    .Select(s => s.Trim())
    .Where(s => s.Length > 0)
    .Distinct()
    .ToArray();

if (corsOrigins.Length > 0)
    Console.WriteLine($"[Config] Дополнительные CORS origins: {string.Join(", ", corsOrigins)}");

// Сохраняем настройки для использования в других частях приложения
builder.Services.AddSingleton(serverSettings ?? new ServerSettings());

// За реверс-прокси нужны реальные схема и адрес клиента: иначе редиректы уходят на http,
// а rate limiting считает все запросы пришедшими с одного IP.
if (serverSettings?.BehindReverseProxy ?? true)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // Прокси свой, поэтому доверяем ему без списка разрешённых адресов.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();
builder.Services.AddScoped<EmailService>();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<TokenService>();

// ===== RATE LIMITING (защита от брутфорса) =====
var loginRateLimit = serverSettings?.LoginRateLimit ?? 5;
var apiRateLimit = serverSettings?.ApiRateLimit ?? 100;

builder.Services.AddRateLimiter(options =>
{
    // Лимит для логина
    options.AddFixedWindowLimiter("login", opt =>
    {
        opt.PermitLimit = loginRateLimit;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });
    
    // Общий лимит API
    options.AddFixedWindowLimiter("api", opt =>
    {
        opt.PermitLimit = apiRateLimit;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 2;
    });

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        await context.HttpContext.Response.WriteAsync("Слишком много запросов. Попробуйте позже.", token);
    };
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AetherWeb", policy =>
    {
        if (corsOrigins.Length == 0)
        {
            // Тот же origin — межсайтовые заголовки не нужны вовсе.
            return;
        }

        policy.WithOrigins(corsOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
    };

    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            Console.WriteLine($"[Auth Error]: {context.Exception.Message}");
            return Task.CompletedTask;
        },
        OnMessageReceived = context =>
        {
            var path = context.HttpContext.Request.Path;
            if (path.StartsWithSegments("/clubhub"))
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken))
                {
                    context.Token = accessToken;
                }
                else if (context.Request.Headers.ContainsKey("Authorization"))
                {
                    var authHeader = context.Request.Headers["Authorization"].ToString();
                    if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Token = authHeader.Substring("Bearer ".Length).Trim();
                    }
                }
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SignalRPolicy", policy =>
    {
        policy.RequireAuthenticatedUser();
    });
});

builder.Services.AddHostedService<SessionWorker>();
builder.Services.AddHostedService<ComputerStatusService>(); // Мониторинг статусов ПК
builder.Services.AddHostedService<LogCleanupService>();     // Очистка старых логов
builder.Services.AddHostedService<TelegramBotHostedService>(); // Python Telegram-бот заявок

// Клуб текущего запроса: заполняется ClubScopeMiddleware, по нему ClubDbContext
// выбирает базу данных клуба.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentClub, CurrentClub>();

// ===== БАЗЫ ДАННЫХ =====
// Платформенная база одна: реестр клубов, аккаунты владельцев, заявки.
builder.Services.AddDbContext<PlatformDbContext>(options =>
    options.UseNpgsql(dbConnection));

builder.Services.AddSingleton<IClubDbConnectionFactory>(new ClubDbConnectionFactory(dbConnection));
builder.Services.AddSingleton<IClubDbContextFactory, ClubDbContextFactory>();
builder.Services.AddSingleton<IClubRegistry, ClubRegistry>();
builder.Services.AddSingleton<SessionManager>();

// У каждого клуба своя база. Строка подключения собирается на каждый запрос по
// клубу из ClubScopeMiddleware. Если клуб не определён, подключение остаётся
// платформенным и никогда не открывается: RequireClub отклоняет такой запрос
// раньше первого обращения к таблицам.
builder.Services.AddDbContext<ClubDbContext>((sp, options) =>
{
    var connections = sp.GetRequiredService<IClubDbConnectionFactory>();
    var clubId = sp.GetRequiredService<ICurrentClub>().ClubId;

    options.UseNpgsql(clubId is int id
        ? connections.ConnectionStringFor(id)
        : connections.PlatformConnectionString);
});

builder.Services.AddScoped<ClubProvisioningService>();
builder.Services.AddScoped<BalanceNotifier>();
builder.Services.AddScoped<ClubRealtimeNotifier>();
builder.Services.AddSingleton<UploadStorage>();
builder.Services.AddScoped<AetherShell.Server.Services.AuditLogger>();

var app = builder.Build();

if (serverSettings?.BehindReverseProxy ?? true)
{
    // Первым в конвейере, чтобы всё дальше видело настоящие схему и IP клиента.
    app.UseForwardedHeaders();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(err => err.Run(async ctx =>
    {
        ctx.Response.StatusCode = 500;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync("{\"error\":\"Internal server error\"}");
    }));
}

var enableHttps = serverSettings?.EnableHttps ?? false;
if (!app.Environment.IsDevelopment() && enableHttps)
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

var enableSwagger = serverSettings?.EnableSwagger ?? false;
if (app.Environment.IsDevelopment() || enableSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ===== СТАТИКА С ОДНОГО ORIGIN =====
// wwwroot собирается при билде: лендинг и кабинет из AetherMain, панель из admin-panel/dist.
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.File.Name;
        // Кэш статики лендинга/панели: CSS/JS/картинки — долго, HTML — коротко.
        if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("robots.txt", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("sitemap.xml", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("llms.txt", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.CacheControl = "public,max-age=300";
        }
        else
        {
            // CSS/JS/картинки лендинга без хэша в имени — сутки; panel/* с хэшем тоже ок.
            ctx.Context.Response.Headers.CacheControl = "public,max-age=86400";
        }
    }
});

// Картинки, загруженные из панели. Лежат в томе с данными, а не в wwwroot,
// чтобы пересборка образа их не сносила.
var uploadStorage = app.Services.GetRequiredService<UploadStorage>();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadStorage.RootPath),
    RequestPath = UploadStorage.PublicPrefix,
    ServeUnknownFileTypes = false,
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl = "public,max-age=2592000";
    }
});

app.UseRouting();

app.UseCors("AetherWeb");

app.UseRateLimiter(); // Rate Limiting

app.UseAuthentication();

// Строго между аутентификацией и авторизацией: клуб определяется по claim-ам токена,
// а владельцу/платформенному админу здесь же выдаются роли Super/Senior/Admin —
// они должны существовать ДО проверки [Authorize(Roles = ...)] в UseAuthorization.
app.UseClubScope();

app.UseAuthorization();

app.MapControllers();

app.MapHub<ClubHub>("/clubhub")
   .RequireCors("AetherWeb")
   .RequireAuthorization("SignalRPolicy");

// ===== МАРШРУТЫ ВЕБ-ИНТЕРФЕЙСОВ =====
var webRoot = app.Environment.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");

// /kabinet — список клубов владельца или всей платформы (читаемый URL).
app.MapGet("/kabinet", () => ServeFile(Path.Combine(webRoot, "cabinet.html")));
// Старый адрес оставляем как редирект, чтобы закладки не ломались.
app.MapGet("/cabinet", () => Results.Redirect("/kabinet", permanent: false));

// Читаемые URL лендинга → тот же index.html (скролл по секции на клиенте).
foreach (var slug in new[] { "vozmozhnosti", "funkcii", "podklyuchit-klub" })
{
    var route = "/" + slug;
    app.MapGet(route, () => ServeFile(Path.Combine(webRoot, "index.html")));
}

app.MapGet("/resursy", () => ServeFile(Path.Combine(webRoot, "resursy.html")));

// /panel/{slug} и legacy /panel/klub/{id} — админ-панель клуба (SPA).
app.MapGet("/panel", () => ServeFile(Path.Combine(webRoot, "panel", "index.html")));
app.MapGet("/panel/klub/{clubId:int}", (int clubId) =>
    ServeFile(Path.Combine(webRoot, "panel", "index.html")));
app.MapGet("/panel/{slug}", (string slug) =>
    ServeFile(Path.Combine(webRoot, "panel", "index.html")));
app.MapFallback("/panel/{*path}", (string? path) =>
{
    // Реальные файлы уже отдал UseStaticFiles, сюда попадают только маршруты приложения.
    return ServeFile(Path.Combine(webRoot, "panel", "index.html"));
});

static IResult ServeFile(string path)
    => File.Exists(path)
        ? Results.File(path, "text/html")
        : Results.NotFound("Статика не собрана. Выполните сборку admin-panel и пересоберите сервер.");

// База от версии до разделения на клубные базы миграциями не обновляется:
// проверяем это до Migrate(), иначе он падает на существующем Accounts.
LegacyDatabaseGuard.EnsureNotLegacy(dbConnection);

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

    // Автоматически применяем миграции при запуске
    context.Database.Migrate();

    // Старым клубам (и временным club-{id}) выдаём читаемый адрес /panel/{slug}.
    var clubsToSlug = context.Clubs
        .Where(c => string.IsNullOrEmpty(c.Slug) || c.Slug.StartsWith("club-"))
        .OrderBy(c => c.Id)
        .ToList();
    if (clubsToSlug.Count > 0)
    {
        var taken = context.Clubs
            .Where(c => !string.IsNullOrEmpty(c.Slug) && !c.Slug.StartsWith("club-"))
            .Select(c => c.Slug)
            .ToList();
        foreach (var club in clubsToSlug)
        {
            club.Slug = ClubSlug.EnsureUnique(ClubSlug.FromName(club.Name), taken);
            taken.Add(club.Slug);
        }
        context.SaveChanges();
    }

    // ===== БАЗЫ КЛУБОВ =====
    // Схема клубной базы обновляется отдельно от платформенной, поэтому после
    // выхода новой версии сервера каждый клуб мигрируется на старте.
    // Если запись Clubs есть, а PostgreSQL-базы нет (сбросили том / сбой при создании) —
    // создаём её здесь, иначе любой запрос к клубу падает с 3D000.
    var clubDbFactory = scope.ServiceProvider.GetRequiredService<IClubDbContextFactory>();
    var provisioning = scope.ServiceProvider.GetRequiredService<ClubProvisioningService>();
    foreach (var clubId in context.Clubs.Select(c => c.Id).ToList())
    {
        try
        {
            await provisioning.EnsureDatabaseAsync(clubId);
            using var clubDb = clubDbFactory.Create(clubId);
            clubDb.Database.Migrate();

            // Сбрасываем статус Online: соединения не переживают перезапуск сервера.
            var stuck = clubDb.Computers.Where(c => c.IsOnline).ToList();
            if (stuck.Count > 0)
            {
                foreach (var pc in stuck)
                {
                    pc.IsOnline = false;
                    pc.Status = ComputerStatus.Offline;
                    pc.CurrentApp = null;
                    pc.CurrentAppTitle = null;
                    pc.CurrentAppSince = null;
                }
                clubDb.SaveChanges();
            }
        }
        catch (Exception ex)
        {
            // Сервер должен подняться даже если одна клубная база сломана:
            // остальные клубы будут работать, а этот покажет ошибку в панели.
            Console.WriteLine($"[System] Клуб {clubId}: не удалось подготовить базу — {ex.Message}");
        }
    }

    // ===== ПЛАТФОРМЕННЫЙ АДМИНИСТРАТОР =====
    // Это твой аккаунт владельца шелла: вход в /kabinet. Берём email/пароль из env,
    // иначе из server-settings.json (локальный запуск без Docker).
    var platformAdminEmail =
        FirstNonEmpty(
            Environment.GetEnvironmentVariable("AETHER_ADMIN_EMAIL"),
            serverSettings?.AdminEmail);
    var platformAdminPassword =
        FirstNonEmpty(
            Environment.GetEnvironmentVariable("AETHER_ADMIN_PASSWORD"),
            serverSettings?.AdminPassword);

    if (!string.IsNullOrWhiteSpace(platformAdminEmail) && !string.IsNullOrWhiteSpace(platformAdminPassword))
    {
        var normalizedEmail = platformAdminEmail.Trim().ToLowerInvariant();
        var existing = context.Accounts.FirstOrDefault(a => a.Email == normalizedEmail);

        if (existing == null)
        {
            context.Accounts.Add(new Account
            {
                Email = normalizedEmail,
                PasswordHash = PasswordHasher.Hash(platformAdminPassword),
                Role = AccountRoles.PlatformAdmin,
                DisplayName = "Администратор платформы",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            context.SaveChanges();
            Console.WriteLine($"[System] Создан владелец шелла (PlatformAdmin): {normalizedEmail}");
            Console.WriteLine("[System] Вход: /kabinet  (не /panel — панель только для клуба)");
        }
        else
        {
            var changed = false;
            if (existing.Role != AccountRoles.PlatformAdmin)
            {
                existing.Role = AccountRoles.PlatformAdmin;
                changed = true;
                Console.WriteLine($"[System] Аккаунт {normalizedEmail} повышен до PlatformAdmin");
            }

            // Пароль из .env / настроек всегда актуален: иначе после смены в .env
            // «учётку не создало» выглядит как «не пускает».
            if (!PasswordHasher.Verify(platformAdminPassword, existing.PasswordHash))
            {
                existing.PasswordHash = PasswordHasher.Hash(platformAdminPassword);
                existing.MustChangePassword = false;
                existing.IsActive = true;
                changed = true;
                Console.WriteLine($"[System] Пароль владельца {normalizedEmail} синхронизирован из AETHER_ADMIN_PASSWORD");
            }

            if (!existing.IsActive)
            {
                existing.IsActive = true;
                changed = true;
            }

            if (changed)
                context.SaveChanges();
            else
                Console.WriteLine($"[System] Владелец шелла уже есть: {normalizedEmail}");
        }
    }
    else if (!context.Accounts.Any())
    {
        Console.WriteLine("[System] ВНИМАНИЕ: аккаунтов нет. Задайте AETHER_ADMIN_EMAIL и AETHER_ADMIN_PASSWORD в .env (или AdminEmail/AdminPassword в server-settings.json) и перезапустите сервер.");
    }
}

static string? FirstNonEmpty(params string?[] values)
    => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

/// <summary>
/// Простой парсер .env: KEY=VALUE, без экспорта уже заданных переменных.
/// Ищем в текущей папке, рядом с exe и на 1–3 уровня выше (корень compose).
/// </summary>
static void LoadDotEnvFiles()
{
    var candidates = new List<string>();
    void add(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var full = Path.GetFullPath(path);
            if (File.Exists(full) && !candidates.Contains(full, StringComparer.OrdinalIgnoreCase))
                candidates.Add(full);
        }
        catch { /* ignore */ }
    }

    add(Path.Combine(Directory.GetCurrentDirectory(), ".env"));
    add(Path.Combine(AppContext.BaseDirectory, ".env"));

    var walk = AppContext.BaseDirectory;
    for (var i = 0; i < 5 && !string.IsNullOrEmpty(walk); i++)
    {
        add(Path.Combine(walk, ".env"));
        walk = Directory.GetParent(walk)?.FullName;
    }

    foreach (var file in candidates)
    {
        try
        {
            var loaded = 0;
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#') || !line.Contains('=')) continue;
                var idx = line.IndexOf('=');
                var key = line[..idx].Trim().TrimStart('\uFEFF');
                var value = line[(idx + 1)..].Trim().Trim('"').Trim('\'');
                if (key.Length == 0) continue;
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key))) continue;
                Environment.SetEnvironmentVariable(key, value);
                loaded++;
            }
            if (loaded > 0)
                Console.WriteLine($"[Config] Подхвачен .env ({loaded} переменных): {file}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Не удалось прочитать {file}: {ex.Message}");
        }
    }
}

app.Run();