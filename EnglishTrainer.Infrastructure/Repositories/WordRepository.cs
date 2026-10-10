using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Repositories;
using EnglishTrainer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishTrainer.Infrastructure.Repositories;

/// <summary>Слова и колоды. Справочные данные, только чтение (кроме импорта).</summary>
public sealed class WordRepository(AppDbContext context) : IWordRepository
{
    public Task<Word?> GetByIdAsync(int id, CancellationToken ct = default) =>
        context.Words.FirstOrDefaultAsync(w => w.Id == id, ct);

    public Task<Word?> GetWithDetailsAsync(int id, CancellationToken ct = default) =>
        context.Words
            .Include(w => w.Translations)
            .Include(w => w.Examples)
            // Две коллекции в одном запросе дают декартово произведение строк:
            // переводы × примеры. Раздельные запросы дешевле.
            .AsSplitQuery()
            .FirstOrDefaultAsync(w => w.Id == id, ct);

    public Task<Word?> GetByLemmaAsync(string lemma, string partOfSpeech, CancellationToken ct = default) =>
        context.Words.FirstOrDefaultAsync(w => w.Lemma == lemma && w.PartOfSpeech == partOfSpeech, ct);

    public async Task<IReadOnlyList<Word>> GetByDeckAsync(
        int deckId, bool includeDetails, CancellationToken ct = default)
    {
        // Порядок слов хранится в связи колоды, поэтому сначала читается он.
        var orderedIds = await context.DeckWords
            .Where(dw => dw.DeckId == deckId)
            .OrderBy(dw => dw.Order)
            .Select(dw => dw.WordId)
            .ToListAsync(ct);

        if (orderedIds.Count == 0)
            return [];

        // Отбор идёт от Words, а не от связей колоды. Причина техническая:
        // Include нельзя применять после Select, который проецирует другой тип,
        // а отбор через DeckWords требует именно такой проекции.
        var query = context.Words.Where(w => orderedIds.Contains(w.Id));

        query = includeDetails
            ? query.Include(w => w.Translations).Include(w => w.Examples).AsSplitQuery()
            // Для списка переводы нужны, а примеры — нет: экономим трафик и память.
            : query.Include(w => w.Translations);

        var words = await query.AsNoTracking().ToListAsync(ct);

        // Contains не задаёт порядок результата, поэтому он восстанавливается явно.
        return [.. orderedIds.Select(id => words.Find(w => w.Id == id)).OfType<Word>()];
    }

    public async Task<IReadOnlyDictionary<int, Word>> GetWithDetailsByIdsAsync(
        IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return new Dictionary<int, Word>();

        // Один запрос на все слова вместо N обращений — именно это убирает
        // проблему N+1 при сборке очереди повторений.
        var list = await context.Words
            .Include(w => w.Translations)
            .Include(w => w.Examples)
            .Where(w => ids.Contains(w.Id))
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync(ct);

        return list.ToDictionary(w => w.Id);
    }

