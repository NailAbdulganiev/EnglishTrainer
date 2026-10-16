using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Enums;
using EnglishTrainer.Infrastructure.Persistence;

namespace EnglishTrainer.IntegrationTests.Database;

/// <summary>
/// Данные для интеграционных тестов.
/// </summary>
/// <remarks>
/// <para>
/// Каждый тест заводит свои данные. Импортированный словарь для этого не годится:
/// он поставляется вместе с приложением, и тест, опирающийся на него, ломался бы
/// от правки любого JSON-файла колоды — а такие правки к репозиториям отношения
/// не имеют.
/// </para>
/// <para>
/// Все имена уникальны: на email, ник, пару «написание + часть речи» и пару
/// «пользователь + слово» в схеме стоят уникальные индексы, и повторный прогон
/// в одной транзакции не должен упираться в собственные данные предыдущего теста.
/// </para>
/// </remarks>
internal static class TestData
{
    /// <summary>Создаёт пользователя с настройками и сохраняет его.</summary>
    public static async Task<User> AddUserAsync(
        AppDbContext context,
        string timeZoneId = "UTC",
        long? telegramId = null,
        bool remindersEnabled = true,
        DateTime? nextReminderAtUtc = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];

        var user = new User
        {
            Email = $"test-{suffix}@example.com",
            Username = $"test{suffix}",
            DisplayName = "Проверочный",
            PasswordHash = "не-используется-в-этих-тестах",
            TimeZoneId = timeZoneId,
            TelegramId = telegramId,
            TelegramLinkedAt = telegramId is null ? null : DateTime.UtcNow,
            Settings = new UserSettings
            {
                DailyReminderEnabled = remindersEnabled,
                // Напоминание ставится в прошлом: так выборка «кому пора»
                // срабатывает без ожидания.
                NextReminderAtUtc = nextReminderAtUtc ?? DateTime.UtcNow.AddMinutes(-1)
            }
        };

        await context.Users.AddAsync(user);
        await context.SaveChangesAsync();

        return user;
    }

    /// <summary>Создаёт слово с переводом и сохраняет его.</summary>
    public static async Task<Word> AddWordAsync(
        AppDbContext context,
        string? lemma = null,
        string partOfSpeech = "noun",
        string translation = "перевод",
        int? frequencyRank = null)
    {
        var word = new Word
        {
            Lemma = lemma ?? $"word{Guid.NewGuid():N}"[..12],
            PartOfSpeech = partOfSpeech,
            FrequencyRank = frequencyRank,
            Level = CefrLevel.A1,
            Translations =
            {
                new WordTranslation { LanguageCode = "ru", Text = translation, IsPrimary = true }
            }
        };

        await context.Words.AddAsync(word);
        await context.SaveChangesAsync();

        return word;
    }

    /// <summary>Создаёт колоду и, если переданы слова, связывает их с ней.</summary>
    public static async Task<Deck> AddDeckAsync(
        AppDbContext context,
        bool isSystem = true,
        int? ownerId = null,
        params int[] wordIds)
    {
        var deck = new Deck
        {
            Slug = $"deck-{Guid.NewGuid():N}"[..16],
            Title = "Проверочная колода",
            IsSystem = isSystem,
            OwnerId = ownerId
        };

        await context.Decks.AddAsync(deck);
        await context.SaveChangesAsync();

        for (var index = 0; index < wordIds.Length; index++)
        {
            await context.DeckWords.AddAsync(new DeckWord
            {
                DeckId = deck.Id,
                WordId = wordIds[index],
                Order = index
            });
        }

        await context.SaveChangesAsync();

        return deck;
    }

    /// <summary>
    /// Создаёт состояние карточки.
    /// </summary>
    /// <param name="intervalDays">
    /// Интервал повторения. По нему определяется «выучено» — порог в
    /// <see cref="CardState.LearnedIntervalDays"/>.
    /// </param>
    public static async Task<CardState> AddCardStateAsync(
        AppDbContext context,
        int userId,
        int wordId,
        double intervalDays,
        DateTime dueAtUtc,
        CardPhase phase = CardPhase.Review)
    {
        var state = new CardState
        {
            UserId = userId,
            WordId = wordId,
            Phase = phase,
            IntervalDays = intervalDays,
            DueAt = dueAtUtc,
            Stability = intervalDays,
            Difficulty = 5,
            LastReviewedAt = dueAtUtc.AddDays(-intervalDays)
        };

        await context.CardStates.AddAsync(state);
        await context.SaveChangesAsync();

        return state;
    }

    /// <summary>Создаёт дневную статистику и сохраняет её.</summary>
    public static async Task<DailyStat> AddDailyStatAsync(
        AppDbContext context,
        int userId,
        DateOnly date,
        int reviewedCount = 10,
        int newWordsStarted = 2)
    {
        var stat = new DailyStat
        {
            UserId = userId,
            Date = date,
            ReviewedCount = reviewedCount,
            CorrectCount = reviewedCount / 2,
            NewWordsStarted = newWordsStarted
        };

        await context.DailyStats.AddAsync(stat);
        await context.SaveChangesAsync();

        return stat;
    }

    /// <summary>Создаёт запись журнала ответов и сохраняет её.</summary>
    public static async Task<ReviewLog> AddReviewLogAsync(
        AppDbContext context,
        int cardStateId,
        int userId,
        int wordId,
        DateTime reviewedAtUtc,
        bool isCorrect = true)
    {
        var log = new ReviewLog
        {
            CardStateId = cardStateId,
            UserId = userId,
            WordId = wordId,
            ReviewedAt = reviewedAtUtc,
            // Правильность в журнале отдельным полем не хранится: она выводится
            // из оценки. «Забыл» — единственная оценка, означающая ошибку.
            Rating = isCorrect ? ReviewRating.Good : ReviewRating.Again,
            Exercise = ExerciseKind.Typing,
            PhaseBefore = CardPhase.Review,
            ElapsedDays = 1,
            ScheduledDays = 2,
            ElapsedMs = 1500
        };

        await context.ReviewLogs.AddAsync(log);
        await context.SaveChangesAsync();

        return log;
    }
}
