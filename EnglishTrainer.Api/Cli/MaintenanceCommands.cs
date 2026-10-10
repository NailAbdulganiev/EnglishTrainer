using EnglishTrainer.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnglishTrainer.Api.Cli;

/// <summary>
/// Команды обслуживания: применить миграции или загрузить словарь и выйти.
/// </summary>
/// <remarks>
/// Нужны потому, что автоматические миграции при старте удобны в разработке,
/// но не годятся для развёртывания: там схему обновляют отдельным шагом,
/// до того как новые экземпляры приложения начнут принимать трафик.
/// <code>
/// dotnet run --project EnglishTrainer.Api -- migrate
/// dotnet run --project EnglishTrainer.Api -- seed
/// </code>
/// </remarks>
internal static class MaintenanceCommands
{
    private const string MigrateCommand = "migrate";
    private const string SeedCommand = "seed";

    /// <summary>Запрошена ли команда обслуживания вместо запуска веб-сервера.</summary>
    public static bool IsMaintenanceCommand(string[] args) =>
        args.Any(argument =>
            string.Equals(argument, MigrateCommand, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(argument, SeedCommand, StringComparison.OrdinalIgnoreCase));

    public static async Task<int> ExecuteAsync(string[] args, IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(MaintenanceCommands));

        foreach (var argument in args)
        {
            switch (argument.ToLowerInvariant())
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
            }
        }

        return 0;
    }
}
