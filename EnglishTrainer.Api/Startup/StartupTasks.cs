using System.Data.Common;
using System.Text.RegularExpressions;
using EnglishTrainer.Infrastructure;
using EnglishTrainer.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

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

        if (configuration.GetValue(SeedOnStartupKey, false))
        {
            await initializer.SeedIfEmptyAsync();
        }
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
