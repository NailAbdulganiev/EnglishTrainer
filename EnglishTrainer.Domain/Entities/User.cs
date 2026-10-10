using EnglishTrainer.Domain.Common;

namespace EnglishTrainer.Domain.Entities;

/// <summary>Пользователь сервиса.</summary>
public class User : Entity
{
    /// <summary>Идентификатор для внешнего мира. Внутренний <see cref="Entity.Id"/> наружу не отдаётся.</summary>
    public Guid PublicId { get; set; } = Guid.NewGuid();

    /// <summary>Адрес для входа. Хранится в нижнем регистре, уникален.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Хеш пароля (PBKDF2). Открытый пароль не хранится нигде.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Уникальный ник, по которому человека находят другие пользователи.
    /// </summary>
    /// <remarks>
    /// Хранится отдельно от <see cref="DisplayName"/> намеренно: отображаемое имя
    /// не уникально и может быть любым («Йоханнес»), а ник — это адрес,
    /// по которому отправляют заявку в друзья. Смешивать их нельзя: две «Ани»
    /// в одной системе — обычное дело.
    /// </remarks>
    public string Username { get; set; } = string.Empty;

    /// <summary>Часовой пояс в формате IANA, например <c>Europe/Moscow</c>.</summary>
    /// <remarks>
    /// Нужен, чтобы «напомнить в 19:00» означало 19:00 пользователя, а не сервера.
    /// Ровно на этом месте ломаются проекты, где время хранят «как есть».
    /// </remarks>
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>Привязка к Telegram, если пользователь подключил бота.</summary>
    public long? TelegramId { get; set; }

    public DateTime? TelegramLinkedAt { get; set; }

    public UserSettings? Settings { get; set; }

    public ICollection<CardState> CardStates { get; set; } = new List<CardState>();

    public ICollection<StudySession> Sessions { get; set; } = new List<StudySession>();

    public ICollection<DailyStat> DailyStats { get; set; } = new List<DailyStat>();

    public ICollection<FavoriteWord> Favorites { get; set; } = new List<FavoriteWord>();
}
