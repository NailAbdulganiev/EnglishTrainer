using EnglishTrainer.Domain.Common;
using EnglishTrainer.Domain.Enums;

namespace EnglishTrainer.Domain.Entities;

/// <summary>
/// Состояние повторения слова у конкретного пользователя — центральная сущность системы.
/// </summary>
/// <remarks>
/// Ключ — пара (пользователь, слово), а не «карточка в колоде». Одно слово может
/// лежать в нескольких колодах, но прогресс по нему должен быть один: иначе
/// пользователь «учит» одно слово трижды независимо и статистика врёт.
/// </remarks>
public class CardState : Entity
{
    public int UserId { get; set; }

    public int WordId { get; set; }

    public CardPhase Phase { get; set; } = CardPhase.New;

    /// <summary>
    /// Устойчивость памяти: через сколько дней вероятность вспомнить падает до целевой.
    /// Поле заведено под FSRS — даже если сейчас работает более простая схема,
    /// структуру данных менять потом не придётся.
    /// </summary>
    public double Stability { get; set; }

    /// <summary>Сложность слова для этого пользователя (1–10). Растёт при ошибках.</summary>
    public double Difficulty { get; set; }

    /// <summary>Текущий интервал в днях. Дробный — на этапе заучивания интервалы меньше суток.</summary>
    public double IntervalDays { get; set; }

    /// <summary>Когда карточку пора показать снова. По этому полю строится вся сессия.</summary>
    public DateTime DueAt { get; set; } = DateTime.UtcNow;

    /// <summary>Сколько раз слово показано.</summary>
    public int Reps { get; set; }

    /// <summary>Сколько раз пользователь забыл слово после того, как уже «выучил».</summary>
    public int Lapses { get; set; }

    public DateTime? LastReviewedAt { get; set; }

    /// <summary>
    /// Слово считается выученным при интервале больше этого значения.
    /// Определение «выучено» обязано быть проверяемым: «увидел 5 раз» — не критерий.
    /// </summary>
    public const double LearnedIntervalDays = 30;

    public bool IsLearned => IntervalDays >= LearnedIntervalDays;

    public User? User { get; set; }

    public Word? Word { get; set; }
}
