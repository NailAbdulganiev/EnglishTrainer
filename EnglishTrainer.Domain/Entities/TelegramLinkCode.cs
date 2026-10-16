using EnglishTrainer.Domain.Common;

namespace EnglishTrainer.Domain.Entities;

/// <summary>
/// Одноразовый код привязки Telegram к аккаунту.
/// </summary>
/// <remarks>
/// <para>
/// Привязка идёт в две стороны, и обе ненадёжны по-своему: браузер не знает,
/// какой Telegram-аккаунт у человека, а бот не знает пароля от тренажёра.
/// Код — это короткий мостик между ними: браузер его выдаёт, бот принимает,
/// и только человек, владеющий обоими, может его перенести.
/// </para>
/// <para>
/// Как и у сброса пароля, в базе лежит <b>хеш</b>, а не сам код: код уходит
/// в ссылку вида <c>t.me/…?start=&lt;код&gt;</c>, и утечка таблицы не должна
/// позволять привязать чужой Telegram к своему аккаунту.
/// </para>
/// <para>
/// Код одноразовый и короткоживущий. Срок в минутах, а не в часах: код
/// переносится вручную в мессенджер, и держать его действующим сутки незачем.
/// </para>
/// </remarks>
public class TelegramLinkCode : Entity
{
    public int UserId { get; set; }

    /// <summary>SHA-256 от кода в hex. Сам код в базе не хранится.</summary>
    public string CodeHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    /// <summary>Когда код использовали. <c>null</c> — ещё не использован.</summary>
    public DateTime? UsedAt { get; set; }

    public User? User { get; set; }

    /// <summary>Годен ли код: не использован и не истёк.</summary>
    public bool IsUsable(DateTime nowUtc) => UsedAt is null && ExpiresAt > nowUtc;
}
