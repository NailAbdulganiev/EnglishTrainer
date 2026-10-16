using EnglishTrainer.Application.Dtos;
using EnglishTrainer.Domain.Abstractions;
using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Exceptions;
using EnglishTrainer.Domain.Repositories;
using EnglishTrainer.Domain.Stats;

namespace EnglishTrainer.Application.Services;

/// <summary>
/// Недельный рейтинг: я и мои друзья.
/// </summary>
/// <remarks>
/// <para>
/// Рейтинг строится по уже собранной дневной статистике, а не по отдельным
/// счётчикам для соревнования. Причина та же, что и у достижений: два источника
/// одного числа расходятся, и «у меня больше ответов, а я ниже» — ровно тот
/// вид поломки, который подрывает доверие ко всему экрану.
/// </para>
/// <para>
/// Друзья, а не все пользователи: рейтинг среди незнакомых людей наказывает
/// новичка за то, что он новичок, и мотивирует ровно наоборот задуманному.
/// </para>
/// </remarks>
public sealed class LeaderboardService(
    IUserRepository users,
    IFriendshipRepository friendships,
    IDailyStatRepository dailyStats,
    IClock clock)
{
    /// <summary>Глубина истории для подсчёта серии дней — та же, что в профиле.</summary>
    private const int StreakWindowDays = 180;

    /// <summary>
    /// Запас к окну истории, в днях.
    /// </summary>
    /// <remarks>
    /// Участники живут в разных часовых поясах, и «сегодня» у них отличается
    /// на сутки. Общий запрос берёт окно с запасом в обе стороны, а точные
    /// границы каждый участник получает уже в своём поясе — иначе у друга
    /// в UTC+13 пропал бы сегодняшний день, а у друга в UTC−11 — вчерашний.
    /// </remarks>
    private const int TimeZoneMarginDays = 2;

    public async Task<LeaderboardDto> GetAsync(int userId, CancellationToken ct = default)
    {
        var me = await users.GetByIdAsync(userId, ct)
            ?? throw NotFoundException.For("Пользователь", userId);

        var accepted = await friendships.GetAcceptedAsync(userId, ct);

        // Друг определяется по тому, с какой стороны запись о дружбе:
        // в строке лежат обе стороны, и «кто из них я» видно по RequesterId.
        var friends = accepted
            .Select(friendship => friendship.RequesterId == userId
                ? friendship.Addressee
                : friendship.Requester)
            .OfType<User>()
            .Where(friend => friend.Id != userId)
            .GroupBy(friend => friend.Id)
            .Select(group => group.First())
            .ToList();

        var participants = new List<User>(friends.Count + 1) { me };
        participants.AddRange(friends);

        var today = clock.TodayFor(me.TimeZoneId);
        var history = await dailyStats.GetRangeForUsersAsync(
            [.. participants.Select(participant => participant.Id)],
            today.AddDays(-(StreakWindowDays + TimeZoneMarginDays)),
            today.AddDays(TimeZoneMarginDays),
            ct);

        var byUser = history
            .GroupBy(stat => stat.UserId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<DailyStat>)[.. group]);

        var players = participants
            .Select(participant =>
            {
                var participantToday = clock.TodayFor(participant.TimeZoneId);
                var weekFrom = participantToday.AddDays(-(LeaderboardRanking.WindowDays - 1));

                var recent = byUser.TryGetValue(participant.Id, out var rows) ? rows : [];

                return new LeaderboardPlayer(
                    participant.Id,
                    participant.Username,
                    participant.DisplayName,
                    participant.AvatarUrl,
                    LeaderboardRanking.ScoreFor(recent, weekFrom, participantToday, participantToday));
            })
            .ToList();

        var ranked = LeaderboardRanking.Rank(players);

        return new LeaderboardDto(
            today.AddDays(-(LeaderboardRanking.WindowDays - 1)),
            today,
            friends.Count,
            [.. ranked.Select(player => new LeaderboardEntryDto(
                player.Rank,
                player.Username,
                player.DisplayName,
                player.AvatarUrl,
                player.UserId == userId,
                player.Score.WeeklyWords,
                player.Score.WeeklyReviews,
                player.Score.CurrentStreak))]);
    }
}
