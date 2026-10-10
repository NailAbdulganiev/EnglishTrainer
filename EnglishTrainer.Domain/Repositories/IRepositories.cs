using EnglishTrainer.Domain.Entities;

namespace EnglishTrainer.Domain.Repositories;

/// <summary>Хранилище слов и колод (общий справочник, не привязан к пользователю).</summary>
public interface IWordRepository
{
    Task<Word?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Слово вместе с переводами и примерами — для карточки слова.</summary>
    Task<Word?> GetWithDetailsAsync(int id, CancellationToken ct = default);

    Task<Word?> GetByLemmaAsync(string lemma, string partOfSpeech, CancellationToken ct = default);

    /// <summary>
    /// Слова колоды. <paramref name="includeDetails"/> управляет загрузкой переводов
    /// и примеров: для списка они не нужны, для карточки обязательны.
    /// </summary>
    Task<IReadOnlyList<Word>> GetByDeckAsync(int deckId, bool includeDetails, CancellationToken ct = default);

    /// <summary>
    /// Слова по идентификаторам одним запросом, с переводами и примерами.
    /// Нужен, чтобы не делать N+1 при сборке очереди повторений.
    /// </summary>
    Task<IReadOnlyDictionary<int, Word>> GetWithDetailsByIdsAsync(
        IReadOnlyCollection<int> ids, CancellationToken ct = default);

    /// <summary>
    /// Слова колоды, которых у пользователя ещё нет в изучении, в порядке полезности.
    /// Основа для отбора «новых» карточек в сессии.
    /// </summary>
    Task<IReadOnlyList<Word>> GetNewWordsForUserAsync(
        int userId, int deckId, int limit, CancellationToken ct = default);

    /// <summary>
    /// Слова-соседи по колоде: нужны как правдоподобные неверные варианты
    /// в упражнении с выбором.
    /// </summary>
    Task<IReadOnlyList<Word>> GetDistractorsAsync(int excludeWordId, int limit, CancellationToken ct = default);

    /// <summary>Сколько слов в колоде — для расчёта прогресса.</summary>
    Task<int> CountInDeckAsync(int deckId, CancellationToken ct = default);

    /// <summary>Идентификаторы слов колоды — для подсчёта прогресса пользователя.</summary>
    Task<IReadOnlyList<int>> GetWordIdsInDeckAsync(int deckId, CancellationToken ct = default);

    Task AddRangeAsync(IEnumerable<Word> words, CancellationToken ct = default);
}

/// <summary>Хранилище колод.</summary>
public interface IDeckRepository
{
    Task<IReadOnlyList<Deck>> GetAllAsync(CancellationToken ct = default);

    Task<Deck?> GetBySlugAsync(string slug, CancellationToken ct = default);

    Task<Deck?> GetByIdAsync(int id, CancellationToken ct = default);

    Task AddAsync(Deck deck, CancellationToken ct = default);
}

/// <summary>Хранилище пользователей.</summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<User?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default);

    Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct = default);

    Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);

    Task<User?> GetByTelegramIdAsync(long telegramId, CancellationToken ct = default);

    /// <summary>Пользователь вместе с настройками — типовой сценарий для API.</summary>
    Task<User?> GetWithSettingsAsync(int userId, CancellationToken ct = default);

    Task AddAsync(User user, CancellationToken ct = default);

    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct = default);

    Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// Поиск людей по началу ника — чтобы добавить в друзья, нужно сначала найти.
    /// Совпадение ищется с начала строки: так работает индекс по нику.
    /// </summary>
    Task<IReadOnlyList<User>> SearchByUsernameAsync(string query, int limit, CancellationToken ct = default);
}

/// <summary>Хранилище избранных слов.</summary>
public interface IFavoriteWordRepository
{
    /// <summary>Идентификаторы избранных слов пользователя — для отметок в интерфейсе.</summary>
    Task<IReadOnlyCollection<int>> GetWordIdsAsync(int userId, CancellationToken ct = default);

    Task<bool> ExistsAsync(int userId, int wordId, CancellationToken ct = default);

    Task AddAsync(FavoriteWord favorite, CancellationToken ct = default);

    /// <summary>Убирает слово из избранного. Возвращает <c>false</c>, если его там не было.</summary>
    Task<bool> RemoveAsync(int userId, int wordId, CancellationToken ct = default);

    /// <summary>Избранные слова вместе со словом и состоянием повторения.</summary>
    Task<IReadOnlyList<FavoriteWord>> GetWithWordsAsync(int userId, CancellationToken ct = default);

    Task<int> CountAsync(int userId, CancellationToken ct = default);
}

/// <summary>Хранилище заявок в друзья.</summary>
public interface IFriendshipRepository
{
    /// <summary>Заявка между двумя пользователями в любом направлении.</summary>
    Task<Friendship?> GetBetweenAsync(int firstUserId, int secondUserId, CancellationToken ct = default);

