using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Enums;
using EnglishTrainer.Domain.Repositories;
using EnglishTrainer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishTrainer.Infrastructure.Repositories;

/// <summary>Состояния карточек: главный источник данных для сборки сессии.</summary>
public sealed class CardStateRepository(AppDbContext context) : ICardStateRepository
{
    public Task<CardState?> GetAsync(int userId, int wordId, CancellationToken ct = default) =>
        context.CardStates.FirstOrDefaultAsync(c => c.UserId == userId && c.WordId == wordId, ct);

    public async Task<IReadOnlyList<CardState>> GetDueAsync(
        int userId, DateTime nowUtc, int limit, CancellationToken ct = default)
    {
        if (limit <= 0)
            return [];

        // ВАЖНО: сортировка по DueAt (когда карточка стала доступна), а не по
        // IntervalDays. Сортировка по интервалу дала бы обратный порядок:
        // давно выученные слова с большим интервалом вытеснили бы свежие.
        // Именно под этот запрос создан индекс (UserId, DueAt).
        return await context.CardStates
            .Where(c => c.UserId == userId && c.DueAt <= nowUtc)
            .OrderBy(c => c.DueAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public Task<int> CountDueAsync(int userId, DateTime nowUtc, CancellationToken ct = default) =>
        context.CardStates.CountAsync(c => c.UserId == userId && c.DueAt <= nowUtc, ct);

    public async Task<IReadOnlyList<CardState>> GetForWordsAsync(
        int userId, IReadOnlyCollection<int> wordIds, CancellationToken ct = default)
    {
        if (wordIds.Count == 0)
            return [];

        return await context.CardStates
            .Where(c => c.UserId == userId && wordIds.Contains(c.WordId))
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task AddAsync(CardState cardState, CancellationToken ct = default)
    {
        await context.CardStates.AddAsync(cardState, ct);
    }

    public async Task AddRangeAsync(IEnumerable<CardState> cardStates, CancellationToken ct = default)
    {
        await context.CardStates.AddRangeAsync(cardStates, ct);
    }

    public async Task<CardStateSummary> GetSummaryAsync(
        int userId, DateTime nowUtc, CancellationToken ct = default)
    {
        var learnedThreshold = nowUtc.AddDays(CardState.LearnedIntervalDays);

        // Порог «выучено» вычисляется на сервере: пересылать DueAt всех карточек
        // ради одной цифры — заведомо плохая идея.
        // SingleOrDefault, а не FirstOrDefault: группировка без условия даёт ровно
        // одну строку (или ни одной). First здесь ещё и вызывал предупреждение EF
        // о запросе без OrderBy — на каждый запрос статистики.
        var row = await context.CardStates
            .Where(c => c.UserId == userId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Learned = g.Count(c => c.DueAt > learnedThreshold
                                       && c.Phase == CardPhase.Review),
                DueNow = g.Count(c => c.DueAt <= nowUtc)
            })
            .SingleOrDefaultAsync(ct);

        return row is null
            ? new CardStateSummary(0, 0, 0)
            : new CardStateSummary(row.Total, row.Learned, row.DueNow);
    }
}
