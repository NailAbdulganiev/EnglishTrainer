using EnglishTrainer.Domain.Common;
using EnglishTrainer.Domain.Enums;

namespace EnglishTrainer.Domain.Entities;

/// <summary>
/// Сессия обучения: зафиксированная очередь карточек.
/// </summary>
/// <remarks>
/// Очередь сохраняется в БД, а не формируется на каждый запрос. Причина:
/// при обновлении страницы или переключении устройства пользователь должен
/// продолжить ту же сессию, а не получить новую с другими словами.
/// У пользователя может быть только одна активная сессия — это гарантируется
/// уникальным индексом в БД, а не проверкой в коде.
/// </remarks>
public class StudySession : Entity
{
    public int UserId { get; set; }

    /// <summary>Колода, по которой идёт сессия. <c>null</c> — слова из всех колод.</summary>
    public int? DeckId { get; set; }

    public StudySessionStatus Status { get; set; } = StudySessionStatus.Active;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    /// <summary>Локальная дата пользователя, к которой относится сессия (для дневной статистики).</summary>
    public DateOnly LocalDate { get; set; }

    public int PlannedReviewCount { get; set; }

    public int PlannedNewCount { get; set; }

    public int AnsweredCount { get; set; }

    public int CorrectCount { get; set; }

    public User? User { get; set; }

    public Deck? Deck { get; set; }

    public ICollection<StudySessionItem> Items { get; set; } = new List<StudySessionItem>();

    public bool IsFinished => Status is StudySessionStatus.Completed or StudySessionStatus.Abandoned;
}
