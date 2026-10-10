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

    /// <summary>Даты, в которые пользователь действительно занимался.</summary>
    public static IEnumerable<DateOnly> ActiveDates(IEnumerable<DailyStat> stats) =>
        stats.Where(stat => stat.ReviewedCount > 0).Select(stat => stat.Date);
}
