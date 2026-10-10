using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Enums;
using EnglishTrainer.Domain.Repositories;
using EnglishTrainer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishTrainer.Infrastructure.Repositories;

/// <summary>Сессии обучения вместе с очередью карточек.</summary>
public sealed class StudySessionRepository(AppDbContext context) : IStudySessionRepository
{
    public Task<StudySession?> GetActiveAsync(int userId, CancellationToken ct = default) =>
        context.StudySessions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == StudySessionStatus.Active, ct);

    public async Task AddAsync(StudySession session, CancellationToken ct = default)
    {
        await context.StudySessions.AddAsync(session, ct);
    }

    public Task<StudySession?> GetActiveWithItemsAsync(int userId, CancellationToken ct = default) =>
        context.StudySessions
            // Слова и переводы подтягиваются сразу: очередь отдаётся целиком,
            // и ленивая загрузка здесь превратилась бы в N+1.
            // Навигации объявлены nullable, поэтому в лямбдах стоит «!»:
            // для строки сессии слово существует всегда, а несколько Include
            // по одному и тому же Items неизбежны — EF умеет включать
            // вложенные коллекции только по одной за вызов.
            .Include(s => s.Items.OrderBy(i => i.Position))
                .ThenInclude(i => i.Word!.Translations)
            .Include(s => s.Items)
                .ThenInclude(i => i.Word!.Examples)
            .Include(s => s.Items)
                .ThenInclude(i => i.CardState)
            // AsSplitQuery: три коллекции в одном запросе дали бы декартово
            // произведение строк, то есть кратный рост передаваемых данных.
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == StudySessionStatus.Active, ct);
}

/// <summary>Журнал ответов — база для кривой удержания и будущего перехода на FSRS.</summary>
public sealed class ReviewLogRepository(AppDbContext context) : IReviewLogRepository
{
    public async Task AddAsync(ReviewLog log, CancellationToken ct = default)
    {
        await context.ReviewLogs.AddAsync(log, ct);
    }

    public Task<int> CountAsync(int userId, CancellationToken ct = default) =>
        context.ReviewLogs.CountAsync(l => l.UserId == userId, ct);

    public async Task<IReadOnlyList<ReviewLog>> GetForWordAsync(
        int userId, int wordId, CancellationToken ct = default) =>
        await context.ReviewLogs
            .Where(l => l.UserId == userId && l.WordId == wordId)
            .OrderByDescending(l => l.ReviewedAt)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<IReadOnlyList<RetentionPoint>> GetRetentionAsync(
        int userId, DateTime fromUtc, CancellationToken ct = default)
    {
        // Агрегация на стороне БД: тянуть все ответы за месяц в память ради
        // двух чисел — типичная ошибка, которая проявляется на реальных объёмах.
        var grouped = await context.ReviewLogs
            .Where(l => l.UserId == userId && l.ReviewedAt >= fromUtc)
            .GroupBy(l => l.ReviewedAt.Date)
            .Select(g => new
            {
                Date = g.Key,
                Reviewed = g.Count(),
                Correct = g.Count(l => l.Rating != ReviewRating.Again)
            })
            .OrderBy(x => x.Date)
            .AsNoTracking()
            .ToListAsync(ct);

        return grouped
            .Select(x => new RetentionPoint(DateOnly.FromDateTime(x.Date), x.Reviewed, x.Correct))
            .ToList();
    }
}

/// <summary>Дневная статистика: источник данных для серии дней и графиков.</summary>
public sealed class DailyStatRepository(AppDbContext context) : IDailyStatRepository
{
    public Task<DailyStat?> GetAsync(int userId, DateOnly date, CancellationToken ct = default) =>
        context.DailyStats.FirstOrDefaultAsync(s => s.UserId == userId && s.Date == date, ct);

    public async Task<IReadOnlyList<DailyStat>> GetRangeAsync(
        int userId, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        await context.DailyStats
            .Where(s => s.UserId == userId && s.Date >= from && s.Date <= to)
            .OrderBy(s => s.Date)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task AddAsync(DailyStat stat, CancellationToken ct = default)
    {
        await context.DailyStats.AddAsync(stat, ct);
    }

    public async Task<IReadOnlyList<DailyStat>> GetRecentAsync(
        int userId, int days, CancellationToken ct = default)
    {
        if (days <= 0)
            return [];

        var from = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-days);

        return await context.DailyStats
            .Where(s => s.UserId == userId && s.Date >= from)
            .OrderByDescending(s => s.Date)
            .AsNoTracking()
            .ToListAsync(ct);
    }
}

/// <summary>
/// Завершение транзакции. Application вызывает <see cref="SaveChangesAsync"/>
/// и не знает, что под этим стоит EF Core.
/// </summary>
public sealed class UnitOfWork(AppDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        context.SaveChangesAsync(ct);
}
