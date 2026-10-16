using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Enums;
using EnglishTrainer.Infrastructure.Persistence;
using EnglishTrainer.Infrastructure.Repositories;
using EnglishTrainer.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;

namespace EnglishTrainer.IntegrationTests.Repositories;

/// <summary>
/// Состояния карточек: выборка «что пора повторить» и сводка прогресса.
/// </summary>
/// <remarks>
/// <para>
/// Это самый горячий запрос системы, и проверяется в нём то, что нельзя
/// проверить иначе: порядок сортировки, границы выборки и определения слов
/// «выучено» и «пора повторить». Сквозной сценарий видит только результат
/// целиком и не заметит, например, что просроченные карточки приходят
/// в обратном порядке — очередь всё равно наполнится.
/// </para>
/// <para>
/// Отдельно закреплено единство определения «выучено». Оно встречается в трёх
/// местах — в сводке, в прогрессе колоды и в свойстве сущности, — и раньше
/// в сводке считалось по сроку повторения, а в колоде по интервалу. Два
/// определения одного числа дают разные цифры на двух экранах.
/// </para>
/// </remarks>
public sealed class CardStateRepositoryTests
{
    [Fact]
    public async Task GetDueAsync_ReturnsOnlyOverdueCardsInOrderOfDueDate()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new CardStateRepository(scope.Context);

        var user = await TestData.AddUserAsync(scope.Context);
        var now = DateTime.UtcNow;

        var overdueLong = await AddCardAsync(scope.Context, user.Id, now.AddDays(-5), intervalDays: 12);
        var overdueRecent = await AddCardAsync(scope.Context, user.Id, now.AddMinutes(-10), intervalDays: 3);

        // Не просрочена: срок в будущем.
        await AddCardAsync(scope.Context, user.Id, now.AddDays(2), intervalDays: 3);

        var due = await repository.GetDueAsync(user.Id, now, limit: 10);

