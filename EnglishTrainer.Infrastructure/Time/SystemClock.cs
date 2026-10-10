using EnglishTrainer.Domain.Abstractions;

namespace EnglishTrainer.Infrastructure.Time;

/// <summary>
/// Системные часы. Единственное место в приложении, где читается настоящее время.
/// </summary>
/// <remarks>
/// Всё остальное получает время через <see cref="IClock"/>. Это позволяет
/// в тестах подставить любую дату и проверить, например, что серия дней
/// не рвётся при переходе через полночь в часовом поясе пользователя.
/// </remarks>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public DateOnly TodayFor(string timeZoneId) => DateOnly.FromDateTime(ToLocalTime(timeZoneId));

    public TimeOnly LocalTimeFor(string timeZoneId) => TimeOnly.FromDateTime(ToLocalTime(timeZoneId));

    /// <summary>
    /// Переводит текущий момент в местное время пользователя.
    /// </summary>
    /// <remarks>
    /// Неизвестный или устаревший идентификатор пояса не должен ронять запрос:
    /// строка приходит из настроек пользователя, и она может оказаться
    /// из системы, где нет нужной базы часовых поясов. В этом случае
    /// безопасное поведение — считать время по UTC, а не отдавать 500.
    /// </remarks>
    private static DateTime ToLocalTime(string timeZoneId)
    {
        var utcNow = DateTime.UtcNow;

        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(
                string.IsNullOrWhiteSpace(timeZoneId) ? TimeZoneInfo.Utc.Id : timeZoneId);

            return TimeZoneInfo.ConvertTimeFromUtc(utcNow, timeZone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return utcNow;
        }
    }
}
