using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Enums;

namespace EnglishTrainer.Domain.Scheduling;

/// <summary>
/// Планировщик интервалов на основе SM-2 (алгоритм, который десятилетиями
/// работал в Anki) с отдельным этапом заучивания.
/// </summary>
/// <remarks>
/// <para>
/// Почему SM-2, а не FSRS. FSRS точнее, но требует набора весов, обученных
/// на большой истории ответов. У нового проекта истории нет, поэтому честный
/// порядок такой: запустить SM-2, накопить журнал (<c>ReviewLog</c>) и перейти
/// на FSRS, когда данных станет достаточно.
/// </para>
/// <para>
/// Данные к переходу уже подготовлены: <see cref="CardState.Stability"/> и
/// <see cref="CardState.Difficulty"/> — поля FSRS, а журнал хранит состояние
/// до каждого ответа, поэтому историю можно пересчитать задним числом.
/// </para>
/// <para>
/// Реализация детерминирована: время передаётся параметром, случайности нет,
/// поэтому алгоритм полностью покрывается юнит-тестами.
/// </para>
/// </remarks>
public sealed class Sm2SrsScheduler(SrsOptions? options = null) : ISrsScheduler
{
    private readonly SrsOptions _options = options ?? new SrsOptions();

    public SrsResult Schedule(CardState? current, ReviewRating rating, DateTime nowUtc) =>
        current is null
            ? ScheduleNew(rating, nowUtc)
            : ScheduleExisting(current, rating, nowUtc);

    private SrsResult ScheduleNew(ReviewRating rating, DateTime nowUtc)
    {
        // Новое слово всегда попадает в этап заучивания: интервал измеряется
        // часами, а не днями. «Легко» сразу даёт вторую ступень — если слово
        // действительно знакомо, незачем гонять его по минутам.
        var intervalDays = rating switch
        {
            ReviewRating.Again => _options.AgainIntervalMinutes / MinutesPerDay,
            _ => _options.LearningStepHours[0] / HoursPerDay
        };

        if (rating == ReviewRating.Easy)
            intervalDays = _options.LearningStepHours[^1] / HoursPerDay;

        return new SrsResult(
            CardPhase.Learning,
            _options.InitialStabilityDays,
            _options.InitialDifficulty,
            intervalDays,
            nowUtc.AddDays(intervalDays),
            IsLapse: false);
    }

    private SrsResult ScheduleExisting(CardState current, ReviewRating rating, DateTime nowUtc)
    {
        var stability = current.Stability <= 0 ? _options.InitialStabilityDays : current.Stability;
        var difficulty = current.Difficulty <= 0 ? _options.InitialDifficulty : current.Difficulty;

        // Провал: слово забыто. Интервал сбрасывается, сложность растёт,
        // но накопленная история сохраняется — иначе алгоритм потеряет
        // представление о том, что слово для этого пользователя трудное.
        if (rating == ReviewRating.Again)
        {
            var retryDays = _options.AgainIntervalMinutes / MinutesPerDay;
            var wasLearned = current.Phase == CardPhase.Review || current.IsLearned;

            return new SrsResult(
                wasLearned ? CardPhase.Relearning : CardPhase.Learning,
                Math.Max(_options.MinStabilityDays, stability * _options.LapseStabilityFactor),
                ClampDifficulty(difficulty + _options.LapseDifficultyPenalty),
                retryDays,
                nowUtc.AddDays(retryDays),
                IsLapse: wasLearned);
        }

        var factor = rating switch
        {
            ReviewRating.Hard => _options.HardFactor,
            ReviewRating.Good => _options.GoodFactor,
            _ => _options.EasyFactor
        };

        var ease = _options.EaseFromDifficulty(difficulty) + (factor - _options.GoodFactor);

        // Ключевая формула SM-2: новый интервал = старый × коэффициент лёгкости.
        // Именно она даёт экспоненциальное разрежение повторений.
        var intervalDays = current.IntervalDays <= 0
            ? _options.LearningStepHours[^1] / HoursPerDay
            : current.IntervalDays * ease;

        // Пока слово в этапе заучивания, интервал не должен перескакивать
        // через сутки: слово обязано закрепиться, прежде чем уйти в долгосрочное.
        if (current.Phase is CardPhase.Learning or CardPhase.Relearning
            && intervalDays < _options.GraduatingIntervalDays)
        {
            var maxLearningStep = _options.LearningStepHours[^1] / HoursPerDay;
            intervalDays = Math.Min(Math.Max(intervalDays, _options.MinIntervalDays), maxLearningStep);
        }
        else if (intervalDays < _options.GraduatingIntervalDays)
        {
            intervalDays = _options.GraduatingIntervalDays;
        }

        intervalDays = Math.Clamp(intervalDays, _options.MinIntervalDays, _options.MaxIntervalDays);

        var nextDifficulty = ClampDifficulty(difficulty + rating switch
        {
            ReviewRating.Hard => _options.HardDifficultyDelta,
            ReviewRating.Easy => _options.EasyDifficultyDelta,
            _ => 0
        });

        // Устойчивость памяти для FSRS: растёт вместе с интервалом.
        var nextStability = Math.Max(stability, intervalDays * _options.StabilityGain);

        var nextPhase = intervalDays < _options.GraduatingIntervalDays
            ? (current.IsLearned ? CardPhase.Relearning : CardPhase.Learning)
            : CardPhase.Review;

        return new SrsResult(
            nextPhase,
            nextStability,
            nextDifficulty,
            intervalDays,
            nowUtc.AddDays(intervalDays),
            IsLapse: false);
    }

