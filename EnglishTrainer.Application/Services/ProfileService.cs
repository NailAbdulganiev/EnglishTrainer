using EnglishTrainer.Application.Dtos;
using EnglishTrainer.Application.Mapping;
using EnglishTrainer.Application.Text;
using EnglishTrainer.Domain.Abstractions;
using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Exceptions;
using EnglishTrainer.Domain.Repositories;
using EnglishTrainer.Domain.Stats;

namespace EnglishTrainer.Application.Services;

/// <summary>Профиль: кто пользователь, как его найти и как у него дела.</summary>
public sealed class ProfileService(
    IUserRepository users,
    ICardStateRepository cardStates,
    IDailyStatRepository dailyStats,
    IReviewLogRepository reviewLogs,
    IFavoriteWordRepository favorites,
    IFriendshipRepository friendships,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    private const int StreakWindowDays = 180;
    private const int MaxDisplayNameLength = 64;

    /// <summary>Профиль вместе со сводкой прогресса — всё, что нужно странице профиля.</summary>
    public async Task<ProfileDto> GetAsync(int userId, CancellationToken ct = default)
    {
        var user = await users.GetWithSettingsAsync(userId, ct)
            ?? throw NotFoundException.For("Пользователь", userId);

        var summary = await cardStates.GetSummaryAsync(userId, clock.UtcNow, ct);
        var recent = await dailyStats.GetRecentAsync(userId, StreakWindowDays, ct);
        var streak = StreakCalculator.Calculate(
            StreakCalculator.ActiveDates(recent), clock.TodayFor(user.TimeZoneId));

        return new ProfileDto(
            user.ToDto(),
            (user.Settings ?? new UserSettings()).ToDto(),
            summary.Learned,
            summary.Total,
            Math.Max(0, summary.Total - summary.Learned),
            streak.Current,
            streak.Longest,
            await favorites.CountAsync(userId, ct),
            await friendships.CountAcceptedAsync(userId, ct),
            await reviewLogs.CountAsync(userId, ct));
    }

    /// <summary>
    /// Меняет ник и отображаемое имя.
    /// </summary>
    /// <remarks>
    /// Ник — это адрес, по которому человека находят: занятый ник отклоняется
    /// с понятной ошибкой, а не подменяется «похожим». Иначе пользователь
    /// не найдёт себя по тому нику, который только что вводил.
    /// </remarks>
    public async Task<UserDto> UpdateAsync(
        int userId, UpdateProfileRequestDto request, CancellationToken ct = default)
    {
        var user = await users.GetByIdAsync(userId, ct)
            ?? throw NotFoundException.For("Пользователь", userId);

        if (!string.IsNullOrWhiteSpace(request.Username))
        {
            var username = UsernamePolicy.Require(request.Username);

            if (!string.Equals(username, user.Username, StringComparison.Ordinal))
            {
                if (await users.UsernameExistsAsync(username, ct))
                    throw new ConflictException($"Ник «{username}» уже занят.");

                user.Username = username;
            }
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            var displayName = request.DisplayName.Trim();

            if (displayName.Length > MaxDisplayNameLength)
                throw new ValidationException($"Отображаемое имя длиннее {MaxDisplayNameLength} символов.");

            user.DisplayName = displayName;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return user.ToDto();
    }
}
