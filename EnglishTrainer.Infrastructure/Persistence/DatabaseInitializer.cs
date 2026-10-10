using EnglishTrainer.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnglishTrainer.Infrastructure.Persistence;

/// <summary>
/// Приведение базы в рабочее состояние при запуске приложения.
/// </summary>
/// <remarks>
/// <para>
/// Вынесено из <c>Program.cs</c> отдельным типом, потому что это не «настройка DI»,
/// а прикладная операция: применить миграции и, если словарь пуст, загрузить его.
/// Её видно в логах, её можно вызвать вручную и её легко заменить
/// на отдельный шаг развёртывания, когда появится настоящий CI.
/// </para>
/// <para>
/// Автоматические миграции при старте — сознательный выбор для учебного проекта,
/// где развёртывание делает один человек. В продукте с несколькими экземплярами
/// приложения миграции выполняет отдельный шаг перед выкаткой: одновременный
/// запуск нескольких инстансов может привести к гонке за схему.
/// </para>
/// </remarks>
public sealed class DatabaseInitializer(
    AppDbContext context,
    VocabularySeeder seeder,
    ILogger<DatabaseInitializer> logger)
{
    /// <summary>Применяет миграции. Возвращает список применённых к базе миграций.</summary>
    /// <remarks>
    /// Сначала применяем, потом читаем список. Обратный порядок не работает
    /// на пустой базе: таблицы <c>__EFMigrationsHistory</c> ещё не существует,
    /// а запрос к ней — единственный способ узнать применённые миграции.
    /// Создаёт эту таблицу сам <c>Migrate</c>.
    /// </remarks>
    public async Task<IReadOnlyList<string>> MigrateAsync(CancellationToken ct = default)
    {
        await context.Database.MigrateAsync(ct);

        var applied = (await context.Database.GetAppliedMigrationsAsync(ct)).ToList();

        logger.LogInformation("Схема базы актуальна, всего применено миграций: {Count}.", applied.Count);
        return applied;
    }

    /// <summary>Загружает словарь, если в базе ещё нет ни одного слова.</summary>
    public async Task<VocabularySeedResult?> SeedIfEmptyAsync(CancellationToken ct = default)
    {
        if (await context.Words.AnyAsync(ct))
        {
            logger.LogInformation("Словарь уже загружен, импорт пропущен.");
            return null;
        }

        return await seeder.SeedAsync(ct);
    }

    /// <summary>
    /// Загружает словарь принудительно. Нужен после правки файлов словаря:
    /// импорт идемпотентен, поэтому повторный прогон безопасен.
    /// </summary>
    public Task<VocabularySeedResult> SeedAsync(CancellationToken ct = default) => seeder.SeedAsync(ct);
}
