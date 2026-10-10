using EnglishTrainer.Domain.Common;

namespace EnglishTrainer.Domain.Entities;

/// <summary>
/// Персональные настройки обучения. Одна запись на пользователя
/// (первичный ключ совпадает с <see cref="User.Id"/>).
/// </summary>
public class UserSettings : Entity
{
    public int UserId { get; set; }

    /// <summary>
    /// Сколько новых слов вводить в день. По умолчанию мало намеренно:
    /// пользователь, взявший 50 новых слов, через три дня получает 300 карточек
    /// к повторению и уходит навсегда. Лимит — защита от выгорания, а не ограничение.
    /// </summary>
    public int NewWordsPerDay { get; set; } = 10;

    /// <summary>
    /// Верхняя граница карточек в одной сессии. Защищает от «стены» после перерыва:
    /// если пользователь вернулся через месяц, сессия не должна состоять из 500 карточек.
    /// </summary>
    public int MaxCardsPerSession { get; set; } = 60;

    /// <summary>Цель по количеству ответов в день (для серии и мотивации).</summary>
    public int DailyGoal { get; set; } = 30;

    public bool DailyReminderEnabled { get; set; } = true;

    /// <summary>Локальное время напоминания (в часовом поясе пользователя).</summary>
    public TimeOnly ReminderTime { get; set; } = new(19, 0);

    /// <summary>Показывать ли транскрипцию сразу вместе со словом.</summary>
    public bool ShowTranscriptionUpfront { get; set; } = true;

    public User? User { get; set; }
}
