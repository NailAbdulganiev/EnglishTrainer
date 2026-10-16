using EnglishTrainer.Domain.Entities;

namespace EnglishTrainer.Domain.Stats;

/// <summary>Результат участника за неделю.</summary>
/// <param name="WeeklyWords">Сколько новых слов взято за неделю.</param>
/// <param name="WeeklyReviews">Сколько ответов дано за неделю.</param>
/// <param name="CurrentStreak">Текущая серия дней.</param>
public readonly record struct LeaderboardScore(int WeeklyWords, int WeeklyReviews, int CurrentStreak);

/// <summary>Участник рейтинга вместе с местом.</summary>
/// <param name="Rank">Место. У равных результатов место одинаковое.</param>
public sealed record LeaderboardPlayer(
    int UserId,
    string Username,
    string DisplayName,
    string? AvatarUrl,
    LeaderboardScore Score,
    int Rank = 0);

/// <summary>
/// Подсчёт и упорядочивание недельного рейтинга.
/// </summary>
/// <remarks>
/// <para>
/// Чистые функции в Domain, а не запрос в базе. Рейтинг — это правило («кто
/// выше»), и правило, размазанное по SQL, нельзя ни покрыть тестом, ни
/// объяснить словами: сортировка по трём полям с разными направлениями
/// в SQL ещё и читается тяжело.
/// </para>
/// <para>
/// Окно — последние семь дней, а не календарная неделя. Календарная неделя
/// требует общего для всех начала отсчёта, а участники живут в разных часовых
/// поясах: у одного понедельник уже начался, у другого ещё воскресенье.
/// Скользящее окно честнее и, кроме того, не обнуляет результат понедельник
/// утром — падение в ноль после недели работы демотивирует.
/// </para>
/// </remarks>
public static class LeaderboardRanking
{
    /// <summary>Сколько дней длится окно рейтинга.</summary>
    public const int WindowDays = 7;

    /// <summary>
    /// Считает результат участника.
    /// </summary>
    /// <param name="recent">
    /// Дневная статистика за окно серии (не только за неделю): серия дней
    /// не помещается в семь суток, а второй запрос ради неё — лишний.
    /// </param>
    /// <param name="weekFrom">Первый день недельного окна.</param>
    /// <param name="weekTo">Последний день недельного окна.</param>
    /// <param name="today">«Сегодня» в поясе участника.</param>
    public static LeaderboardScore ScoreFor(
        IReadOnlyList<DailyStat> recent, DateOnly weekFrom, DateOnly weekTo, DateOnly today)
    {
        var weeklyWords = 0;
        var weeklyReviews = 0;

        foreach (var stat in recent)
        {
            if (stat.Date < weekFrom || stat.Date > weekTo)
            {
                continue;
            }

            weeklyWords += stat.NewWordsStarted;
            weeklyReviews += stat.ReviewedCount;
        }

        var streak = StreakCalculator.Calculate(StreakCalculator.ActiveDates(recent), today);

        return new LeaderboardScore(weeklyWords, weeklyReviews, streak.Current);
    }

    /// <summary>
    /// Расставляет места.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Сортировка по трём ключам: сначала слова за неделю, затем ответы, затем
    /// серия. Порядок не произвольный — он от самого «дорогого» показателя
    /// к самому дешёвому: сто ответов можно набрать за вечер, а сто новых слов
    /// требуют недели.
    /// </para>
    /// <para>
    /// Ник — последний ключ. Без него порядок участников с одинаковым счётом
    /// зависел бы от порядка строк в базе, и список прыгал бы при каждом
    /// обновлении страницы.
    /// </para>
    /// <para>
    /// Места при равенстве не разделяются: два человека с одинаковым счётом
    /// получают одно место, а следующее пропускается (1, 2, 2, 4). Придумывать
    /// искусственный разрыв между одинаковыми результатами — значит наказывать
    /// за то, чего человек не делал.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<LeaderboardPlayer> Rank(IReadOnlyList<LeaderboardPlayer> players)
    {
        var ordered = players
            .OrderByDescending(player => player.Score.WeeklyWords)
            .ThenByDescending(player => player.Score.WeeklyReviews)
            .ThenByDescending(player => player.Score.CurrentStreak)
            .ThenBy(player => player.Username, StringComparer.Ordinal)
            .ToList();

        var result = new List<LeaderboardPlayer>(ordered.Count);

        LeaderboardScore? previous = null;
        var rank = 0;

        for (var index = 0; index < ordered.Count; index++)
        {
            var player = ordered[index];

            if (previous is null || player.Score != previous.Value)
            {
                rank = index + 1;
                previous = player.Score;
            }

            result.Add(player with { Rank = rank });
        }

        return result;
    }
}
