using EnglishTrainer.Application.Dtos;
using EnglishTrainer.Domain.Abstractions;
using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Enums;
using EnglishTrainer.Domain.Exceptions;
using EnglishTrainer.Domain.Repositories;
using EnglishTrainer.Domain.Stats;

namespace EnglishTrainer.Application.Services;

/// <summary>
/// «Слово дня» и напоминания для бота.
/// </summary>
/// <remarks>
/// <para>
/// Отдельный сервис, а не часть <see cref="TelegramService"/>: привязка нужна
/// веб-версии, а напоминания — только боту. Смешав их, пришлось бы держать
/// в API зависимость от того, как формируется сообщение в мессенджере.
/// </para>
/// <para>
/// Напоминание отправляется не «всем, у кого совпало локальное время», а тем,
/// у кого наступил сохранённый момент <c>NextReminderAtUtc</c>. Разница видна
/// на нагрузке: первый вариант раз в минуту читает таблицу пользователей
/// целиком, второй — несколько строк по индексу.
/// </para>
/// </remarks>
public sealed class TelegramReminderService(
    IUserRepository users,
    IWordRepository words,
    ICardStateRepository cardStates,
    IDailyStatRepository dailyStats,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    /// <summary>Глубина истории для подсчёта серии дней — та же, что в профиле.</summary>
    private const int StreakWindowDays = 180;

    /// <summary>
    /// Забирает напоминания, которым пришло время, и сдвигает их расписание.
    /// </summary>
    /// <remarks>
    /// Расписание сдвигается <b>до</b> отправки. Это осознанный размен:
    /// повторный запуск планировщика в ту же минуту не отправит напоминание
    /// дважды, а неудачная отправка оставит человека без одного напоминания.
    /// Два одинаковых сообщения раздражают сильнее, чем одно недостающее,
    /// и, что важнее, дубли выглядят как сломанный сервис.
    /// </remarks>
    public async Task<IReadOnlyList<TelegramReminderDto>> TakeDueAsync(
        int limit, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var due = await users.GetDueRemindersAsync(now, limit, ct);
        var result = new List<TelegramReminderDto>(due.Count);

        foreach (var user in due)
        {
            user.ScheduleReminder(now);

            var digest = await BuildDigestAsync(user, ct);

            result.Add(new TelegramReminderDto(user.Id, user.TelegramId!.Value, user.DisplayName, digest));
        }

        await unitOfWork.SaveChangesAsync(ct);

        return result;
    }

    /// <summary>Сводка дня: сколько к повторению, сколько сделано, серия и слово дня.</summary>
    public async Task<TelegramDigestDto> GetDigestAsync(int userId, CancellationToken ct = default)
    {
        var user = await users.GetWithSettingsAsync(userId, ct)
            ?? throw NotFoundException.For("Пользователь", userId);

        return await BuildDigestAsync(user, ct);
    }

    /// <summary>Сводка дня по аккаунту Telegram — для команд бота.</summary>
    public async Task<TelegramDigestDto> GetDigestForTelegramAsync(
        long telegramId, CancellationToken ct = default)
    {
        var user = await users.GetByTelegramIdAsync(telegramId, ct)
            ?? throw new ForbiddenException("Этот Telegram не привязан к аккаунту.");

        return await BuildDigestAsync(user, ct);
    }

    /// <summary>Слово дня — по аккаунту Telegram.</summary>
    public async Task<WordOfTheDayDto?> GetWordOfTheDayForTelegramAsync(
        long telegramId, CancellationToken ct = default)
    {
        var user = await users.GetByTelegramIdAsync(telegramId, ct)
            ?? throw new ForbiddenException("Этот Telegram не привязан к аккаунту.");

        return await PickWordAsync(user, ct);
    }

    private async Task<TelegramDigestDto> BuildDigestAsync(User user, CancellationToken ct)
    {
        var settings = user.Settings ?? new UserSettings();
        var today = clock.TodayFor(user.TimeZoneId);

        var todayStat = await dailyStats.GetAsync(user.Id, today, ct);
        var recent = await dailyStats.GetRecentAsync(user.Id, StreakWindowDays, ct);

        // Серия считается в поясе самого пользователя: «сегодня» у него
        // и на сервере — разные даты, и напоминание приходит как раз вечером,
        // когда расхождение даёт разный ответ.
        var streak = StreakCalculator.Calculate(StreakCalculator.ActiveDates(recent), today);

        return new TelegramDigestDto(
            today,
            await cardStates.CountDueAsync(user.Id, clock.UtcNow, ct),
            todayStat?.ReviewedCount ?? 0,
            settings.DailyGoal,
            streak.Current,
            await PickWordAsync(user, ct));
    }

    /// <summary>
    /// Выбирает слово дня.
    /// </summary>
    /// <remarks>
    /// Порядок предпочтений не случаен. Сначала просроченное повторение — это
    /// то, ради чего напоминание и отправляется. Потом ближайшее будущее:
    /// показать его полезнее, чем случайное слово. И только если человек ещё
    /// ничего не начал, берётся слово из словаря — иначе первое же напоминание
    /// пришло бы пустым, а это худший первый опыт из возможных.
    /// </remarks>
    private async Task<WordOfTheDayDto?> PickWordAsync(User user, CancellationToken ct)
    {
        var now = clock.UtcNow;

        var due = await cardStates.GetDueAsync(user.Id, now, 1, ct: ct);

        if (due.Count > 0)
        {
            return await DescribeAsync(due[0].WordId, WordOfDaySource.Review, ct);
        }

        var soonest = await cardStates.GetSoonestAsync(user.Id, 1, ct: ct);

        if (soonest.Count > 0)
        {
            return await DescribeAsync(soonest[0].WordId, WordOfDaySource.Upcoming, ct);
        }

        var total = await words.CountAllAsync(ct);

        if (total == 0)
        {
            return null;
        }

        // Одно слово на человека на весь день: выбор выводится из пары
        // «пользователь + дата», а не из случайного числа, поэтому повторный
        // вызов в тот же день вернёт то же слово.
        var offset = (int)(StableHash(user.Id, clock.TodayFor(user.TimeZoneId)) % (uint)total);

        var fromDictionary = await words.GetWithDetailsByOffsetAsync(offset, ct);

        return fromDictionary is null ? null : Describe(fromDictionary, WordOfDaySource.Dictionary);
    }

    private async Task<WordOfTheDayDto?> DescribeAsync(int wordId, WordOfDaySource source, CancellationToken ct)
    {
        var loaded = await words.GetWithDetailsByIdsAsync([wordId], ct);

        return loaded.TryGetValue(wordId, out var word) ? Describe(word, source) : null;
    }

    private static WordOfTheDayDto Describe(Word word, WordOfDaySource source)
    {
        var translation = word.Translations
            .OrderByDescending(t => t.IsPrimary)
            .FirstOrDefault()?.Text ?? string.Empty;

        var example = word.Examples.FirstOrDefault();

        return new WordOfTheDayDto(
            word.Id,
            word.Lemma,
            word.PartOfSpeech,
            word.IpaUk,
            word.IpaUs,
            translation,
            example?.TextEn,
            example?.TextRu,
            source);
    }

    /// <summary>
    /// Устойчивый хеш пары «пользователь + дата».
    /// </summary>
    /// <remarks>
    /// FNV-1a написан здесь руками, а не взят из <c>string.GetHashCode</c>:
    /// встроенный хеш рандомизирован между запусками процесса, и «слово дня»
    /// менялось бы при каждом перезапуске сервиса, оставаясь тем же в базе.
    /// </remarks>
    private static uint StableHash(int userId, DateOnly date)
    {
        unchecked
        {
            var hash = 2166136261u;

            foreach (var b in BitConverter.GetBytes(userId))
            {
                hash = (hash ^ b) * 16777619u;
            }

            return (hash ^ (uint)date.DayNumber) * 16777619u;
        }
    }
}
