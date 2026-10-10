using EnglishTrainer.Application.Dtos;
using EnglishTrainer.Application.Mapping;
using EnglishTrainer.Application.Text;
using EnglishTrainer.Domain.Abstractions;
using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Enums;
using EnglishTrainer.Domain.Exceptions;
using EnglishTrainer.Domain.Repositories;
using EnglishTrainer.Domain.Stats;

namespace EnglishTrainer.Application.Services;

/// <summary>
/// Друзья: заявки по нику и прогресс друг друга.
/// </summary>
/// <remarks>
/// <para>
/// Заявка — это состояние с двумя сторонами, а не флаг «друзья». Поэтому
/// переходы выполняются только из ожидающего состояния: принять можно лишь то,
/// что тебе отправили, а повторная заявка от того же человека не создаётся.
/// </para>
/// <para>
/// Встречная заявка не считается ошибкой: если A отправил заявку B, а B в это же
/// время отправил её A, намерение обоих очевидно, и дружба оформляется сразу.
/// </para>
/// </remarks>
public sealed class FriendService(
    IUserRepository users,
    IFriendshipRepository friendships,
    ICardStateRepository cardStates,
    IDailyStatRepository dailyStats,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    /// <summary>Сколько человек показывать в результатах поиска.</summary>
    private const int SearchLimit = 10;

    /// <summary>Окно, по которому считается серия дней. Полгода — с запасом.</summary>
    private const int StreakWindowDays = 180;

    public async Task<FriendsOverviewDto> GetOverviewAsync(int userId, CancellationToken ct = default)
    {
        var accepted = await friendships.GetAcceptedAsync(userId, ct);
        var incoming = await friendships.GetIncomingAsync(userId, ct);
        var outgoing = await friendships.GetOutgoingAsync(userId, ct);

        var friends = new List<FriendDto>(accepted.Count);

        foreach (var friendship in accepted)
        {
            // Друг — это вторая сторона заявки, а не конкретный её участник:
            // направление зависит от того, кто кого позвал.
            var friend = friendship.RequesterId == userId ? friendship.Addressee : friendship.Requester;

            if (friend is null)
                continue;

            friends.Add(await BuildFriendAsync(
                friend, friendship.RespondedAt ?? friendship.CreatedAt, ct));
        }

        return new FriendsOverviewDto(
            friends,
            incoming
                .Where(f => f.Requester is not null)
                .Select(f => new FriendRequestDto(f.Requester!.Username, f.Requester.DisplayName, f.CreatedAt))
                .ToList(),
            outgoing
                .Where(f => f.Addressee is not null)
                .Select(f => new FriendRequestDto(f.Addressee!.Username, f.Addressee.DisplayName, f.CreatedAt))
                .ToList());
    }

    /// <summary>Поиск людей по началу ника, с указанием уже сложившихся отношений.</summary>
    public async Task<IReadOnlyList<UserSearchResultDto>> SearchAsync(
        int userId, string? query, CancellationToken ct = default)
    {
        var normalized = UsernamePolicy.Normalize(query);

        // Пустой запрос вернул бы весь список пользователей: для поиска
        // конкретного человека это бесполезно и раскрывает лишнее.
        if (normalized.Length < 2)
            return [];

        var found = await users.SearchByUsernameAsync(normalized, SearchLimit, ct);
        var result = new List<UserSearchResultDto>(found.Count);

        foreach (var candidate in found)
        {
            if (candidate.Id == userId)
                continue;

            result.Add(new UserSearchResultDto(
                candidate.Username,
                candidate.DisplayName,
                await ResolveRelationAsync(userId, candidate.Id, ct)));
        }

        return result;
    }

    public async Task SendRequestAsync(int userId, string username, CancellationToken ct = default)
    {
        var target = await FindByUsernameAsync(username, ct);

        if (target.Id == userId)
            throw new ValidationException("Нельзя добавить в друзья самого себя.");

        var existing = await friendships.GetBetweenAsync(userId, target.Id, ct);

        if (existing is not null)
        {
            switch (existing.Status)
            {
                case FriendshipStatus.Accepted:
                    throw new ConflictException($"Вы уже друзья с {target.Username}.");

                case FriendshipStatus.Pending when existing.AddresseeId == userId:
                    // Встречная заявка: оба хотели одного и того же, поэтому
                    // достаточно подтвердить уже существующую.
                    Accept(existing);
                    await unitOfWork.SaveChangesAsync(ct);
                    return;

                case FriendshipStatus.Pending:
                    throw new ConflictException($"Заявка пользователю {target.Username} уже отправлена.");

                default:
                    // Отклонённую заявку можно отправить заново: старая запись
                    // удаляется, чтобы не мешать уникальному индексу.
                    await friendships.RemoveAsync(existing, ct);
                    break;
            }
        }

        await friendships.AddAsync(new Friendship
        {
            RequesterId = userId,
            AddresseeId = target.Id,
            Status = FriendshipStatus.Pending
        }, ct);

        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task AcceptAsync(int userId, string username, CancellationToken ct = default)
    {
        Accept(await RequireIncomingAsync(userId, username, ct));
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task DeclineAsync(int userId, string username, CancellationToken ct = default)
    {
        var friendship = await RequireIncomingAsync(userId, username, ct);

        friendship.Status = FriendshipStatus.Declined;
        friendship.RespondedAt = clock.UtcNow;

        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>Убирает человека из друзей. Работает и для своей исходящей заявки.</summary>
    public async Task RemoveAsync(int userId, string username, CancellationToken ct = default)
    {
        var target = await FindByUsernameAsync(username, ct);

        var friendship = await friendships.GetBetweenAsync(userId, target.Id, ct)
            ?? throw new ConflictException($"{target.Username} нет ни в друзьях, ни в заявках.");

        await friendships.RemoveAsync(friendship, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>Прогресс друга: сводка по словам, серия дней и ответы за сегодня.</summary>
    private async Task<FriendDto> BuildFriendAsync(User friend, DateTime friendsSince, CancellationToken ct)
    {
        var summary = await cardStates.GetSummaryAsync(friend.Id, clock.UtcNow, ct);
        var recent = await dailyStats.GetRecentAsync(friend.Id, StreakWindowDays, ct);

        // «Сегодня» и серия считаются в поясе самого друга, а не смотрящего:
        // иначе у человека в UTC+10 серия выглядела бы оборванной.
        var today = clock.TodayFor(friend.TimeZoneId);
        var streak = StreakCalculator.Calculate(StreakCalculator.ActiveDates(recent), today);
        var todayStat = recent.FirstOrDefault(stat => stat.Date == today);

        return new FriendDto(
            friend.Username,
            friend.DisplayName,
            summary.Learned,
            streak.Current,
            todayStat?.ReviewedCount ?? 0,
            friendsSince);
    }

    private async Task<FriendRelation> ResolveRelationAsync(int userId, int otherUserId, CancellationToken ct)
    {
        var friendship = await friendships.GetBetweenAsync(userId, otherUserId, ct);

        return friendship?.Status switch
        {
            FriendshipStatus.Accepted => FriendRelation.Friends,
            FriendshipStatus.Pending when friendship.RequesterId == userId => FriendRelation.OutgoingRequest,
            FriendshipStatus.Pending => FriendRelation.IncomingRequest,
            _ => FriendRelation.None
        };
    }

    private async Task<Friendship> RequireIncomingAsync(int userId, string username, CancellationToken ct)
    {
        var target = await FindByUsernameAsync(username, ct);

        var friendship = await friendships.GetBetweenAsync(userId, target.Id, ct);

        // Ответить можно только на заявку, адресованную тебе: иначе один человек
        // мог бы «принять» дружбу, о которой второй не просил.
        if (friendship is null || friendship.AddresseeId != userId || friendship.Status != FriendshipStatus.Pending)
            throw new ConflictException($"Нет входящей заявки от {target.Username}.");

        return friendship;
    }

    private async Task<User> FindByUsernameAsync(string username, CancellationToken ct)
    {
        var normalized = UsernamePolicy.Require(username);

        return await users.GetByUsernameAsync(normalized, ct)
            ?? throw NotFoundException.For("Пользователь с ником", normalized);
    }

    private void Accept(Friendship friendship)
    {
        friendship.Status = FriendshipStatus.Accepted;
        friendship.RespondedAt = clock.UtcNow;
    }
}
