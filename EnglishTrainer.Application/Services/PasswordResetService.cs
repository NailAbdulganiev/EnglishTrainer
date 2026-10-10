using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using EnglishTrainer.Application.Text;
using EnglishTrainer.Domain.Abstractions;
using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Exceptions;
using EnglishTrainer.Domain.Repositories;

namespace EnglishTrainer.Application.Services;

/// <summary>
/// Восстановление доступа: ссылка для сброса пароля и установка нового.
/// </summary>
/// <remarks>
/// <para>
/// Токен сброса — это 32 случайных байта, закодированных base64url. В базе
/// хранится только его SHA-256: если таблица когда-нибудь утечёт, восстановить
/// по хешу токен нельзя, а значит и сбросить чужой пароль тоже.
/// </para>
/// <para>
/// Ссылка живёт ограниченное время и срабатывает один раз. После успешного
/// сброса все прочие действующие токены пользователя гасятся: если ссылок
/// было запрошено несколько, старая не должна остаться рабочим ключом.
/// </para>
/// </remarks>
public sealed class PasswordResetService(
    IUserRepository users,
    IPasswordResetTokenRepository tokens,
    IPasswordHasher passwordHasher,
    IEmailSender emailSender,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    /// <summary>Срок жизни ссылки. Двое суток — компромисс между удобством и риском.</summary>
    public const int TokenLifetimeHours = 2;

    private const int TokenBytes = 32;

    /// <summary>
    /// Создаёт ссылку для сброса и отправляет её письмом.
    /// </summary>
    /// <remarks>
    /// Метод ничего не сообщает о том, найден пользователь или нет: вызывающий
    /// эндпоинт всегда отвечает одинаково. Иначе форма «забыли пароль» станет
    /// способом узнать, какие адреса зарегистрированы в сервисе.
    /// </remarks>
    public async Task RequestAsync(string? login, string resetLinkBase, CancellationToken ct = default)
    {
        var user = await FindUserAsync(login, ct);

        if (user is null)
            return;

        var token = GenerateToken();

        await tokens.AddAsync(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = Hash(token),
            ExpiresAt = clock.UtcNow.AddHours(TokenLifetimeHours)
        }, ct);

        await unitOfWork.SaveChangesAsync(ct);

        // Токен кодируется для адреса: в base64url встречаются «-» и «_»,
        // которые в строке запроса допустимы, но экранировать надёжнее.
        var link = $"{resetLinkBase}?token={Uri.EscapeDataString(token)}";

        await emailSender.SendAsync(
            new EmailMessage(user.Email, "Сброс пароля EnglishTrainer", BuildBody(user, link)), ct);
    }

    /// <summary>Устанавливает новый пароль по токену из письма.</summary>
    public async Task ResetAsync(string? token, string? newPassword, CancellationToken ct = default)
    {
        var password = PasswordPolicy.Require(newPassword);

        if (string.IsNullOrWhiteSpace(token))
            throw new ValidationException("В ссылке нет токена. Запросите новую ссылку.");

        var stored = await tokens.GetByHashAsync(Hash(token), ct);

        // Одна ошибка на «токена нет», «истёк» и «уже использован»: подсказывать,
        // какая именно ссылка была правильной, незачем.
        if (stored is null || !stored.IsUsable(clock.UtcNow))
            throw new ValidationException("Ссылка недействительна или устарела. Запросите новую.");

        var user = await users.GetByIdAsync(stored.UserId, ct)
            ?? throw NotFoundException.For("Пользователь", stored.UserId);

        user.PasswordHash = passwordHasher.Hash(password);

        await InvalidateActiveTokensAsync(user.Id, ct);

        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Прямая установка пароля по логину.
    /// </summary>
    /// <remarks>
    /// Нужна для сброса из командной строки: если почтового провайдера нет,
    /// а доступ потерян, у владельца сервера должен остаться способ вернуть себе
    /// аккаунт. Возвращает <c>false</c>, если пользователь не найден.
    /// </remarks>
    public async Task<bool> SetPasswordAsync(string? login, string newPassword, CancellationToken ct = default)
    {
        var password = PasswordPolicy.Require(newPassword);

        var user = await FindUserAsync(login, ct);

        if (user is null)
            return false;

        user.PasswordHash = passwordHasher.Hash(password);

        // Старые ссылки сброса становятся бесполезны: пароль уже установлен вручную.
        await InvalidateActiveTokensAsync(user.Id, ct);

        await unitOfWork.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Гасит все действующие токены пользователя.</summary>
    private async Task InvalidateActiveTokensAsync(int userId, CancellationToken ct)
    {
        var active = await tokens.GetActiveAsync(userId, clock.UtcNow, ct);

        foreach (var token in active)
            token.UsedAt = clock.UtcNow;

        // Сохранение выполняет вызывающий метод: так сброс пароля и гашение
        // ссылок попадают в одну транзакцию и не могут разойтись.
    }

    private async Task<User?> FindUserAsync(string? login, CancellationToken ct)
    {
        var identifier = AuthService.NormalizeIdentifier(login);

        if (identifier.Length == 0)
            return null;

        return await users.GetByEmailAsync(identifier, ct)
               ?? await users.GetByUsernameAsync(identifier, ct);
    }

    private static string BuildBody(User user, string link) =>
        $"""
         Здравствуйте, {user.DisplayName}!

         Кто-то запросил сброс пароля для аккаунта @{user.Username}.

         Чтобы задать новый пароль, откройте ссылку:
         {link}

         Ссылка действует {TokenLifetimeHours} ч и срабатывает один раз.
         Если вы не запрашивали сброс, просто проигнорируйте это письмо:
         пароль останется прежним.
         """;

    private static string GenerateToken() =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
