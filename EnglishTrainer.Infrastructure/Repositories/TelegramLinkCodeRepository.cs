using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Repositories;
using EnglishTrainer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishTrainer.Infrastructure.Repositories;

/// <summary>Коды привязки Telegram к аккаунту.</summary>
public sealed class TelegramLinkCodeRepository(AppDbContext context) : ITelegramLinkCodeRepository
{
    public Task<TelegramLinkCode?> GetByHashAsync(string codeHash, CancellationToken ct = default) =>
        // Без AsNoTracking: найденный код помечается использованным.
        context.TelegramLinkCodes.FirstOrDefaultAsync(c => c.CodeHash == codeHash, ct);

    public async Task<IReadOnlyList<TelegramLinkCode>> GetActiveAsync(
        int userId, DateTime nowUtc, CancellationToken ct = default) =>
        await context.TelegramLinkCodes
            .Where(c => c.UserId == userId && c.UsedAt == null && c.ExpiresAt > nowUtc)
            .ToListAsync(ct);

    public async Task AddAsync(TelegramLinkCode code, CancellationToken ct = default) =>
        await context.TelegramLinkCodes.AddAsync(code, ct);
}