    public async Task<IReadOnlyList<Word>> GetNewWordsForUserAsync(
        int userId, int deckId, int limit, CancellationToken ct = default)
    {
        if (limit <= 0)
            return [];

        // Слова колоды, по которым у пользователя ещё нет состояния карточки.
        // Сортировка по частотному рангу: первыми идут самые употребительные,
        // а не случайные. NULL в конце — слова без ранга наименее приоритетны.
        // Когда ранга нет ни у кого (словарь загружен из файлов), порядок задаёт
        // идентификатор, то есть последовательность внутри файла колоды.
        return await context.Words
            .Where(w => context.DeckWords.Any(dw => dw.DeckId == deckId && dw.WordId == w.Id)
                        && !context.CardStates.Any(cs => cs.UserId == userId && cs.WordId == w.Id))
            .OrderBy(w => w.FrequencyRank == null)
            .ThenBy(w => w.FrequencyRank)
            .ThenBy(w => w.Id)
            .Take(limit)
            .Include(w => w.Translations)
            .Include(w => w.Examples)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Word>> GetDistractorsAsync(
        int excludeWordId, int limit, CancellationToken ct = default)
    {
        if (limit <= 0)
            return [];

        // Берём слова сопоставимой частотности: редкое слово в качестве неверного
        // варианта выдаёт правильный ответ с первого взгляда.
        var reference = await context.Words
            .Where(w => w.Id == excludeWordId)
            .Select(w => new { w.FrequencyRank, w.PartOfSpeech })
            .FirstOrDefaultAsync(ct);

        var query = context.Words
            .Include(w => w.Translations)
            .Where(w => w.Id != excludeWordId);

        if (reference?.PartOfSpeech is { Length: > 0 } partOfSpeech)
            query = query.Where(w => w.PartOfSpeech == partOfSpeech);

        if (reference?.FrequencyRank is { } rank)
        {
            var from = rank - 1500;
            var to = rank + 1500;
            query = query.Where(w => w.FrequencyRank != null
                                     && w.FrequencyRank >= from
                                     && w.FrequencyRank <= to);
        }

        return await query
            .OrderBy(w => w.FrequencyRank == null)
            .ThenBy(w => w.FrequencyRank)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public Task<int> CountInDeckAsync(int deckId, CancellationToken ct = default) =>
        context.DeckWords.CountAsync(dw => dw.DeckId == deckId, ct);

    public async Task<IReadOnlyList<int>> GetWordIdsInDeckAsync(int deckId, CancellationToken ct = default) =>
        await context.DeckWords
            .Where(dw => dw.DeckId == deckId)
            .Select(dw => dw.WordId)
            .ToListAsync(ct);

    public async Task AddRangeAsync(IEnumerable<Word> words, CancellationToken ct = default)
    {
        await context.Words.AddRangeAsync(words, ct);
    }
}

/// <summary>Колоды.</summary>
public sealed class DeckRepository(AppDbContext context) : IDeckRepository
{
    public async Task<IReadOnlyList<Deck>> GetAllAsync(CancellationToken ct = default) =>
        await context.Decks
            .OrderBy(d => d.SortOrder)
            .AsNoTracking()
            .ToListAsync(ct);

    public Task<Deck?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        context.Decks.FirstOrDefaultAsync(d => d.Slug == slug, ct);

    public Task<Deck?> GetByIdAsync(int id, CancellationToken ct = default) =>
        context.Decks.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task AddAsync(Deck deck, CancellationToken ct = default)
    {
        await context.Decks.AddAsync(deck, ct);
    }
}

/// <summary>Пользователи и настройки.</summary>
public sealed class UserRepository(AppDbContext context) : IUserRepository
{
    public Task<User?> GetByIdAsync(int id, CancellationToken ct = default) =>
        context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default) =>
        context.Users.FirstOrDefaultAsync(u => u.PublicId == publicId, ct);

    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct = default) =>
        context.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

    public Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default) =>
        context.Users.FirstOrDefaultAsync(u => u.Username == username, ct);

    public Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default) =>
        context.Users.AnyAsync(u => u.Username == username, ct);

    public async Task<IReadOnlyList<User>> SearchByUsernameAsync(
        string query, int limit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || limit <= 0)
            return [];

        // Совпадение ищется с начала ника, а не «содержит»: такой запрос
        // опирается на индекс по нику и не деградирует с ростом числа людей.
        return await context.Users
            .Where(u => u.Username.StartsWith(query))
            .OrderBy(u => u.Username)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public Task<User?> GetByTelegramIdAsync(long telegramId, CancellationToken ct = default) =>
        context.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId, ct);

    public Task<User?> GetWithSettingsAsync(int userId, CancellationToken ct = default) =>
        context.Users
            .Include(u => u.Settings)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

    public async Task AddAsync(User user, CancellationToken ct = default)
    {
        await context.Users.AddAsync(user, ct);
    }

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct = default) =>
        context.Users.AnyAsync(u => u.Email == normalizedEmail, ct);
}
