namespace EnglishTrainer.Domain.Scheduling;

/// <summary>
/// Когда отправить следующее ежедневное напоминание.
/// </summary>
/// <remarks>
/// <para>
/// Чистая функция времени, поэтому она живёт в Domain и проверяется тестом
/// без базы и без сети. Это единственное место, где «напомнить в 19:00»
/// превращается в конкретный момент UTC, и ошибиться здесь дороже всего:
/// напоминание в неправильный час выглядит как сломанное приложение.
/// </para>
/// <para>
/// Момент хранится в UTC, а не вычисляется перебором всех пользователей раз
/// в минуту. Разница принципиальная: при переборе каждый тик читает таблицу
/// пользователей целиком, а с сохранённым моментом запрос — это выборка
/// по индексу <c>next_reminder_at_utc &lt;= now</c>, и она не растёт вместе
/// с числом пользователей.
/// </para>
/// </remarks>
public static class ReminderSchedule
{
    /// <summary>
    /// Ближайший момент, когда локальное время пользователя равно
    /// <paramref name="localTime"/>.
    /// </summary>
    /// <returns>
    /// Момент в UTC или <c>null</c>, если часовой пояс неизвестен: лучше
    /// не напомнить вовсе, чем напомнить посреди ночи по UTC.
    /// </returns>
    public static DateTime? NextOccurrence(TimeOnly localTime, string timeZoneId, DateTime utcNow)
    {
        var zone = Resolve(timeZoneId);

        if (zone is null)
        {
            return null;
        }

        var nowUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        var today = DateOnly.FromDateTime(localNow);

        var candidate = ToUtc(today, localTime, zone);

        // Если время сегодня уже прошло — напоминаем завтра. Сравнение строгое,
        // и это не придирка: планировщик пересчитывает расписание ровно в момент
        // отправки, и нестрогое сравнение вернуло бы тот же момент снова — то же
        // напоминание ушло бы второй раз на следующем тике. Совпадение до секунды
        // при этом практически невозможно: местное время выводится из текущего
        // момента с его долями, а не из целой минуты.
        return candidate > nowUtc
            ? candidate
            : ToUtc(today.AddDays(1), localTime, zone);
    }

    /// <summary>Часовой пояс по идентификатору IANA. <c>null</c>, если такого нет.</summary>
    public static TimeZoneInfo? Resolve(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }

    /// <summary>
    /// Локальное время суток в UTC с учётом переходов на летнее время.
    /// </summary>
    /// <remarks>
    /// В ночь перевода часов вперёд некоторые моменты не существуют: 02:30
    /// в этот день просто не наступает. <c>ConvertTimeToUtc</c> на таком
    /// времени падает, поэтому несуществующее время сдвигается вперёд до
    /// ближайшего действительного: напоминание лучше отправить на час позже,
    /// чем не отправить вовсе.
    /// </remarks>
    private static DateTime ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);

        for (var guard = 0; guard < 4 && zone.IsInvalidTime(local); guard++)
        {
            local = local.AddHours(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
