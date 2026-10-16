using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Stats;

namespace EnglishTrainer.UnitTests.Domain;

/// <summary>
/// Подсчёт и упорядочивание недельного рейтинга.
/// </summary>
/// <remarks>
/// <para>
/// Рейтинг — это в первую очередь правило «кто выше», и оно должно быть
/// проверяемым без базы. Здесь проверяются границы окна, порядок ключей
/// сортировки и поведение при равных результатах: именно на равенстве
/// сортировка обычно и ломается, выдавая разным людям одно место и одновременно
/// завися от порядка строк в базе.
/// </para>
/// <para>
/// Отдельно закреплено, что участники с одинаковым счётом не разделяются:
/// искусственный разрыв между одинаковыми результатами наказывает за то,
/// чего человек не делал.
/// </para>
/// </remarks>
public sealed class LeaderboardRankingTests
{
    private static readonly DateOnly Today = new(2026, 10, 11);

    [Fact]
    public void ScoreFor_SumsOnlyTheWindow()
    {
        // Окно — семь дней, сегодня включительно. День за его границей
        // (11 - 7 = 4 октября) в счёт не идёт, хотя и лежит в истории.
        var stats = new List<DailyStat>
        {
            Stat(1, Today.AddDays(-6), words: 3, reviews: 12),
            Stat(1, Today, words: 2, reviews: 8),
            Stat(1, Today.AddDays(-7), words: 100, reviews: 500)
        };

        var score = LeaderboardRanking.ScoreFor(stats, Today.AddDays(-6), Today, Today);

        Assert.Equal(5, score.WeeklyWords);
        Assert.Equal(20, score.WeeklyReviews);
    }

    [Fact]
    public void ScoreFor_CountsStreakFromWholeHistory()
    {
        // Серия длиннее недельного окна: история приходит за 180 дней, и серия
        // считается по ней, а не по семи дням.
        var stats = Enumerable.Range(0, 30)
            .Select(offset => Stat(1, Today.AddDays(-offset), words: 1, reviews: 5))
            .ToList();

        var score = LeaderboardRanking.ScoreFor(stats, Today.AddDays(-6), Today, Today);

        Assert.Equal(30, score.CurrentStreak);
    }

    [Fact]
    public void ScoreFor_WithoutStats_ReturnsZero()
    {
        var score = LeaderboardRanking.ScoreFor([], Today.AddDays(-6), Today, Today);

        Assert.Equal(0, score.WeeklyWords);
        Assert.Equal(0, score.WeeklyReviews);
        Assert.Equal(0, score.CurrentStreak);
    }

    [Fact]
    public void Rank_OrdersByWordsThenReviewsThenStreak()
    {
        var players = new List<LeaderboardPlayer>
        {
            Player("streak", new LeaderboardScore(0, 0, 30)),
            Player("reviews", new LeaderboardScore(0, 200, 0)),
            Player("words", new LeaderboardScore(10, 0, 0))
        };

        var ranked = LeaderboardRanking.Rank(players);

        Assert.Equal(["words", "reviews", "streak"], ranked.Select(player => player.Username));
        Assert.Equal([1, 2, 3], ranked.Select(player => player.Rank));
    }

    [Fact]
    public void Rank_GivesEqualScoresTheSamePlaceAndSkipsTheNext()
    {
        // Соревновательное ранжирование (1, 2, 2, 4), а не порядковые номера:
        // два человека с одинаковым счётом не должны оказаться на разных местах.
        var players = new List<LeaderboardPlayer>
        {
            Player("first", new LeaderboardScore(20, 40, 5)),
            Player("second-a", new LeaderboardScore(10, 20, 3)),
            Player("second-b", new LeaderboardScore(10, 20, 3)),
            Player("last", new LeaderboardScore(1, 1, 1))
        };

        var ranked = LeaderboardRanking.Rank(players);

        Assert.Equal(1, ranked[0].Rank);
        Assert.Equal(2, ranked[1].Rank);
        Assert.Equal(2, ranked[2].Rank);
        Assert.Equal(4, ranked[3].Rank);
    }

    [Fact]
    public void Rank_DoesNotDependOnInputOrder()
    {
        // Последний ключ сортировки — ник. Без него порядок равных участников
        // зависел бы от порядка строк в базе, и список прыгал бы при каждом
        // обновлении страницы.
        var players = new List<LeaderboardPlayer>
        {
            Player("carol", new LeaderboardScore(5, 5, 5)),
            Player("alice", new LeaderboardScore(5, 5, 5)),
            Player("bob", new LeaderboardScore(5, 5, 5))
        };

        var forward = LeaderboardRanking.Rank(players);
        var backward = LeaderboardRanking.Rank([.. players.AsEnumerable().Reverse()]);

        Assert.Equal(["alice", "bob", "carol"], forward.Select(player => player.Username));
        Assert.Equal(
            forward.Select(player => player.Username),
            backward.Select(player => player.Username));
    }

    [Fact]
    public void Rank_WithSinglePlayer_PutsThemFirst()
    {
        var ranked = LeaderboardRanking.Rank([Player("solo", new LeaderboardScore(0, 0, 0))]);

        Assert.Single(ranked);
        Assert.Equal(1, ranked[0].Rank);
    }

    [Fact]
    public void Rank_WithNoPlayers_ReturnsEmpty() =>
        Assert.Empty(LeaderboardRanking.Rank([]));

    [Fact]
    public void WindowDays_IsSeven() =>
        // Число зашито в тексты интерфейса («рейтинг недели»). Меняя его,
        // придётся менять и подписи — пусть это будет заметно здесь.
        Assert.Equal(7, LeaderboardRanking.WindowDays);

    private static LeaderboardPlayer Player(string username, LeaderboardScore score) =>
        new(UserId: username.GetHashCode(StringComparison.Ordinal) & 0x7FFFFFFF,
            Username: username,
            DisplayName: username,
            AvatarUrl: null,
            Score: score);

    private static DailyStat Stat(int userId, DateOnly date, int words, int reviews) =>
        new()
        {
            UserId = userId,
            Date = date,
            NewWordsStarted = words,
            ReviewedCount = reviews,
            CorrectCount = reviews
        };
}
