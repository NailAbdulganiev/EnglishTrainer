using EnglishTrainer.Domain.Entities;

namespace EnglishTrainer.Domain.Stats;

/// <summary>Серия дней занятий: текущая и самая длинная.</summary>
/// <param name="Current">Дней подряд по сегодняшний день.</param>
/// <param name="Longest">Самая длинная серия за всю историю.</param>
public readonly record struct StreakResult(int Current, int Longest);

/// <summary>
/// Подсчёт серии дней по датам занятий.
/// </summary>
/// <remarks>
/// <para>
/// Вынесено из сервиса статистики отдельной чистой функцией: тот же расчёт нужен
/// в списке друзей, а логика «серия не рвётся, если сегодня ещё не занимался»
/// слишком легко ломается при копировании.
/// </para>
/// <para>
/// Функция детерминирована и не знает ни про базу, ни про часовые пояса —
/// даты приходят уже в поясе пользователя.
/// </para>
/// </remarks>
public static class StreakCalculator
{
    public static StreakResult Calculate(IEnumerable<DateOnly> activeDates, DateOnly today)
    {
        var days = activeDates.ToHashSet();

        if (days.Count == 0)
            return new StreakResult(0, 0);

        // Текущая серия: считаем назад от сегодняшнего дня, но допускаем
        // «сегодня ещё не занимался» — иначе серия обнулялась бы каждое утро
        // и перестала бы мотивировать.
        var current = 0;
        var cursor = days.Contains(today) ? today : today.AddDays(-1);

        while (days.Contains(cursor))
        {
            current++;
            cursor = cursor.AddDays(-1);
        }

        // Длиннейшая серия: проходим даты по возрастанию и считаем длину
        // непрерывных отрезков.
        var longest = 0;
        var run = 0;
        DateOnly? previous = null;

        foreach (var date in days.OrderBy(d => d))
        {
            run = previous is not null && date == previous.Value.AddDays(1) ? run + 1 : 1;
            longest = Math.Max(longest, run);
            previous = date;
        }

        return new StreakResult(current, longest);
    }

    /// <summary>
    /// Даты, в которые дневная цель действительно выполнена.
    /// </summary>
    /// <remarks>
    /// <para>
    /// День занятий — это не «зашёл и ответил один раз», а выполненная дневная
    /// цель: при цели в 30 ответов и двадцати сделанных день серию не продлевает.
    /// Цель берётся из самой дневной записи (<see cref="DailyStat.DailyGoal"/>),
    /// а не из текущих настроек, поэтому прошлые дни не пересчитываются.
    /// </para>
    /// <para>
    /// Записи одного дня складываются: строк может оказаться несколько (например,
    /// при повторной синхронизации дня), и по отдельности ни одна из них цели
    /// не достигает, а вместе они её выполняют.
    /// </para>
    /// </remarks>
    public static IEnumerable<DateOnly> ActiveDates(IEnumerable<DailyStat> stats) =>
        stats
            .GroupBy(stat => stat.Date)
            .Where(IsDailyGoalReached)
            .Select(day => day.Key);

    /// <summary>Выполнена ли дневная цель в этот день.</summary>
    private static bool IsDailyGoalReached(IGrouping<DateOnly, DailyStat> day)
    {
        var reviews = 0;
        var goal = 0;

        foreach (var stat in day)
        {
            reviews += stat.ReviewedCount;
            goal = Math.Max(goal, stat.DailyGoal);
        }

        // Нулевая цель — запись до появления столбца: раньше день засчитывался
        // при любом ответе, и задним числом это правило не меняется.
        return reviews > 0 && reviews >= Math.Max(goal, 1);
    }
}
