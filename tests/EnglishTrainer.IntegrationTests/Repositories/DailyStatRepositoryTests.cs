using EnglishTrainer.Infrastructure.Repositories;
using EnglishTrainer.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;

namespace EnglishTrainer.IntegrationTests.Repositories;

/// <summary>
/// Дневная статистика: источник серии дней, кривой удержания и рейтинга.
/// </summary>
/// <remarks>
/// <para>
/// Отдельного внимания стоит выборка сразу по нескольким пользователям:
/// она появилась ради рейтинга, и её ошибка видна не как исключение, а как
/// неправильный рейтинг. Собирать статистику десяти друзей по одному запросу
/// на каждого — десять обращений к базе там, где хватает одного, поэтому
/// соблазн «упростить» её будет возникать снова.
/// </para>
/// <para>
/// Уникальный индекс по паре «пользователь + дата» тоже проверяется: на нём
/// держится то, что один день не попадёт в статистику дважды, а значит
/// и серия дней не удлинится сама собой.
/// </para>
/// </remarks>
public sealed class DailyStatRepositoryTests
{
    private static readonly DateOnly Today = new(2026, 10, 11);

    [Fact]
    public async Task GetRangeForUsersAsync_ReturnsOnlyRequestedUsersAndDays()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new DailyStatRepository(scope.Context);

        var mine = await TestData.AddUserAsync(scope.Context);
        var friend = await TestData.AddUserAsync(scope.Context);
        var stranger = await TestData.AddUserAsync(scope.Context);

        await TestData.AddDailyStatAsync(scope.Context, mine.Id, Today);
        await TestData.AddDailyStatAsync(scope.Context, mine.Id, Today.AddDays(-30));
        await TestData.AddDailyStatAsync(scope.Context, friend.Id, Today.AddDays(-1));
        await TestData.AddDailyStatAsync(scope.Context, stranger.Id, Today);

        var found = await repository.GetRangeForUsersAsync(
            [mine.Id, friend.Id], Today.AddDays(-7), Today);

        Assert.Equal(2, found.Count);
        Assert.DoesNotContain(found, stat => stat.UserId == stranger.Id);
        Assert.DoesNotContain(found, stat => stat.Date < Today.AddDays(-7));
    }

    [Fact]
    public async Task GetRangeForUsersAsync_WithNoUsers_ReturnsNothing()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new DailyStatRepository(scope.Context);

        var user = await TestData.AddUserAsync(scope.Context);
        await TestData.AddDailyStatAsync(scope.Context, user.Id, Today);

        // Пустой список — это запрос ни о чём, а не «про всех»: без явной
        // проверки условие Contains по пустому множеству вернуло бы пусто
        // само по себе, но полагаться на это в горячем запросе не стоит.
        Assert.Empty(await repository.GetRangeForUsersAsync([], Today.AddDays(-7), Today));
    }

    [Fact]
    public async Task GetRangeAsync_IsLimitedToOneUser()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new DailyStatRepository(scope.Context);

        var mine = await TestData.AddUserAsync(scope.Context);
        var other = await TestData.AddUserAsync(scope.Context);

        await TestData.AddDailyStatAsync(scope.Context, mine.Id, Today, reviewedCount: 7);
        await TestData.AddDailyStatAsync(scope.Context, other.Id, Today, reviewedCount: 70);

        var found = await repository.GetRangeAsync(mine.Id, Today.AddDays(-1), Today);

        Assert.Single(found);
        Assert.Equal(7, found[0].ReviewedCount);
    }

    [Fact]
    public async Task GetRangeAsync_ReturnsDaysInAscendingOrder()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new DailyStatRepository(scope.Context);

        var user = await TestData.AddUserAsync(scope.Context);

        // Добавляются вразнобой: порядок должен задавать запрос, а не порядок вставки.
        await TestData.AddDailyStatAsync(scope.Context, user.Id, Today);
        await TestData.AddDailyStatAsync(scope.Context, user.Id, Today.AddDays(-2));
        await TestData.AddDailyStatAsync(scope.Context, user.Id, Today.AddDays(-1));

        var found = await repository.GetRangeAsync(user.Id, Today.AddDays(-5), Today);

        Assert.Equal(
            [Today.AddDays(-2), Today.AddDays(-1), Today],
            found.Select(stat => stat.Date));
    }

    [Fact]
    public async Task GetRecentAsync_LimitsByDays()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new DailyStatRepository(scope.Context);

        var user = await TestData.AddUserAsync(scope.Context);

        await TestData.AddDailyStatAsync(scope.Context, user.Id, DateOnly.FromDateTime(DateTime.UtcNow));
        await TestData.AddDailyStatAsync(
            scope.Context, user.Id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-400));

        var recent = await repository.GetRecentAsync(user.Id, days: 30);

        Assert.Single(recent);
        Assert.Empty(await repository.GetRecentAsync(user.Id, days: 0));
    }

    [Fact]
    public async Task UniqueIndex_RejectsTwoStatsForSameDay()
    {
        await using var scope = await TestDatabase.BeginAsync();

        var user = await TestData.AddUserAsync(scope.Context);
        await TestData.AddDailyStatAsync(scope.Context, user.Id, Today);

        scope.Context.DailyStats.Add(new Domain.Entities.DailyStat
        {
            UserId = user.Id,
            Date = Today,
            ReviewedCount = 1
        });

        // Дневные агрегаты без дублей: иначе серия дней удлинялась бы сама собой,
        // а кривая удержания показывала бы неправду.
        await Assert.ThrowsAsync<DbUpdateException>(() => scope.Context.SaveChangesAsync());
    }
}
