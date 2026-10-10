using EnglishTrainer.Domain.Common;

namespace EnglishTrainer.Domain.Entities;

/// <summary>
/// Одноразовый токен для сброса пароля.
/// </summary>
/// <remarks>
/// <para>
/// Хранится <b>хеш</b> токена, а не сам токен. Ссылка уходит пользователю,
/// и если база когда-нибудь утечёт, по её содержимому нельзя будет сбросить
/// чужой пароль — восстановить токен из SHA-256 нельзя.
/// </para>
/// <para>
/// Токен одноразовый и с ограниченным сроком: ссылка из письма может попасть
/// в чужую переписку или остаться в истории браузера, поэтому действовать
/// она должна недолго.
/// </para>
/// </remarks>
public class PasswordResetToken : Entity
{
    public int UserId { get; set; }

    /// <summary>SHA-256 от токена в base64url. Сам токен в базе не хранится.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    /// <summary>Когда токен использовали. <c>null</c> — ещё не использован.</summary>
    public DateTime? UsedAt { get; set; }

    public User? User { get; set; }

    /// <summary>Годен ли токен: не использован и не истёк.</summary>
    public bool IsUsable(DateTime nowUtc) => UsedAt is null && ExpiresAt > nowUtc;
}
