using EnglishTrainer.Domain.Abstractions;

namespace EnglishTrainer.UnitTests.TestDoubles;

/// <summary>
/// Часы с управляемым временем.
/// </summary>
/// <remarks>
/// Алгоритм повторений и проверка токенов зависят от текущего момента. Если бы они
/// читали системное время напрямую, проверить истечение срока действия токена
/// или рост интервала можно было бы только ожиданием — то есть никак.
/// </remarks>
internal sealed class FixedClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; set; } = utcNow;

    public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);

    public DateOnly TodayFor(string timeZoneId) => DateOnly.FromDateTime(UtcNow);

    public TimeOnly LocalTimeFor(string timeZoneId) => TimeOnly.FromDateTime(UtcNow);
}