    private const double HoursPerDay = 24.0;
    private const double MinutesPerDay = 24.0 * 60.0;

    private double ClampDifficulty(double value) =>
        Math.Clamp(value, _options.MinDifficulty, _options.MaxDifficulty);
}

/// <summary>Параметры алгоритма: вынесены в объект, чтобы их можно было настраивать и тестировать.</summary>
public sealed class SrsOptions
{
    /// <summary>Ступени заучивания в часах. Последняя — «выпускной» интервал.</summary>
    public double[] LearningStepHours { get; init; } = [8, 24];

    /// <summary>Через сколько минут вернуть карточку при ответе «не помню».</summary>
    public double AgainIntervalMinutes { get; init; } = 10;

    /// <summary>Минимальный интервал. Дробный, потому что на заучивании он меньше суток.</summary>
    public double MinIntervalDays { get; init; } = 10.0 / (24.0 * 60.0);

    /// <summary>Максимальный интервал: больше года ставить бессмысленно.</summary>
    public double MaxIntervalDays { get; init; } = 365;

    /// <summary>Интервал, после которого слово уходит в долгосрочное повторение.</summary>
    public double GraduatingIntervalDays { get; init; } = 1;

    public double InitialStabilityDays { get; init; } = 1;
    public double InitialDifficulty { get; init; } = 5;
    public double MinDifficulty { get; init; } = 1;
    public double MaxDifficulty { get; init; } = 10;

    /// <summary>Множители интервала. Это «кривая забывания» в числовом виде.</summary>
    public double HardFactor { get; init; } = 1.2;
    public double GoodFactor { get; init; } = 2.0;
    public double EasyFactor { get; init; } = 3.0;

    public double HardDifficultyDelta { get; init; } = 0.3;
    public double EasyDifficultyDelta { get; init; } = -0.3;
    public double LapseDifficultyPenalty { get; init; } = 1.0;
    public double LapseStabilityFactor { get; init; } = 0.4;
    public double MinStabilityDays { get; init; } = 0.5;

    /// <summary>Насколько устойчивость обгоняет интервал (задел под FSRS).</summary>
    public double StabilityGain { get; init; } = 2.0;

    /// <summary>Обратное преобразование сложности в «лёгкость» SM-2 (2.5 — нейтрально).</summary>
    public double EaseFromDifficulty(double difficulty) =>
        2.5 - (difficulty - InitialDifficulty) * 0.15;
}
