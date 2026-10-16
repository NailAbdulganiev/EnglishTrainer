using EnglishTrainer.Domain.Scheduling;

namespace EnglishTrainer.UnitTests.Domain;

/// <summary>
/// Расчёт момента следующего напоминания.
/// </summary>
/// <remarks>
/// <para>
/// Здесь «напомнить в 19:00» превращается в конкретный момент UTC — единственное
/// место, где это происходит. Ошибка стоит дорого и выглядит одинаково неприятно
/// в обе стороны: напоминание посреди ночи или полное его отсутствие.
/// </para>
/// <para>
/// Отдельно проверяются переходы на летнее время. В ночь перевода часов вперёд
/// некоторых моментов не существует вовсе, и наивный перевод локального времени
/// в UTC на них падает — а напоминание, назначенное на 02:30, просто пропадает.
/// </para>
/// </remarks>
public sealed class ReminderScheduleTests
{
    [Fact]
    public void NextOccurrence_ForTimeLaterToday_ReturnsSameDayInUtc()
    {
        // 12:00 UTC — это 15:00 в Москве. Напоминание в 19:00 ещё впереди,
        // значит оно сегодня и в 16:00 UTC.
        var next = ReminderSchedule.NextOccurrence(
            new TimeOnly(19, 0), "Europe/Moscow", new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 10, 11, 16, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void NextOccurrence_ForTimeAlreadyPassed_ReturnsTomorrow()
    {
        // 20:00 UTC — это 23:00 в Москве: сегодняшние 19:00 прошли четыре часа назад.
        var next = ReminderSchedule.NextOccurrence(
            new TimeOnly(19, 0), "Europe/Moscow", new DateTime(2026, 10, 11, 20, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 10, 12, 16, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void NextOccurrence_ForUnknownTimeZone_ReturnsNull()
    {
        // Лучше не напомнить вовсе, чем напомнить по UTC человеку в UTC+13.
        Assert.Null(ReminderSchedule.NextOccurrence(
            new TimeOnly(19, 0), "Mars/Olympus", new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NextOccurrence_WithoutTimeZone_ReturnsNull(string? timeZoneId) =>
        Assert.Null(ReminderSchedule.NextOccurrence(
            new TimeOnly(19, 0), timeZoneId!, new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc)));

    [Fact]
    public void NextOccurrence_IsAlwaysStrictlyInTheFuture()
    {
        // Планировщик пересчитывает расписание сразу после отправки. Если бы
        // функция могла вернуть текущий момент, то же напоминание ушло бы
        // повторно на следующем тике.
        var now = new DateTime(2026, 10, 11, 16, 0, 0, DateTimeKind.Utc);

        var next = ReminderSchedule.NextOccurrence(new TimeOnly(19, 0), "Europe/Moscow", now);

        Assert.True(next > now, $"Следующее напоминание {next} не позже текущего момента {now}.");
    }

    [Fact]
    public void NextOccurrence_AcrossSpringForward_SkipsNonexistentLocalTime()
    {
        // В ночь на 29 марта 2026 года в Берлине 02:00 сразу становится 03:00,
        // и времени 02:30 в этот день не существует. Наивный перевод локального
        // времени в UTC на нём падает, и напоминание теряется — поэтому
        // несуществующее время сдвигается на час вперёд: 03:30 CEST = 01:30 UTC.
        var next = ReminderSchedule.NextOccurrence(
            new TimeOnly(2, 30), "Europe/Berlin", new DateTime(2026, 3, 29, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 3, 29, 1, 30, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void NextOccurrence_AcrossFallBack_KeepsTheLaterOffset()
    {
        // Обратный переход делает время неоднозначным: 02:30 в Берлине
        // 25 октября 2026 года бывает дважды. Выбирается пояс стандартного
        // времени, то есть второе вхождение — напоминание приходит позже,
        // но приходит ровно один раз.
        var next = ReminderSchedule.NextOccurrence(
            new TimeOnly(2, 30), "Europe/Berlin", new DateTime(2026, 10, 24, 20, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void NextOccurrence_ForUtcUser_KeepsLocalTimeEqualToUtc()
    {
        var next = ReminderSchedule.NextOccurrence(
            new TimeOnly(7, 15), "UTC", new DateTime(2026, 10, 11, 3, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 10, 11, 7, 15, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void NextOccurrence_ForTimeZoneAheadOfUtc_CrossesDateBoundary()
    {
        // 20:00 UTC 11 октября — это 07:00 12 октября в Владивостоке (UTC+10).
        // Локальные 08:00 наступят в тот же местный день, то есть в 22:00 UTC
        // уже 11 октября по UTC. Разные даты в двух системах — обычное дело.
        var next = ReminderSchedule.NextOccurrence(
            new TimeOnly(8, 0), "Asia/Vladivostok", new DateTime(2026, 10, 11, 20, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 10, 11, 22, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void Resolve_ForKnownTimeZone_ReturnsIt()
    {
        var zone = ReminderSchedule.Resolve("Europe/Moscow");

        Assert.NotNull(zone);
    }
}
