using EnglishTrainer.Domain.Common;
using EnglishTrainer.Domain.Enums;

namespace EnglishTrainer.Domain.Entities;

/// <summary>
/// Одна карточка внутри сессии: какое слово показывать и в каком порядке.
/// </summary>
/// <remarks>
/// Оценка ответа хранится здесь (и дублируется в <see cref="ReviewLog"/> как
/// часть истории повторений). Разделение намеренное: сессия — это «что я делал
/// сегодня», журнал — это «как я учил это слово всё время».
/// </remarks>
public class StudySessionItem : Entity
{
    public int StudySessionId { get; set; }

    public int WordId { get; set; }

    /// <summary>Ссылка на состояние карточки, если слово уже изучалось.</summary>
    public int? CardStateId { get; set; }

    /// <summary>Позиция в очереди, начиная с 1.</summary>
    public int Position { get; set; }

    public SessionItemKind Kind { get; set; }

    public ExerciseKind Exercise { get; set; } = ExerciseKind.Typing;

    public bool IsAnswered { get; set; }

    public DateTime? AnsweredAt { get; set; }

    public ReviewRating? Rating { get; set; }

    /// <summary>
    /// Была ли карточка показана заново в этой же сессии. Нужно для правила
    /// «забыл — вернётся в конце сессии»: так слово закрепляется, пока свежо.
    /// </summary>
    public bool IsRequeued { get; set; }

    public StudySession? StudySession { get; set; }

    public Word? Word { get; set; }

    public CardState? CardState { get; set; }
}
