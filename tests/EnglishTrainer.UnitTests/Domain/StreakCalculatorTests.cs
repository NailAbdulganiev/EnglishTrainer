using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Stats;

namespace EnglishTrainer.UnitTests.Domain;

/// <summary>
/// Проверка подсчёта серии дней занятий.
/// </summary>
/// <remarks>
/// <para>
/// Серия — главный мотиватор в приложении, и ошибается она тихо: человек видит
/// «0 дней» утром, когда ещё не успел позаниматься, и решает, что всё пропало.
/// Поэтому здесь зафиксирована не только длина серии, но и граница между
/// «сегодня ещё не занимался» и «серия действительно прервана».
/// </para>
/// <para>
/// Отдельно проверяется, что длиннейшая серия считается по всей истории и не
/// зависит от сегодняшнего дня: это исторический рекорд, а не текущее состояние.
/// </para>
/// </remarks>
public sealed class StreakCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 3, 10);

    [Fact]
    public void Calculate_WithoutActiveDays_ReturnsZeroStreaks() =>
        // Пустая история — обычное состояние нового пользователя: интерфейс должен
        // получить нули, а не исключение.
        Assert.Equal(new StreakResult(0, 0), StreakCalculator.Calculate([], Today));

    [Fact]
    public void Calculate_WithOnlyToday_CountsSingleDayStreak()
    {
        var result = StreakCalculator.Calculate([Today], Today);

        Assert.Equal(1, result.Current);
        Assert.Equal(1, result.Longest);
    }

    [Fact]
    public void Calculate_WithThreeDaysIncludingToday_CountsThree()
    {
        var result = StreakCalculator.Calculate(
            [Today.AddDays(-2), Today.AddDays(-1), Today], Today);

        Assert.Equal(3, result.Current);
        Assert.Equal(3, result.Longest);
    }

    [Fact]
    public void Calculate_WhenTodayIsNotStudiedYet_KeepsStreakFromYesterday()
    {
        // Ключевое поведение: до конца дня серия не рвётся. Иначе каждый утренний
        // вход в приложение показывал бы «0» и отбивал желание заниматься.
        var result = StreakCalculator.Calculate(
            [Today.AddDays(-3), Today.AddDays(-2), Today.AddDays(-1)], Today);

        Assert.Equal(3, result.Current);
        Assert.Equal(3, result.Longest);
    }

    [Fact]
    public void Calculate_WhenWholeDayIsMissed_DropsCurrentStreakToZero()
    {
        // Пропущен целый день (последнее занятие — позавчера): серия прервана.
        // Длиннейшая при этом остаётся: рекорд не сгорает из-за перерыва.
        var result = StreakCalculator.Calculate(
            [Today.AddDays(-4), Today.AddDays(-3), Today.AddDays(-2)], Today);

        Assert.Equal(0, result.Current);
        Assert.Equal(3, result.Longest);
    }

    [Fact]
    public void Calculate_WithGapInMiddle_CountsOnlyCurrentRunAsCurrentStreak()
    {
        var first = new DateOnly(2026, 3, 1);
        var second = new DateOnly(2026, 3, 2);
        var fourth = new DateOnly(2026, 3, 4);

        var result = StreakCalculator.Calculate([first, second, fourth], fourth);

        // Пропуск третьего числа обрывает серию: текущая считается только с
        // четвёртого, а длиннейшей остаётся отрезок из двух дней.
        Assert.Equal(1, result.Current);
        Assert.Equal(2, result.Longest);
    }

    [Fact]
    public void Calculate_WhenHistoryEndedLongAgo_KeepsLongestFromWholeHistory()
    {
        var dates = Enumerable.Range(1, 5).Select(day => new DateOnly(2026, 3, day)).ToArray();
        var today = new DateOnly(2026, 3, 20);

        var result = StreakCalculator.Calculate(dates, today);

        // Длиннейшая серия — исторический рекорд, он не зависит от today.
        Assert.Equal(0, result.Current);
        Assert.Equal(5, result.Longest);
    }

    [Fact]
    public void Calculate_DoesNotDependOnDateOrder()
    {
        var ordered = new[] { Today.AddDays(-2), Today.AddDays(-1), Today };
        var shuffled = new[] { Today.AddDays(-1), Today, Today.AddDays(-2) };

        // Даты приходят из разных источников (журнал, агрегаты) и не обязаны быть
        // отсортированными: результат не должен зависеть от порядка.
        Assert.Equal(
            StreakCalculator.Calculate(ordered, Today),
            StreakCalculator.Calculate(shuffled, Today));
        Assert.Equal(new StreakResult(3, 3), StreakCalculator.Calculate(shuffled, Today));
    }

    [Fact]
    public void Calculate_IgnoresDuplicateDates()
    {
        // Дубликаты возможны при повторной синхронизации дня, и они не должны
        // искусственно надувать длину серии.
        var result = StreakCalculator.Calculate([Today, Today, Today.AddDays(-1)], Today);

        Assert.Equal(2, result.Current);
        Assert.Equal(2, result.Longest);
    }

    [Fact]
    public void ActiveDates_SkipsDaysWithoutReviews()
    {
        var studiedFirst = Stat(new DateOnly(2026, 3, 1), reviewedCount: 5);
        var skipped = Stat(new DateOnly(2026, 3, 2), reviewedCount: 0);
        var studiedThird = Stat(new DateOnly(2026, 3, 3), reviewedCount: 1);

        var dates = StreakCalculator.ActiveDates([studiedFirst, skipped, studiedThird]).ToArray();

        // День с нулём ответов занятием не считается: иначе серия продлевалась бы
        // простым открытием приложения без единого повторения.
        Assert.Equal<DateOnly>([studiedFirst.Date, studiedThird.Date], dates);
    }

    [Fact]
    public void ActiveDates_SkipsDayWhereDailyGoalWasNotReached()
    {
        var goal = 30;
        var short_ = Stat(new DateOnly(2026, 3, 1), reviewedCount: 20, dailyGoal: goal);
        var complete = Stat(new DateOnly(2026, 3, 2), reviewedCount: goal, dailyGoal: goal);

        var dates = StreakCalculator.ActiveDates([short_, complete]).ToArray();

        // Двадцать ответов при цели в тридцать — день не выполнен: именно из-за
        // этого серия раньше «давалась» раньше, чем человек заканчивал норму.
        Assert.Equal<DateOnly>([complete.Date], dates);
    }

    [Fact]
    public void ActiveDates_SumsRecordsOfTheSameDay()
    {
        var goal = 30;
        var morning = Stat(new DateOnly(2026, 3, 1), reviewedCount: 18, dailyGoal: goal);
        var evening = Stat(new DateOnly(2026, 3, 1), reviewedCount: 12, dailyGoal: goal);

        var dates = StreakCalculator.ActiveDates([morning, evening]).ToArray();

        // Ни одна запись по отдельности цели не достигает, а вместе они её выполняют.
        Assert.Equal<DateOnly>([morning.Date], dates);
    }

    [Fact]
    public void ActiveDates_ForRecordsWithoutFixedGoal_CountsAnyReview()
    {
        // Записи, созданные до появления столбца цели: правило не меняется задним
        // числом, иначе у всех, кто занимался годами, серия обнулилась бы молча.
        var legacy = Stat(new DateOnly(2026, 3, 1), reviewedCount: 1, dailyGoal: 0);

        Assert.Equal<DateOnly>([legacy.Date], StreakCalculator.ActiveDates([legacy]).ToArray());
    }

    [Fact]
    public void Calculate_WhenDayIsBelowGoal_BreaksCurrentStreak()
    {
        var goal = 30;
        var twoDaysAgo = Stat(Today.AddDays(-2), reviewedCount: goal, dailyGoal: goal);
        var yesterday = Stat(Today.AddDays(-1), reviewedCount: 20, dailyGoal: goal);
        var today = Stat(Today, reviewedCount: goal, dailyGoal: goal);

        var result = StreakCalculator.Calculate(StreakCalculator.ActiveDates([twoDaysAgo, yesterday, today]), Today);

        // Серия считается по выполненным дням: вчерашние двадцать ответов её
        // обрывают, и текущая серия начинается заново с сегодняшнего дня.
        Assert.Equal(1, result.Current);
        Assert.Equal(1, result.Longest);
    }

    [Fact]
    public void ActiveDates_FeedCalculateWithoutBreakingStreak()
    {
        var stats = new[]
        {
            Stat(Today.AddDays(-2), reviewedCount: 4),
            Stat(Today.AddDays(-1), reviewedCount: 0),
            Stat(Today.AddDays(-1), reviewedCount: 7),
            Stat(Today, reviewedCount: 3)
        };

        var result = StreakCalculator.Calculate(StreakCalculator.ActiveDates(stats), Today);

        // Фильтрация и подсчёт должны быть согласованы: нулевая запись в середине
        // не разрывает серию, а дубликат дня её не удлиняет.
        Assert.Equal(3, result.Current);
        Assert.Equal(3, result.Longest);
    }

    /// <summary>Запись дневной статистики с указанным числом ответов.</summary>
    /// <param name="dailyGoal">
    /// Дневная цель, действовавшая в тот день. Ноль — запись без зафиксированной
    /// цели: так выглядят строки, созданные до появления столбца.
    /// </param>
    private static DailyStat Stat(DateOnly date, int reviewedCount, int dailyGoal = 0) =>
        new() { Date = date, ReviewedCount = reviewedCount, DailyGoal = dailyGoal };
}
