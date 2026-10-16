using EnglishTrainer.Application.Dtos;
using EnglishTrainer.Application.Mapping;
using EnglishTrainer.Application.Media;
using EnglishTrainer.Application.Text;
using EnglishTrainer.Domain.Abstractions;
using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Enums;
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
    IAvatarStorage avatars,
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
    /// Профиль другого игрока: прогресс, который виден друзьям.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Открыть чужой профиль можно только другу, и для постороннего отвечаем
    /// «не найдено», а не «запрещено». Разница существенна: 403 подтверждает,
    /// что такой ник существует, и перебором адресов можно было бы составить
    /// список пользователей сервиса.
    /// </para>
    /// <para>
    /// «Сегодня», окно недели и серия считаются в поясе того, чей профиль
    /// открывают: у друга в UTC+10 день уже начался, и его «сегодня» — правда
    /// про него, а не про смотрящего.
    /// </para>
    /// <para>
    /// Недельные показатели берутся из того же правила, что и рейтинг
    /// (<see cref="LeaderboardRanking"/>), — иначе один и тот же экран и рейтинг
    /// показывали бы разные числа об одном человеке.
    /// </para>
    /// </remarks>
    public async Task<PublicProfileDto> GetPublicAsync(
        int viewerId, string username, CancellationToken ct = default)
    {
        var user = await users.GetByUsernameAsync(UsernamePolicy.Require(username), ct)
            ?? throw NotFoundException.For("Пользователь с ником", username);

        var isMe = user.Id == viewerId;
        Friendship? friendship = null;

        if (!isMe)
        {
            friendship = await friendships.GetBetweenAsync(viewerId, user.Id, ct);

            if (friendship is null || friendship.Status != FriendshipStatus.Accepted)
                throw NotFoundException.For("Пользователь с ником", username);
        }

        var nowUtc = clock.UtcNow;
        var summary = await cardStates.GetSummaryAsync(user.Id, nowUtc, ct);
        var today = clock.TodayFor(user.TimeZoneId);
        var recent = await dailyStats.GetRecentAsync(user.Id, StreakWindowDays, ct);

        // Серия и недельные показатели считаются из одной и той же дневной
        // статистики: второй запрос ради недели был бы лишним обращением к базе.
        var streak = StreakCalculator.Calculate(StreakCalculator.ActiveDates(recent), today);
        var score = LeaderboardRanking.ScoreFor(
            recent, today.AddDays(-(LeaderboardRanking.WindowDays - 1)), today, today);

        return new PublicProfileDto(
            user.Username,
            user.DisplayName,
            user.AvatarUrl,
            user.CreatedAt,
            summary.Learned,
            Math.Max(0, summary.Total - summary.Learned),
            streak.Current,
            streak.Longest,
            recent.FirstOrDefault(stat => stat.Date == today)?.ReviewedCount ?? 0,
            score.WeeklyWords,
            score.WeeklyReviews,
            await reviewLogs.CountAsync(user.Id, ct),
            await friendships.CountAcceptedAsync(user.Id, ct),
            friendship is null ? null : friendship.RespondedAt ?? friendship.CreatedAt);
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

    /// <summary>
    /// Загружает новый аватар, заменяя прежний.
    /// </summary>
    /// <remarks>
    /// Порядок действий выбран осознанно: сначала на диск пишется новый файл,
    /// потом обновляется строка в базе, и только затем удаляется старый.
    /// Любой обрыв на этом пути оставляет пользователя с рабочим аватаром —
    /// худшее, что может случиться, это забытый файл на диске. Обратный порядок
    /// дал бы профиль со ссылкой на уже удалённый файл, то есть пустой кружок
    /// без объяснения причины.
    /// </remarks>
    public async Task<UserDto> SetAvatarAsync(
        int userId, AvatarUpload upload, CancellationToken ct = default)
    {
        var user = await users.GetByIdAsync(userId, ct)
            ?? throw NotFoundException.For("Пользователь", userId);

        // Проверка содержимого идёт до записи на диск: недопустимый файл
        // не должен оказаться в каталоге, который раздаётся статикой.
        var image = await AvatarPolicy.ReadAsync(upload, ct);
        var previous = user.AvatarFileName;

        user.AvatarFileName = await avatars.SaveAsync(image.Content, image.Extension, ct);

        await unitOfWork.SaveChangesAsync(ct);

        if (!string.IsNullOrEmpty(previous) && previous != user.AvatarFileName)
        {
            await avatars.DeleteAsync(previous, ct);
        }

        return user.ToDto();
    }

    /// <summary>
    /// Удаляет аватар. После этого в интерфейсе снова показываются инициалы.
    /// </summary>
    /// <remarks>
    /// Повторный вызов не ошибка: удаление — идемпотентное действие, и «удалить
    /// то, чего нет» должно приводить к тому же результату, а не к отказу.
    /// </remarks>
    public async Task<UserDto> RemoveAvatarAsync(int userId, CancellationToken ct = default)
    {
        var user = await users.GetByIdAsync(userId, ct)
            ?? throw NotFoundException.For("Пользователь", userId);

        var previous = user.AvatarFileName;

        if (string.IsNullOrEmpty(previous))
        {
            return user.ToDto();
        }

        user.AvatarFileName = null;

        await unitOfWork.SaveChangesAsync(ct);

        // Файл удаляется после базы. Оставшийся файл — мусор, который никому
        // не виден; ссылка на удалённый файл была бы видимой поломкой.
        await avatars.DeleteAsync(previous, ct);

        return user.ToDto();
    }
}
