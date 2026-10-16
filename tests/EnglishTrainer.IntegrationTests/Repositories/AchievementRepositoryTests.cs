using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Infrastructure.Repositories;
using EnglishTrainer.IntegrationTests.Database;

namespace EnglishTrainer.IntegrationTests.Repositories;

/// <summary>
/// Достижения: выдача награды и её идемпотентность.
/// </summary>
/// <remarks>
/// Проверка идёт после каждого открытия страницы, и два одновременных запроса —
/// например, браузер и мини-апп — это обычное дело, а не редкость. Обычная
/// вставка в такой ситуации дала бы нарушение уникального индекса, то есть
/// ошибку 500 на ровном месте. Поэтому выдача идёт через ON CONFLICT DO NOTHING,
/// и это единственное место в проекте, где от идемпотентности зависит не
/// корректность данных, а отсутствие падения.
/// </remarks>
public sealed class AchievementRepositoryTests
{
    [Fact]
    public async Task UnlockAsync_StoresAchievementWithDate()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new AchievementRepository(scope.Context);

        var user = await TestData.AddUserAsync(scope.Context);
        var at = DateTime.UtcNow;

        var unlocked = await repository.UnlockAsync(user.Id, ["first-step", "words-10"], at);

        Assert.Equal(2, unlocked);

        var stored = await repository.GetAsync(user.Id);
        Assert.Equal(2, stored.Count);
        Assert.All(stored, achievement => Assert.Equal(at, achievement.UnlockedAt, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task UnlockAsync_CalledTwice_UnlocksOnce()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new AchievementRepository(scope.Context);

        var user = await TestData.AddUserAsync(scope.Context);

        Assert.Equal(1, await repository.UnlockAsync(user.Id, ["first-step"], DateTime.UtcNow));
        Assert.Equal(0, await repository.UnlockAsync(user.Id, ["first-step"], DateTime.UtcNow));
        Assert.Equal(0, await repository.UnlockAsync(user.Id, ["first-step"], DateTime.UtcNow));

        Assert.Single(await repository.GetAsync(user.Id));
    }

    [Fact]
    public async Task UnlockAsync_KeepsOriginalDateOnRepeat()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new AchievementRepository(scope.Context);

        var user = await TestData.AddUserAsync(scope.Context);
        var first = DateTime.UtcNow.AddDays(-3);

        await repository.UnlockAsync(user.Id, ["streak-7"], first);
        await repository.UnlockAsync(user.Id, ["streak-7"], DateTime.UtcNow);

        var stored = await repository.GetAsync(user.Id);

        // Дата получения не переписывается: «получено тогда-то» — это то,
        // чем человек делится, и подменять её при каждом заходе нельзя.
        Assert.Equal(first, stored[0].UnlockedAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task UnlockAsync_WithEmptyList_DoesNothing()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new AchievementRepository(scope.Context);

        var user = await TestData.AddUserAsync(scope.Context);

        Assert.Equal(0, await repository.UnlockAsync(user.Id, [], DateTime.UtcNow));
        Assert.Empty(await repository.GetAsync(user.Id));
    }

    [Fact]
    public async Task GetCodesAsync_ReturnsOnlyOwnCodes()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new AchievementRepository(scope.Context);

        var mine = await TestData.AddUserAsync(scope.Context);
        var other = await TestData.AddUserAsync(scope.Context);

        await repository.UnlockAsync(mine.Id, ["first-step"], DateTime.UtcNow);
        await repository.UnlockAsync(other.Id, ["words-100"], DateTime.UtcNow);

        var codes = await repository.GetCodesAsync(mine.Id);

        Assert.Equal(["first-step"], codes);
    }

    [Fact]
    public async Task UnlockAsync_ForUnknownUser_FailsOnForeignKey()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new AchievementRepository(scope.Context);

        // Награда без пользователя — это строка, которую никто никогда
        // не увидит и не удалит. Внешний ключ обязан её отклонить.
        await Assert.ThrowsAnyAsync<Exception>(
            () => repository.UnlockAsync(999_999_999, ["first-step"], DateTime.UtcNow));
    }

    [Fact]
    public async Task DeletingUser_RemovesTheirAchievements()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new AchievementRepository(scope.Context);

        var user = await TestData.AddUserAsync(scope.Context);
        await repository.UnlockAsync(user.Id, ["first-step"], DateTime.UtcNow);

        var tracked = await scope.Context.Users.FindAsync(user.Id);

        Assert.NotNull(tracked);
        scope.Context.Users.Remove(tracked!);
        await scope.Context.SaveChangesAsync();

        Assert.Empty(await repository.GetAsync(user.Id));
    }
}
