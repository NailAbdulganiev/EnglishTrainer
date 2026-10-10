using EnglishTrainer.Domain.Common;
using EnglishTrainer.Domain.Enums;

namespace EnglishTrainer.Domain.Entities;

/// <summary>
/// Журнал каждого ответа пользователя. Пишется всегда, даже если кажется избыточным.
/// </summary>
/// <remarks>
/// По этой истории алгоритм FSRS подстраивает параметры под конкретного человека,
/// а вы получаете кривую удержания — единственный честный показатель того,
/// работает ли обучение. Без журнала вы навсегда застрянете на дефолтных параметрах
/// и не сможете ответить на вопрос «стало ли лучше».
/// </remarks>
public class ReviewLog : Entity
{
    public int CardStateId { get; set; }

    public int UserId { get; set; }

    public int WordId { get; set; }

    public ReviewRating Rating { get; set; }

    public DateTime ReviewedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Сколько дней прошло с прошлого показа. Для FSRS это ключевой вход.</summary>
    public double ElapsedDays { get; set; }

    /// <summary>Какой интервал был назначен по итогу этого ответа.</summary>
    public double ScheduledDays { get; set; }

    /// <summary>Состояние до ответа — нужно, чтобы можно было пересчитать историю.</summary>
    public CardPhase PhaseBefore { get; set; }

    public double StabilityBefore { get; set; }

    public double DifficultyBefore { get; set; }

    /// <summary>Время ответа в миллисекундах. Слишком быстрый ответ — признак угадывания.</summary>
    public int? ElapsedMs { get; set; }

    public ExerciseKind Exercise { get; set; }

    public CardState? CardState { get; set; }
}