        // Порядок — по сроку, а не по интервалу: иначе давно выученные слова
        // с большим интервалом вытесняли бы свежие и самые просроченные.
        Assert.Equal(
            [overdueLong.WordId, overdueRecent.WordId],
            due.Select(state => state.WordId));
    }

    [Fact]
    public async Task GetDueAsync_RespectsLimit()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new CardStateRepository(scope.Context);

        var user = await TestData.AddUserAsync(scope.Context);
        var now = DateTime.UtcNow;

        for (var index = 0; index < 5; index++)
        {
            await AddCardAsync(scope.Context, user.Id, now.AddDays(-1 - index), intervalDays: 2);
        }

        var due = await repository.GetDueAsync(user.Id, now, limit: 2);

        Assert.Equal(2, due.Count);
    }

    [Fact]
    public async Task GetDueAsync_DoesNotMixUsersCards()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new CardStateRepository(scope.Context);

        var mine = await TestData.AddUserAsync(scope.Context);
        var other = await TestData.AddUserAsync(scope.Context);
        var now = DateTime.UtcNow;

        var myCard = await AddCardAsync(scope.Context, mine.Id, now.AddDays(-1), intervalDays: 2);
        await AddCardAsync(scope.Context, other.Id, now.AddDays(-1), intervalDays: 2);

        var due = await repository.GetDueAsync(mine.Id, now, limit: 10);

        Assert.Single(due);
        Assert.Equal(myCard.WordId, due[0].WordId);
    }

    [Fact]
    public async Task GetDueAsync_WithNonPositiveLimit_ReturnsNothing()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new CardStateRepository(scope.Context);

        var user = await TestData.AddUserAsync(scope.Context);
        await AddCardAsync(scope.Context, user.Id, DateTime.UtcNow.AddDays(-1), intervalDays: 2);

        // Ноль и отрицательное значение — не «без ограничения», а «ничего»:
        // иначе опечатка в настройке выдала бы пользователю всю его базу карточек.
        Assert.Empty(await repository.GetDueAsync(user.Id, DateTime.UtcNow, limit: 0));
        Assert.Empty(await repository.GetDueAsync(user.Id, DateTime.UtcNow, limit: -5));
    }

    [Fact]
    public async Task GetDueAsync_WithDeckId_ReturnsOnlyWordsOfThatDeck()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new CardStateRepository(scope.Context);

        var user = await TestData.AddUserAsync(scope.Context);
        var now = DateTime.UtcNow;

        var mine = await AddCardAsync(scope.Context, user.Id, now.AddDays(-2), intervalDays: 3);
        await AddCardAsync(scope.Context, user.Id, now.AddDays(-1), intervalDays: 3);
        await AddCardAsync(scope.Context, user.Id, now.AddDays(-1), intervalDays: 3);

        var deck = await TestData.AddDeckAsync(scope.Context, wordIds: [mine.WordId]);

        var due = await repository.GetDueAsync(user.Id, now, limit: 10, deckId: deck.Id);

        // Долг по повторениям общий для пользователя, а не для колоды: без
        // ограничения запрос «по колоде B» приносил бы слова колоды A.
        Assert.Single(due);
        Assert.Equal(mine.WordId, due[0].WordId);
    }

    [Fact]
    public async Task GetDueAsync_WithDeckId_IncludesWordSharedByTwoDecks()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new CardStateRepository(scope.Context);

        var user = await TestData.AddUserAsync(scope.Context);
        var now = DateTime.UtcNow;

        var shared = await AddCardAsync(scope.Context, user.Id, now.AddDays(-1), intervalDays: 3);

        var first = await TestData.AddDeckAsync(scope.Context, wordIds: [shared.WordId]);
        var second = await TestData.AddDeckAsync(scope.Context, wordIds: [shared.WordId]);

        // Одно слово может лежать в нескольких колодах: «колода карточки» —
        // свойство связи, поэтому слово обязано находиться в обеих.
        Assert.Single(await repository.GetDueAsync(user.Id, now, limit: 10, deckId: first.Id));
        Assert.Single(await repository.GetDueAsync(user.Id, now, limit: 10, deckId: second.Id));
    }

    [Fact]
    public async Task GetDueAsync_WithoutDeckId_ReturnsCardsOfAllDecks()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new CardStateRepository(scope.Context);

        var user = await TestData.AddUserAsync(scope.Context);
        var now = DateTime.UtcNow;

        var first = await AddCardAsync(scope.Context, user.Id, now.AddDays(-2), intervalDays: 3);
        var second = await AddCardAsync(scope.Context, user.Id, now.AddDays(-1), intervalDays: 3);

        await TestData.AddDeckAsync(scope.Context, wordIds: [first.WordId]);
        await TestData.AddDeckAsync(scope.Context, wordIds: [second.WordId]);

        var due = await repository.GetDueAsync(user.Id, now, limit: 10);

        // Общая тренировка по-прежнему берёт слова отовсюду: ограничение
        // включается только тогда, когда колоду выбрали явно.
        Assert.Equal(2, due.Count);
    }

    [Fact]
    public async Task GetSoonestAsync_WithDeckId_ReturnsOnlyWordsOfThatDeck()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new CardStateRepository(scope.Context);

        var user = await TestData.AddUserAsync(scope.Context);
        var now = DateTime.UtcNow;

        var mine = await AddCardAsync(scope.Context, user.Id, now.AddDays(5), intervalDays: 6);
        await AddCardAsync(scope.Context, user.Id, now.AddHours(1), intervalDays: 1);

        var deck = await TestData.AddDeckAsync(scope.Context, wordIds: [mine.WordId]);

        var soonest = await repository.GetSoonestAsync(user.Id, limit: 10, deckId: deck.Id);

        // Тренировка вне расписания не должна терять выбранную колоду: иначе
        // «заниматься этой темой» превращается в «заниматься чем попало».
        Assert.Single(soonest);
        Assert.Equal(mine.WordId, soonest[0].WordId);
    }

    [Fact]
    public async Task CountDueAsync_CountsOnlyOverdue()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new CardStateRepository(scope.Context);

        var user = await TestData.AddUserAsync(scope.Context);
        var now = DateTime.UtcNow;

        await AddCardAsync(scope.Context, user.Id, now.AddDays(-1), intervalDays: 2);
        await AddCardAsync(scope.Context, user.Id, now.AddHours(-1), intervalDays: 2);
        await AddCardAsync(scope.Context, user.Id, now.AddDays(3), intervalDays: 2);

        Assert.Equal(2, await repository.CountDueAsync(user.Id, now));
    }

    [Fact]
    public async Task GetNextDueAtAsync_ReturnsNearestFutureMoment()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new CardStateRepository(scope.Context);

        var user = await TestData.AddUserAsync(scope.Context);
        var now = DateTime.UtcNow;

        var soon = now.AddHours(3);
        await AddCardAsync(scope.Context, user.Id, now.AddDays(5), intervalDays: 6);
        await AddCardAsync(scope.Context, user.Id, soon, intervalDays: 1);

        var next = await repository.GetNextDueAtAsync(user.Id, now);

        Assert.NotNull(next);
        Assert.Equal(soon, next!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetNextDueAtAsync_WhenNothingAhead_ReturnsNull()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new CardStateRepository(scope.Context);

        var user = await TestData.AddUserAsync(scope.Context);
        var now = DateTime.UtcNow;

        await AddCardAsync(scope.Context, user.Id, now.AddDays(-1), intervalDays: 2);

        // Пустая очередь впереди — это не ошибка, а «сегодня всё»: интерфейсу
        // нужно уметь отличить это от «карточек нет вовсе».
        Assert.Null(await repository.GetNextDueAtAsync(user.Id, now));
    }

    [Fact]
    public async Task GetSoonestAsync_IncludesCardsThatAreNotDueYet()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new CardStateRepository(scope.Context);

        var user = await TestData.AddUserAsync(scope.Context);
        var now = DateTime.UtcNow;

        await AddCardAsync(scope.Context, user.Id, now.AddDays(5), intervalDays: 6);
        await AddCardAsync(scope.Context, user.Id, now.AddHours(1), intervalDays: 1);

        var soonest = await repository.GetSoonestAsync(user.Id, limit: 1);

        Assert.Single(soonest);
        Assert.True(soonest[0].DueAt < now.AddDays(1));
    }

    [Fact]
    public async Task GetSummaryAsync_CountsLearnedByIntervalNotByDueDate()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new CardStateRepository(scope.Context);

        var user = await TestData.AddUserAsync(scope.Context);
        var now = DateTime.UtcNow;

        // Ровно на пороге «выучено», но срок повторения — сегодня.
        // Прежнее определение («срок дальше чем через месяц») считало такую
        // карточку невыученной, а прогресс колоды — выученной.
        var boundary = await TestData.AddWordAsync(scope.Context);
        await TestData.AddCardStateAsync(
            scope.Context,
            user.Id,
            boundary.Id,
            intervalDays: CardState.LearnedIntervalDays,
            dueAtUtc: now);

        var inProgress = await TestData.AddWordAsync(scope.Context);
        await TestData.AddCardStateAsync(
            scope.Context, user.Id, inProgress.Id, intervalDays: 5, dueAtUtc: now.AddDays(5));

        var summary = await repository.GetSummaryAsync(user.Id, now);

        Assert.Equal(2, summary.Total);
        Assert.Equal(1, summary.Learned);
        Assert.Equal(1, summary.DueNow);
    }

    [Fact]
    public async Task GetSummaryAsync_AgreesWithCardStateIsLearned()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new CardStateRepository(scope.Context);

        var user = await TestData.AddUserAsync(scope.Context);
        var now = DateTime.UtcNow;

        // Интервалы по обе стороны порога, включая ровно порог.
        double[] intervals = [0, 1, 29.9, CardState.LearnedIntervalDays, 45];

        foreach (var interval in intervals)
        {
            var word = await TestData.AddWordAsync(scope.Context);

            await TestData.AddCardStateAsync(
                scope.Context,
                user.Id,
                word.Id,
                intervalDays: interval,
                // Срок намеренно «неудобный»: сегодня и завтра. Если определение
                // снова начнут считать по сроку, тест это поймает.
                dueAtUtc: now.AddDays(interval % 2));
        }

        var expected = intervals.Count(interval => interval >= CardState.LearnedIntervalDays);
        var summary = await repository.GetSummaryAsync(user.Id, now);

        Assert.Equal(expected, summary.Learned);
    }

    [Fact]
    public async Task UniqueIndex_RejectsSecondStateForSameUserAndWord()
    {
        await using var scope = await TestDatabase.BeginAsync();

        var user = await TestData.AddUserAsync(scope.Context);
        var word = await TestData.AddWordAsync(scope.Context);

        await TestData.AddCardStateAsync(
            scope.Context, user.Id, word.Id, intervalDays: 1, dueAtUtc: DateTime.UtcNow);

        // Гарантия живёт в базе, а не в коде: проверка «есть ли уже состояние»
        // не спасает от гонки двух одновременных запросов, а уникальный индекс —
        // спасает. Тест проверяет именно индекс.
        scope.Context.CardStates.Add(new CardState
        {
            UserId = user.Id,
            WordId = word.Id,
            IntervalDays = 2,
            DueAt = DateTime.UtcNow.AddDays(2)
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => scope.Context.SaveChangesAsync());
    }

    /// <summary>Заводит слово и состояние карточки для него — так тест не отвлекается на слова.</summary>
    private static async Task<CardState> AddCardAsync(
        AppDbContext context, int userId, DateTime dueAt, double intervalDays)
    {
        var word = await TestData.AddWordAsync(context);

        return await TestData.AddCardStateAsync(
            context, userId, word.Id, intervalDays, dueAt, CardPhase.Review);
    }
}
