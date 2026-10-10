using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Enums;
using EnglishTrainer.Domain.Repositories;
using EnglishTrainer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishTrainer.Infrastructure.Repositories;

/// <summary>Избранные слова: приоритетный список для повторения.</summary>
public sealed class FavoriteWordRepository(AppDbContext context) : IFavoriteWordRepository
{
    public async Task<IReadOnlyCollection<int>> GetWordIdsAsync(int userId, CancellationToken ct = default) =>
        await context.FavoriteWords
            .Where(f => f.UserId == userId)
            .Select(f => f.WordId)
            .ToListAsync(ct);

    public Task<bool> ExistsAsync(int userId, int wordId, CancellationToken ct = default) =>
        context.FavoriteWords.AnyAsync(f => f.UserId == userId && f.WordId == wordId, ct);

    public async Task AddAsync(FavoriteWord favorite, CancellationToken ct = default) =>
        await context.FavoriteWords.AddAsync(favorite, ct);

    public async Task<bool> RemoveAsync(int userId, int wordId, CancellationToken ct = default)
    {
        var favorite = await context.FavoriteWords
            .FirstOrDefaultAsync(f => f.UserId == userId && f.WordId == wordId, ct);

        if (favorite is null)
            return false;

        context.FavoriteWords.Remove(favorite);
        return true;
    }

    public async Task<IReadOnlyList<FavoriteWord>> GetWithWordsAsync(
        int userId, CancellationToken ct = default) =>
        await context.FavoriteWords
            .Where(f => f.UserId == userId)
            .Include(f => f.Word!).ThenInclude(w => w.Translations)
            .Include(f => f.Word!).ThenInclude(w => w.Examples)
            // Свежие отметки — первыми: чаще всего пользователь хочет вернуться
            // к тому, что отметил только что.
            .OrderByDescending(f => f.CreatedAt)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync(ct);

    public Task<int> CountAsync(int userId, CancellationToken ct = default) =>
        context.FavoriteWords.CountAsync(f => f.UserId == userId, ct);
}

/// <summary>Заявки в друзья.</summary>
public sealed class FriendshipRepository(AppDbContext context) : IFriendshipRepository
{
    public Task<Friendship?> GetBetweenAsync(
        int firstUserId, int secondUserId, CancellationToken ct = default) =>
        // Заявка отслеживается контекстом: её состояние меняется при принятии.
        context.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterId == firstUserId && f.AddresseeId == secondUserId) ||
            (f.RequesterId == secondUserId && f.AddresseeId == firstUserId), ct);

    public async Task AddAsync(Friendship friendship, CancellationToken ct = default) =>
        await context.Friendships.AddAsync(friendship, ct);

    public Task RemoveAsync(Friendship friendship, CancellationToken ct = default)
    {
        context.Friendships.Remove(friendship);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Friendship>> GetAcceptedAsync(
        int userId, CancellationToken ct = default) =>
        await WithUsers(context.Friendships.Where(f =>
                f.Status == FriendshipStatus.Accepted &&
                (f.RequesterId == userId || f.AddresseeId == userId)))
            .OrderByDescending(f => f.RespondedAt ?? f.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Friendship>> GetIncomingAsync(
        int userId, CancellationToken ct = default) =>
        await WithUsers(context.Friendships.Where(f =>
                f.Status == FriendshipStatus.Pending && f.AddresseeId == userId))
            .OrderBy(f => f.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Friendship>> GetOutgoingAsync(
        int userId, CancellationToken ct = default) =>
        await WithUsers(context.Friendships.Where(f =>
                f.Status == FriendshipStatus.Pending && f.RequesterId == userId))
            .OrderBy(f => f.CreatedAt)
            .ToListAsync(ct);

    public Task<int> CountAcceptedAsync(int userId, CancellationToken ct = default) =>
        context.Friendships.CountAsync(f =>
            f.Status == FriendshipStatus.Accepted &&
            (f.RequesterId == userId || f.AddresseeId == userId), ct);

    /// <summary>
    /// Подтягивает обоих участников заявки.
    /// </summary>
    /// <remarks>
    /// Без этого не показать ни ник, ни отображаемое имя, а ленивая загрузка
    /// превратила бы список друзей в N+1 запросов.
    /// </remarks>
    private static IQueryable<Friendship> WithUsers(IQueryable<Friendship> query) =>
        query
            .Include(f => f.Requester!)
            .Include(f => f.Addressee!)
            .AsNoTracking();
}
