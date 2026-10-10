using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Repositories;
using EnglishTrainer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishTrainer.Infrastructure.Repositories;

/// <summary>Токены сброса пароля.</summary>
public sealed class PasswordResetTokenRepository(AppDbContext context) : IPasswordResetTokenRepository
{
    public Task<PasswordResetToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default) =>
        // Без AsNoTracking: найденный токен помечается использованным.
        context.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task<IReadOnlyList<PasswordResetToken>> GetActiveAsync(
        int userId, DateTime nowUtc, CancellationToken ct = default) =>
        await context.PasswordResetTokens
            .Where(t => t.UserId == userId && t.UsedAt == null && t.ExpiresAt > nowUtc)
            .ToListAsync(ct);

    public async Task AddAsync(PasswordResetToken token, CancellationToken ct = default) =>
        await context.PasswordResetTokens.AddAsync(token, ct);
}
