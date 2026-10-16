using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Infrastructure.Repositories;
using EnglishTrainer.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;

namespace EnglishTrainer.IntegrationTests.Repositories;

/// <summary>
/// Колоды: видимость, прогресс и «пройдена целиком».
/// </summary>
/// <remarks>
/// <para>
/// Здесь проверяется запрос, который невозможно проверить иначе: «сколько колод
/// выучено целиком» — это вложенный NOT EXISTS по трём таблицам. Такой LINQ
/// компилируется всегда, а выполняется не всегда: EF может не суметь перевести
/// его в SQL и упасть при первом обращении, у пользователя, а не в тесте.
/// </para>
/// <para>
/// Отдельно закреплено, что пустая колода не считается пройденной. «Нет слов»
/// и «все слова выучены» — не одно и то же, и награда за пустую колоду была бы
/// наградой ни за что.
/// </para>
/// </remarks>
public sealed class DeckRepositoryTests
{
    [Fact]
    public async Task GetForUserAsync_ReturnsSystemDecksThenOwn()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new DeckRepository(scope.Context);

        var mine = await TestData.AddUserAsync(scope.Context);
        var stranger = await TestData.AddUserAsync(scope.Context);

        var system = await TestData.AddDeckAsync(scope.Context, isSystem: true);
        var own = await TestData.AddDeckAsync(scope.Context, isSystem: false, ownerId: mine.Id);
        var foreign = await TestData.AddDeckAsync(scope.Context, isSystem: false, ownerId: stranger.Id);

        var visible = await repository.GetForUserAsync(mine.Id);
        var ids = visible.Select(deck => deck.Id).ToList();

        Assert.Contains(system.Id, ids);
        Assert.Contains(own.Id, ids);
        Assert.DoesNotContain(foreign.Id, ids);

        // Личные колоды идут после системных: иначе своя колода оказывалась бы
        // в начале списка раньше «Первых дней», с которых логично начинать.
        Assert.True(
            ids.IndexOf(system.Id) < ids.IndexOf(own.Id),
            "Системные колоды должны идти раньше личных.");
    }

    [Fact]
    public async Task GetSystemAsync_ReturnsOnlySystemDecks()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new DeckRepository(scope.Context);

        var owner = await TestData.AddUserAsync(scope.Context);

        var system = await TestData.AddDeckAsync(scope.Context, isSystem: true);
        await TestData.AddDeckAsync(scope.Context, isSystem: false, ownerId: owner.Id);

        var decks = await repository.GetSystemAsync();

        Assert.Contains(decks, deck => deck.Id == system.Id);
        Assert.All(decks, deck => Assert.True(deck.IsSystem));
    }

    [Fact]
    public async Task CountCompletedAsync_CountsDeckOnlyWhenEveryWordIsLearned()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new DeckRepository(scope.Context);

        var user = await TestData.AddUserAsync(scope.Context);

        var learnedWord = await TestData.AddWordAsync(scope.Context);
        var inProgressWord = await TestData.AddWordAsync(scope.Context);

        var completed = await TestData.AddDeckAsync(scope.Context, wordIds: [learnedWord.Id]);
        await TestData.AddDeckAsync(scope.Context, wordIds: [learnedWord.Id, inProgressWord.Id]);

        await TestData.AddCardStateAsync(
            scope.Context, user.Id, learnedWord.Id,
            intervalDays: CardState.LearnedIntervalDays, dueAtUtc: DateTime.UtcNow);

        await TestData.AddCardStateAsync(
            scope.Context, user.Id, inProgressWord.Id,
            intervalDays: 3, dueAtUtc: DateTime.UtcNow.AddDays(3));

        var count = await repository.CountCompletedAsync(user.Id, CardState.LearnedIntervalDays);

        Assert.Equal(1, count);
        Assert.True(completed.Id > 0);
    }

    [Fact]
    public async Task CountCompletedAsync_DoesNotCountEmptyDeck()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new DeckRepository(scope.Context);

        var user = await TestData.AddUserAsync(scope.Context);
        await TestData.AddDeckAsync(scope.Context);

        // Колода без слов: «все слова выучены» формально верно, но награда
        // за неё была бы наградой ни за что.
        Assert.Equal(0, await repository.CountCompletedAsync(user.Id, CardState.LearnedIntervalDays));
    }

    [Fact]
    public async Task CountCompletedAsync_DoesNotCountOtherUsersProgress()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new DeckRepository(scope.Context);

        var mine = await TestData.AddUserAsync(scope.Context);
        var other = await TestData.AddUserAsync(scope.Context);

        var word = await TestData.AddWordAsync(scope.Context);
        await TestData.AddDeckAsync(scope.Context, wordIds: [word.Id]);

        await TestData.AddCardStateAsync(
            scope.Context, other.Id, word.Id,
            intervalDays: CardState.LearnedIntervalDays, dueAtUtc: DateTime.UtcNow);

        Assert.Equal(0, await repository.CountCompletedAsync(mine.Id, CardState.LearnedIntervalDays));
    }

    [Fact]
    public async Task CountCompletedAsync_CountsNothingWithoutCards()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new DeckRepository(scope.Context);

        var user = await TestData.AddUserAsync(scope.Context);
        var word = await TestData.AddWordAsync(scope.Context);
        await TestData.AddDeckAsync(scope.Context, wordIds: [word.Id]);

        Assert.Equal(0, await repository.CountCompletedAsync(user.Id, CardState.LearnedIntervalDays));
    }

    [Fact]
    public async Task NextWordOrderAsync_OnEmptyDeckStartsAtZero()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new DeckRepository(scope.Context);

        var deck = await TestData.AddDeckAsync(scope.Context);

        // Max по пустому множеству не определён: без запасного значения первое
        // слово получило бы позицию по умолчанию и оказалось бы не в конце.
        Assert.Equal(0, await repository.NextWordOrderAsync(deck.Id));
    }

    [Fact]
    public async Task NextWordOrderAsync_ContinuesAfterLastWord()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new DeckRepository(scope.Context);

        var first = await TestData.AddWordAsync(scope.Context);
        var second = await TestData.AddWordAsync(scope.Context);
        var deck = await TestData.AddDeckAsync(scope.Context, wordIds: [first.Id, second.Id]);

        Assert.Equal(2, await repository.NextWordOrderAsync(deck.Id));
    }

    [Fact]
    public async Task RemoveAsync_DeletesDeckAndItsWordLinks()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new DeckRepository(scope.Context);

        var word = await TestData.AddWordAsync(scope.Context);
        var deck = await TestData.AddDeckAsync(scope.Context, wordIds: [word.Id]);

        await repository.RemoveAsync(deck);
        await scope.Context.SaveChangesAsync();

        Assert.False(await scope.Context.DeckWords.AnyAsync(link => link.DeckId == deck.Id));

        // Слово остаётся: оно лежит в общем справочнике и может использоваться
        // другими колодами, включая системные.
        Assert.True(await scope.Context.Words.AnyAsync(item => item.Id == word.Id));
    }

    [Fact]
    public async Task UniqueIndex_RejectsSameWordTwiceInOneDeck()
    {
        await using var scope = await TestDatabase.BeginAsync();

        var word = await TestData.AddWordAsync(scope.Context);
        var deck = await TestData.AddDeckAsync(scope.Context, wordIds: [word.Id]);

        scope.Context.DeckWords.Add(new DeckWord { DeckId = deck.Id, WordId = word.Id, Order = 1 });

        await Assert.ThrowsAsync<DbUpdateException>(() => scope.Context.SaveChangesAsync());
    }
}