    Task AddAsync(Friendship friendship, CancellationToken ct = default);

    Task RemoveAsync(Friendship friendship, CancellationToken ct = default);

    /// <summary>Принятые друзья. Оба пользователя загружаются сразу.</summary>
    Task<IReadOnlyList<Friendship>> GetAcceptedAsync(int userId, CancellationToken ct = default);

    /// <summary>Входящие заявки, ожидающие ответа.</summary>
    Task<IReadOnlyList<Friendship>> GetIncomingAsync(int userId, CancellationToken ct = default);

    /// <summary>Исходящие заявки, ожидающие ответа.</summary>
    Task<IReadOnlyList<Friendship>> GetOutgoingAsync(int userId, CancellationToken ct = default);

    Task<int> CountAcceptedAsync(int userId, CancellationToken ct = default);
}

/// <summary>Хранилище состояний повторения.</summary>
public interface ICardStateRepository
{
    Task<CardState?> GetAsync(int userId, int wordId, CancellationToken ct = default);

    /// <summary>
    /// Карточки, которые пора повторить, — самые просроченные первыми.
    /// Это самый горячий запрос системы: под него нужен индекс (UserId, DueAt).
    /// </summary>
    Task<IReadOnlyList<CardState>> GetDueAsync(
        int userId, DateTime nowUtc, int limit, CancellationToken ct = default);

    Task<int> CountDueAsync(int userId, DateTime nowUtc, CancellationToken ct = default);

    Task<IReadOnlyList<CardState>> GetForWordsAsync(
        int userId, IReadOnlyCollection<int> wordIds, CancellationToken ct = default);

    Task AddAsync(CardState cardState, CancellationToken ct = default);

    Task AddRangeAsync(IEnumerable<CardState> cardStates, CancellationToken ct = default);

    /// <summary>Сводка по всем карточкам пользователя для страницы прогресса.</summary>
    Task<CardStateSummary> GetSummaryAsync(int userId, DateTime nowUtc, CancellationToken ct = default);
}

/// <summary>Агрегированные показатели прогресса пользователя.</summary>
/// <param name="Total">Всего карточек в изучении.</param>
/// <param name="Learned">Выучено (интервал больше месяца).</param>
/// <param name="DueNow">Пора повторить прямо сейчас.</param>
public readonly record struct CardStateSummary(int Total, int Learned, int DueNow);

/// <summary>Хранилище журнала ответов.</summary>
public interface IReviewLogRepository
{
    Task AddAsync(ReviewLog log, CancellationToken ct = default);

    /// <summary>Сколько всего ответов дал пользователь за всё время.</summary>
    Task<int> CountAsync(int userId, CancellationToken ct = default);

    /// <summary>История по слову — для карточки «как я учил это слово».</summary>
    Task<IReadOnlyList<ReviewLog>> GetForWordAsync(int userId, int wordId, CancellationToken ct = default);

    /// <summary>
    /// Кривая удержания за период: сколько ответов и сколько из них правильные.
    /// Единственный честный показатель того, работает ли методика.
    /// </summary>
    Task<IReadOnlyList<RetentionPoint>> GetRetentionAsync(
        int userId, DateTime fromUtc, CancellationToken ct = default);
}

/// <summary>Точка кривой удержания за день.</summary>
public readonly record struct RetentionPoint(DateOnly Date, int Reviewed, int Correct);

/// <summary>Хранилище сессий обучения.</summary>
public interface IStudySessionRepository
{
    /// <summary>Активная сессия пользователя, если есть.</summary>
    Task<StudySession?> GetActiveAsync(int userId, CancellationToken ct = default);

    /// <summary>Активная сессия вместе с очередью, словами и переводами — для отдачи в API.</summary>
    Task<StudySession?> GetActiveWithItemsAsync(int userId, CancellationToken ct = default);

    Task AddAsync(StudySession session, CancellationToken ct = default);
}

/// <summary>Хранилище дневной статистики.</summary>
public interface IDailyStatRepository
{
    Task<DailyStat?> GetAsync(int userId, DateOnly date, CancellationToken ct = default);

    Task<IReadOnlyList<DailyStat>> GetRangeAsync(
        int userId, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task AddAsync(DailyStat stat, CancellationToken ct = default);

    /// <summary>Последние <paramref name="days"/> дней, свежие первыми — для серии дней.</summary>
    Task<IReadOnlyList<DailyStat>> GetRecentAsync(int userId, int days, CancellationToken ct = default);
}

/// <summary>
/// Единица работы. Нужна, чтобы Application-слой завершал транзакцию,
/// не зная ничего про DbContext и SaveChanges.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
