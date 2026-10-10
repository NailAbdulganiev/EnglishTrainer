using EnglishTrainer.Application.Services;
using EnglishTrainer.Domain.Exceptions;
using EnglishTrainer.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnglishTrainer.Api.Cli;

/// <summary>
/// Команды обслуживания: миграции, словарь и сброс пароля — без запуска веб-сервера.
/// </summary>
/// <remarks>
/// Нужны потому, что автоматические миграции при старте удобны в разработке,
/// но не годятся для развёртывания: там схему обновляют отдельным шагом,
/// до того как новые экземпляры приложения начнут принимать трафик.
/// <code>
/// dotnet run --project EnglishTrainer.Api -- migrate
/// dotnet run --project EnglishTrainer.Api -- seed
/// dotnet run --project EnglishTrainer.Api -- reset-password user@example.com
/// </code>
/// </remarks>
internal static class MaintenanceCommands
{
    private const string MigrateCommand = "migrate";
    private const string SeedCommand = "seed";
    private const string ResetPasswordCommand = "reset-password";

    /// <summary>Запрошена ли команда обслуживания вместо запуска веб-сервера.</summary>
    public static bool IsMaintenanceCommand(string[] args) =>
        args.Any(argument =>
            Matches(argument, MigrateCommand) ||
            Matches(argument, SeedCommand) ||
            Matches(argument, ResetPasswordCommand));

    public static async Task<int> ExecuteAsync(string[] args, IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
        var passwordReset = scope.ServiceProvider.GetRequiredService<PasswordResetService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(MaintenanceCommands));

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index].ToLowerInvariant())
            {
                case MigrateCommand:
                    var applied = await initializer.MigrateAsync();
                    logger.LogInformation(
                        applied.Count == 0 ? "Схема базы уже актуальна." : "Применено миграций: {Count}.", applied.Count);
                    break;

                case SeedCommand:
                    var result = await initializer.SeedAsync();
                    logger.LogInformation(
                        "Словарь обработан: колод {Decks}, новых слов {Added}, дополнено {Updated}, " +
                        "переводов {Translations}, примеров {Examples}.",
                        result.Decks, result.WordsAdded, result.WordsUpdated,
                        result.TranslationsAdded, result.ExamplesAdded);
                    break;

                case ResetPasswordCommand:
                    // Следующий аргумент — логин, поэтому он пропускается в цикле.
                    var login = index + 1 < args.Length ? args[index + 1] : null;
                    index++;
                    await ResetPasswordAsync(passwordReset, login);
                    break;
            }
        }

        return 0;
    }

    /// <summary>
    /// Сбрасывает пароль по логину.
    /// </summary>
    /// <remarks>
    /// Последний способ вернуть доступ, когда почтового провайдера нет: сервер
    /// стоит на машине владельца, и он же может задать новый пароль. Пароль
    /// запрашивается дважды, чтобы опечатка не закрыла аккаунт второй раз.
    /// </remarks>
    private static async Task ResetPasswordAsync(PasswordResetService service, string? login)
    {
        if (string.IsNullOrWhiteSpace(login))
        {
            Console.WriteLine("Укажите email или ник: reset-password <email-или-ник>");
            return;
        }

        Console.WriteLine($"Сброс пароля для «{login}». Ввод виден на экране — это локальный сброс.");
        Console.Write("Новый пароль: ");
        var password = Console.ReadLine();

        Console.Write("Повторите пароль: ");
        var repeated = Console.ReadLine();

        if (!string.Equals(password, repeated, StringComparison.Ordinal))
        {
            Console.WriteLine("Пароли не совпадают, ничего не изменено.");
            return;
        }

        try
        {
            var changed = await service.SetPasswordAsync(login, password ?? string.Empty);

            Console.WriteLine(changed
                ? "Пароль изменён. Войдите с новым паролем."
                : $"Пользователь «{login}» не найден.");
        }
        catch (AppException ex)
        {
            // Ожидаемая ошибка: например, пароль короче минимального.
            Console.WriteLine(ex.Message);
        }
    }

    private static bool Matches(string argument, string command) =>
        string.Equals(argument, command, StringComparison.OrdinalIgnoreCase);
}
