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

    /// <summary>
    /// Дневная цель в ответах, действовавшая в тот день.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Цель хранится рядом с днём, а не берётся из текущих настроек при подсчёте
    /// серии. Иначе повышение плана задним числом рвало бы уже набранную серию:
    /// человек поднял цель с 20 до 30 и увидел ноль дней подряд вместо двадцати.
    /// </para>
    /// <para>
    /// Ноль означает «цель не зафиксирована» — так выглядят строки, созданные до
    /// появления столбца. Такой день засчитывается при любом ответе, как и раньше:
    /// менять прошлое задним числом нельзя.
    /// </para>
    /// </remarks>
    public int DailyGoal { get; set; }

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
