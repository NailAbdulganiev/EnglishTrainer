using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Enums;
using EnglishTrainer.Domain.Scheduling;

namespace EnglishTrainer.UnitTests.Domain;

/// <summary>
/// Проверка алгоритма интервалов повторения.
/// </summary>
/// <remarks>
/// Это единственная часть системы, от которой напрямую зависит, выучит ли человек
/// слова. Ошибка здесь не проявляется сразу: она становится видна через месяц,
/// когда интервалы вырастают до неразумных значений. Поэтому поведение
/// зафиксировано тестами.
/// </remarks>
public sealed class SrsSchedulerTests
{
    private static readonly DateTime Now = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly Sm2SrsScheduler _scheduler = new();

    [Fact]
    public void NewWord_WithGoodRating_GoesToLearningWithIntradayInterval()
    {
        var result = _scheduler.Schedule(null, ReviewRating.Good, Now);

        Assert.Equal(CardPhase.Learning, result.Phase);
        // Первая ступень заучивания — часы, а не дни: интервал меньше суток.
        Assert.InRange(result.IntervalDays, 1.0 / 24.0, 1.0);
        Assert.Equal(Now.AddDays(result.IntervalDays), result.DueAt);
        Assert.False(result.IsLapse);
    }

    [Fact]
    public void NewWord_WithAgainRating_ReturnsWithinMinutes()
    {
        var result = _scheduler.Schedule(null, ReviewRating.Again, Now);

        // Десять минут — это 10 / (24 * 60) суток.
        Assert.Equal(10.0 / (24.0 * 60.0), result.IntervalDays, precision: 6);
        Assert.True(result.DueAt > Now);
    }

    [Fact]
    public void NewWord_WithEasyRating_SkipsFirstLearningStep()
    {
        var good = _scheduler.Schedule(null, ReviewRating.Good, Now);
        var easy = _scheduler.Schedule(null, ReviewRating.Easy, Now);

        Assert.True(easy.IntervalDays > good.IntervalDays);
    }

    [Fact]
    public void GraduatedCard_MovesToReviewPhase()
    {
        var current = ExistingCard(phase: CardPhase.Learning, intervalDays: 1.0);

        var result = _scheduler.Schedule(current, ReviewRating.Good, Now);

        Assert.Equal(CardPhase.Review, result.Phase);
        Assert.True(result.IntervalDays > 1.0);
    }

    [Fact]
    public void RepeatedGoodRatings_GrowIntervalMonotonically()
    {
        CardState? card = null;
        var previousInterval = 0.0;

        for (var review = 0; review < 6; review++)
        {
            var result = _scheduler.Schedule(card, ReviewRating.Good, Now);

            Assert.True(
                result.IntervalDays > previousInterval,
                $"Интервал должен расти: шаг {review + 1} дал {result.IntervalDays} после {previousInterval}.");

            previousInterval = result.IntervalDays;
            card = ExistingCard(result.Phase, result.IntervalDays, result.Stability, result.Difficulty);
        }

        // К шестому повторению интервал обязан перейти в недели, иначе
        // «интервальное повторение» не работает и слова придётся зубрить заново.
        Assert.True(previousInterval > 7, $"Ожидался интервал больше недели, получено {previousInterval}.");
    }

    [Fact]
    public void ForgettingLearnedWord_ResetsIntervalAndCountsLapse()
    {
        var current = ExistingCard(phase: CardPhase.Review, intervalDays: 30.0, stability: 40.0);

        var result = _scheduler.Schedule(current, ReviewRating.Again, Now);

        Assert.Equal(CardPhase.Relearning, result.Phase);
        Assert.True(result.IsLapse);
        Assert.True(result.IntervalDays < 1.0);
    }

    [Fact]
    public void ForgettingLearnedWord_RaisesDifficultyButKeepsItWithinBounds()
    {
        var nearlyImpossible = ExistingCard(
            phase: CardPhase.Review, intervalDays: 30.0, difficulty: 10.0);

        var result = _scheduler.Schedule(nearlyImpossible, ReviewRating.Again, Now);

        Assert.Equal(10.0, result.Difficulty);
    }

    [Fact]
    public void EasyRatings_NeverExceedMaximumInterval()
    {
        var current = ExistingCard(
            phase: CardPhase.Review, intervalDays: 300.0, stability: 400.0, difficulty: 2.0);

        var result = _scheduler.Schedule(current, ReviewRating.Easy, Now);

        Assert.True(result.IntervalDays <= 365.0, $"Интервал {result.IntervalDays} превысил год.");
    }

    [Fact]
    public void HardRating_GrowsIntervalSlowerThanGoodRating()
    {
        var hard = _scheduler.Schedule(
            ExistingCard(CardPhase.Review, 10.0), ReviewRating.Hard, Now);

        var good = _scheduler.Schedule(
            ExistingCard(CardPhase.Review, 10.0), ReviewRating.Good, Now);

        Assert.True(hard.IntervalDays < good.IntervalDays);
    }

    [Fact]
    public void Schedule_IsDeterministic()
    {
        var card = ExistingCard(CardPhase.Review, 12.0);

        var first = _scheduler.Schedule(card, ReviewRating.Good, Now);
        var second = _scheduler.Schedule(card, ReviewRating.Good, Now);

        Assert.Equal(first, second);
    }

    /// <summary>Состояние карточки, уже находящейся в изучении.</summary>
    private static CardState ExistingCard(
        CardPhase phase,
        double intervalDays,
        double stability = 10.0,
        double difficulty = 5.0) => new()
        {
            Id = 1,
            UserId = 1,
            WordId = 1,
            Phase = phase,
            IntervalDays = intervalDays,
            Stability = stability,
            Difficulty = difficulty,
            DueAt = Now,
            Reps = 3,
            LastReviewedAt = Now.AddDays(-intervalDays)
        };
}
