using System.Data.Common;
using System.Text.RegularExpressions;
using EnglishTrainer.Infrastructure;
using EnglishTrainer.Infrastructure.Persistence;
using EnglishTrainer.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EnglishTrainer.Api.Startup;

/// <summary>
/// Действия при запуске: привести базу в рабочее состояние.
/// </summary>
/// <remarks>
/// Поведение управляется конфигурацией, а не кодом: в <c>appsettings.Development.json</c>
/// миграции и импорт словаря включены, в рабочей конфигурации — выключены.
/// Так «просто запустить и посмотреть» работает сразу, а развёртывание
/// не зависит от того, кто первым успел стартовать.
/// </remarks>
internal static partial class StartupTasks
{
    private const string AutoMigrateKey = "Database:AutoMigrate";
    private const string SeedOnStartupKey = "Database:SeedOnStartup";

    public static async Task RunAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(StartupTasks));

        if (configuration.GetValue(AutoMigrateKey, false))
        {
            await MigrateAsync(initializer, configuration, logger);
        }
        else
        {
            logger.LogInformation(
                "Автоматическое применение миграций отключено ({Key}=false). " +
                "Примените их командой: dotnet run --project EnglishTrainer.Api -- migrate",
                AutoMigrateKey);
        }

        ReportEmail(app, logger);

        if (configuration.GetValue(SeedOnStartupKey, false))
        {
            await initializer.SeedIfEmptyAsync();
        }
    }

    /// <summary>
    /// Пишет в журнал, куда уходят письма.
    /// </summary>
    /// <remarks>
    /// Половина настройки почты не даёт ни ошибки, ни предупреждения: письмо
    /// просто уходит в журнал, и в развёртывании это выглядит как «почта
    /// настроена, а письма не приходят». Одна строка при запуске снимает этот
    /// вопрос: видно и адрес отправителя, и то, что отправка выключена.
    /// </remarks>
    private static void ReportEmail(WebApplication app, ILogger logger)
    {
        var email = app.Services.GetRequiredService<IOptions<EmailOptions>>().Value;

        if (!email.IsConfigured)
        {
            logger.LogWarning(
                "Почта не настроена: письмо со ссылкой для сброса пароля будет выводиться в журнал. " +
                "Для отправки задайте Email:Provider (smtp или brevo), адрес отправителя Email:From " +
                "и ключ — Email:Smtp:Host с паролем либо Email:Brevo:ApiKey.");

            return;
        }

        if (email.UsesBrevo)
        {
            // Провайдер называется в журнале явно: «почта настроена, а письма
            // не приходят» чаще всего означает, что работает не тот способ,
            // который имел в виду человек.
            logger.LogInformation(
                "Почта: Brevo (HTTP API), отправитель {From}. Таймаут {Timeout} с.",
                email.From,
                email.TimeoutSeconds);

            return;
        }

        logger.LogInformation(
            "Почта: {Host}:{Port} (SMTP), отправитель {From}. Таймаут {Timeout} с.",
            email.Smtp.Host,
            email.Smtp.Port,
            email.From,
            email.TimeoutSeconds);
    }

    /// <summary>
    /// Применяет миграции, объясняя причину, если база недоступна.
    /// </summary>
    /// <remarks>
    /// Самая частая ошибка при первом запуске — строка подключения указывает
    /// на другой порт или сервер базы ещё не поднят. Голое «connection refused»
    /// в этом случае ничего не подсказывает: непонятно ни куда приложение
    /// стучалось, ни что с этим делать. Поэтому к ошибке добавляются адрес базы
    /// (без пароля) и два способа её исправить.
    /// </remarks>
    private static async Task MigrateAsync(
        DatabaseInitializer initializer, IConfiguration configuration, ILogger logger)
    {
        var target = MaskPassword(
            configuration.GetConnectionString(InfrastructureServiceCollectionExtensions.ConnectionStringName));

        try
        {
            logger.LogInformation("База данных: {Target}", target);
            await initializer.MigrateAsync();
        }
        catch (DbException ex)
        {
            logger.LogCritical(
                ex,
                "Не удалось подключиться к базе данных ({Target}). Проверьте, что сервер PostgreSQL запущен " +
                "и что строка подключения верна. База из docker-compose.yml поднимается командой " +
                "'docker compose up -d'; своя база задаётся в ConnectionStrings:Postgres — " +
                "через user-secrets или переменную окружения ConnectionStrings__Postgres.",
                target);

            // Приложение не должно подниматься без базы: иначе оно отвечает
            // ошибкой на каждый запрос, и причина теряется среди логов.
            throw;
        }
    }

    /// <summary>Убирает пароль из строки подключения перед записью в лог.</summary>
    private static string MaskPassword(string? connectionString) =>
        string.IsNullOrWhiteSpace(connectionString)
            ? "(строка подключения не задана)"
            : PasswordPattern().Replace(connectionString, "$1=***");

    [GeneratedRegex("(?i)(password|pwd)\\s*=\\s*[^;]*")]
    private static partial Regex PasswordPattern();
}
