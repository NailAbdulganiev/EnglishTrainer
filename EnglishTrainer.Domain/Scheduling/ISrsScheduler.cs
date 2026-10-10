using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Enums;

namespace EnglishTrainer.Domain.Scheduling;

/// <summary>
/// Планировщик интервалов повторения. Вынесен в интерфейс, потому что алгоритм —
/// единственная часть системы, которую действительно стоит менять со временем
/// (SM-2 → FSRS), и менять его надо без правок в сервисах.
/// </summary>
public interface ISrsScheduler
{
    /// <summary>
    /// Пересчитывает состояние карточки после ответа.
    /// </summary>
    /// <param name="current">Текущее состояние или <c>null</c> для нового слова.</param>
    /// <param name="rating">Оценка пользователя.</param>
    /// <param name="nowUtc">Текущий момент (UTC), передаётся явно ради тестируемости.</param>
    SrsResult Schedule(CardState? current, ReviewRating rating, DateTime nowUtc);
}

/// <summary>Результат пересчёта: новое состояние карточки.</summary>
/// <param name="Phase">Этап жизненного цикла.</param>
/// <param name="Stability">Устойчивость памяти в днях.</param>
/// <param name="Difficulty">Сложность слова (1–10).</param>
/// <param name="IntervalDays">Назначенный интервал.</param>
/// <param name="DueAt">Когда показать снова.</param>
/// <param name="IsLapse">Был ли это провал (забыл уже изученное слово).</param>
public readonly record struct SrsResult(
    CardPhase Phase,
    double Stability,
    double Difficulty,
    double IntervalDays,
    DateTime DueAt,
    bool IsLapse);
