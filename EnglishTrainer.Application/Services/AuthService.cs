using EnglishTrainer.Application.Dtos;
using EnglishTrainer.Application.Mapping;
using EnglishTrainer.Application.Text;
using EnglishTrainer.Domain.Abstractions;
using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Exceptions;
using EnglishTrainer.Domain.Repositories;

namespace EnglishTrainer.Application.Services;

/// <summary>Регистрация, вход и профиль пользователя.</summary>
public sealed class AuthService(
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    ITokenIssuer tokenIssuer)
{
    public const int MinimumPasswordLength = 8;

    public async Task<AuthResponseDto> RegisterAsync(RegisterRequestDto request, CancellationToken ct = default)
    {
        var email = NormalizeEmail(request.Email);
        ValidateEmail(email);

        if (request.Password is null || request.Password.Length < MinimumPasswordLength)
            throw new ValidationException($"Пароль должен быть не короче {MinimumPasswordLength} символов.");

        if (await users.EmailExistsAsync(email, ct))
            throw new ConflictException("Пользователь с таким email уже зарегистрирован.");

        var user = new User
        {
            Email = email,
            Username = await ReserveUsernameAsync(request.Username, email, ct),
            PasswordHash = passwordHasher.Hash(request.Password),
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? email.Split('@')[0]
                : request.DisplayName.Trim(),
            TimeZoneId = NormalizeTimeZone(request.TimeZoneId),
            Settings = new UserSettings()
        };

        await users.AddAsync(user, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return BuildAuthResponse(user);
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto request, CancellationToken ct = default)
    {
        var email = NormalizeEmail(request.Email);
        var user = await users.GetByEmailAsync(email, ct);

        // Одна и та же ошибка на «нет пользователя» и «неверный пароль»:
        // иначе можно перебором выяснять, какие адреса зарегистрированы.
        if (user is null || !passwordHasher.Verify(request.Password ?? string.Empty, user.PasswordHash))
            throw new UnauthorizedException("Неверный email или пароль.");

        return BuildAuthResponse(user);
    }

    public async Task<UserSettingsDto> UpdateSettingsAsync(
        int userId, UpdateSettingsRequestDto request, CancellationToken ct = default)
    {
        var user = await users.GetWithSettingsAsync(userId, ct)
            ?? throw NotFoundException.For("Пользователь", userId);

        var settings = user.Settings ??= new UserSettings();

        if (request.NewWordsPerDay is { } newPerDay)
        {
            // Границы не косметические: 200 новых слов в день гарантированно
            // приводят к «стене» повторений и уходу пользователя на третьи сутки.
            if (newPerDay is < 0 or > 100)
                throw new ValidationException("Количество новых слов в день должно быть от 0 до 100.");

            settings.NewWordsPerDay = newPerDay;
        }

        if (request.MaxCardsPerSession is { } maxCards)
        {
            if (maxCards is < 5 or > 500)
                throw new ValidationException("Размер сессии должен быть от 5 до 500 карточек.");

            settings.MaxCardsPerSession = maxCards;
        }

        if (request.DailyGoal is { } goal)
        {
            if (goal is < 1 or > 1000)
                throw new ValidationException("Дневная цель должна быть от 1 до 1000 ответов.");

            settings.DailyGoal = goal;
        }

        if (request.DailyReminderEnabled is { } reminders)
            settings.DailyReminderEnabled = reminders;

        if (request.ReminderTime is { } reminderTime)
            settings.ReminderTime = reminderTime;

        if (request.ShowTranscriptionUpfront is { } transcription)
            settings.ShowTranscriptionUpfront = transcription;

        await unitOfWork.SaveChangesAsync(ct);
        return settings.ToDto();
    }

    public async Task UpdateTimeZoneAsync(int userId, string timeZoneId, CancellationToken ct = default)
    {
        var user = await users.GetByIdAsync(userId, ct)
            ?? throw NotFoundException.For("Пользователь", userId);

        user.TimeZoneId = NormalizeTimeZone(timeZoneId);
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Определяет ник нового пользователя.
    /// </summary>
    /// <remarks>
    /// Если ник выбран человеком — он либо свободен, либо регистрация отклоняется.
    /// Молча подставить «похожий» нельзя: пользователь вводил конкретный ник
    /// и будет искать себя по нему. А вот ник, выведенный из email, ничей не выбор,
    /// поэтому к нему допустимо добавить числовой хвост.
    /// </remarks>
    private async Task<string> ReserveUsernameAsync(string? requested, string email, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var chosen = UsernamePolicy.Require(requested);

            if (await users.UsernameExistsAsync(chosen, ct))
                throw new ConflictException($"Ник «{chosen}» уже занят.");

            return chosen;
        }

        var suggested = UsernamePolicy.SuggestFromEmail(email);

        if (!await users.UsernameExistsAsync(suggested, ct))
            return suggested;

        for (var suffix = 2; suffix <= 99; suffix++)
        {
            var candidate = UsernamePolicy.WithSuffix(suggested, suffix);

            if (!await users.UsernameExistsAsync(candidate, ct))
                return candidate;
        }

        return UsernamePolicy.WithSuffix(suggested, Random.Shared.Next(100, 9999));
    }

    private AuthResponseDto BuildAuthResponse(User user)
    {
        var token = tokenIssuer.Issue(user.Id, user.PublicId, user.Email);
        return new AuthResponseDto(token.Value, token.ExpiresAtUtc, user.ToDto());
    }

    /// <summary>Email приводится к нижнему регистру: иначе один адрес можно завести дважды.</summary>
    public static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static void ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || email.StartsWith('@') || email.EndsWith('@'))
            throw new ValidationException("Некорректный email.");
    }

    private static string NormalizeTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return "UTC";

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId).Id;
        }
        catch (TimeZoneNotFoundException)
        {
            throw new ValidationException($"Неизвестный часовой пояс: '{timeZoneId}'.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new ValidationException($"Некорректный часовой пояс: '{timeZoneId}'.");
        }
    }
}
