using EnglishTrainer.Domain.Common;

namespace EnglishTrainer.Domain.Entities;

/// <summary>
/// Дневные агрегаты по пользователю: нужны для серии дней и графиков.
/// </summary>
/// <remarks>
/// Считаются заранее, а не запросом по журналу на каждый показ страницы:
/// со временем журнал становится большим, а статистика запрашивается часто.
/// Именно эта таблица — первый кандидат на кеширование в Redis.
/// </remarks>
public class DailyStat : Entity
{
    public int UserId { get; set; }

    /// <summary>Дата в часовом поясе пользователя, а не сервера.</summary>
    public DateOnly Date { get; set; }

    public int ReviewedCount { get; set; }

    public int NewWordsStarted { get; set; }

    public int CorrectCount { get; set; }

    /// <summary>Сколько всего слов выучено пользователем к концу этого дня.</summary>
    public int LearnedTotal { get; set; }

    /// <summary>Суммарное время занятий в секундах.</summary>
    public int StudySeconds { get; set; }

    public User? User { get; set; }

    /// <summary>Доля правильных ответов за день. <c>null</c>, если ответов не было.</summary>
    public double? Accuracy => ReviewedCount == 0 ? null : (double)CorrectCount / ReviewedCount;
}
