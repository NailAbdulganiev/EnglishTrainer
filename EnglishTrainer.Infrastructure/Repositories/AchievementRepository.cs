using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Repositories;
using EnglishTrainer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishTrainer.Infrastructure.Repositories;

/// <summary>Полученные достижения.</summary>
public sealed class AchievementRepository(AppDbContext context) : IUserAchievementRepository
{
    public async Task<IReadOnlyCollection<string>> GetCodesAsync(
        int userId, CancellationToken ct = default) =>
        await context.UserAchievements
            .Where(a => a.UserId == userId)
            .Select(a => a.Code)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<UserAchievement>> GetAsync(
        int userId, CancellationToken ct = default) =>
        await context.UserAchievements
            .Where(a => a.UserId == userId)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<int> UnlockAsync(
        int userId, IReadOnlyCollection<string> codes, DateTime unlockedAt, CancellationToken ct = default)
    {
        var unlocked = 0;

        foreach (var code in codes)
        {
            // ON CONFLICT DO NOTHING вместо обычной вставки: проверка достижений
            // идёт после каждого ответа, и два одновременных запроса — например,
            // браузер и мини-апп — иначе получили бы нарушение уникального
            // индекса вместо того же самого результата. Повторная выдача награды
            // не ошибка, а нормальный исход гонки.
            unlocked += await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO user_achievements (user_id, code, unlocked_at, created_at)
                VALUES ({userId}, {code}, {unlockedAt}, {unlockedAt})
                ON CONFLICT (user_id, code) DO NOTHING
                """, ct);
        }

        return unlocked;
    }
}
