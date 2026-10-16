using EnglishTrainer.Application;
using EnglishTrainer.Application.Telegram;
using EnglishTrainer.Bot;
using EnglishTrainer.Bot.Telegram;
using EnglishTrainer.Infrastructure;
using EnglishTrainer.Infrastructure.DependencyInjection;

// ── Хост ─────────────────────────────────────────────────────────────────────
// Каталог содержимого задан явно. По умолчанию им становится текущий каталог
// процесса, то есть место, откуда набрали команду, — и appsettings.json
// находился бы или не находился в зависимости от того, где открыт терминал.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

// Локальные секреты — тот же файл в корне решения, что читает API: токен бота
// и строка подключения задаются один раз на оба процесса.
LocalSettings.AddLocalOverrides(builder.Configuration, builder.Environment.ContentRootPath);

// ── Логирование ──────────────────────────────────────────────────────────────
// Провайдеры заданы явно, как и в API: приёмник Event Log на Windows пишет
// в системный журнал, и без прав на запись попытка залогировать ошибку сама
// становится исключением.
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = false;
    options.TimestampFormat = "HH:mm:ss ";
});

// ── Слои приложения ──────────────────────────────────────────────────────────
// Те же Domain, Application и Infrastructure, что и у API. Бот — такой же
// клиент приложения, как HTTP-слой: правила обучения живут в одном месте,
// и второй их набор здесь не появляется.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);

var telegram = builder.Configuration.GetSection(TelegramOptions.SectionName).Get<TelegramOptions>()
    ?? new TelegramOptions();

// ── Клиент Bot API ───────────────────────────────────────────────────────────
// Регистрируется всегда, даже без токена: воркеры получают его в конструкторе,
// и без регистрации хост падал бы на старте с невнятной ошибкой разрешения
// зависимостей вместо понятного «токен не задан» в журнале.
builder.Services.AddHttpClient<ITelegramApi, TelegramApiClient>(client =>
{
    client.BaseAddress = new Uri(telegram.HasToken
        ? $"https://api.telegram.org/bot{telegram.BotToken}/"
        : "https://api.telegram.org/");

    // Таймаут с запасом к времени ожидания long polling: иначе клиент обрывал бы
    // соединение раньше, чем Telegram успел бы ответить, и опрос превратился бы
    // в бесконечный цикл пустых запросов.
    client.Timeout = TimeSpan.FromSeconds(telegram.PollTimeoutSeconds + 60);
});

// ── Обработчики ──────────────────────────────────────────────────────────────
// Область видимости на обновление создаёт сам воркер: обработчик работает
// с контекстом EF Core, а он не рассчитан на долгую жизнь.
builder.Services.AddScoped<TelegramBotService>();
builder.Services.AddHostedService<TelegramPollingWorker>();
builder.Services.AddHostedService<TelegramReminderWorker>();

await builder.Build().RunAsync();

return 0;
