using EnglishTrainer.Domain.Common;
using EnglishTrainer.Domain.Scheduling;

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

    /// <summary>
    /// Имя файла аватара в хранилище. <c>null</c> — аватара нет, показываются инициалы.
    /// </summary>
    /// <remarks>
    /// В базе лежит имя файла, а не сам файл: изображения не место в таблице
    /// пользователей — они раздувают строку, попадают во все выборки и мешают
    /// резервному копированию. Файл лежит на диске, имя уникально (GUID), поэтому
    /// два пользователя не могут перезаписать аватар друг друга.
    /// </remarks>
    public string? AvatarFileName { get; set; }

    /// <summary>
    /// Публичный адрес аватара — вычисляется из <see cref="AvatarFileName"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// В базе не хранится: адрес отдачи (префикс <c>/avatars</c>) — это решение
    /// HTTP-слоя, и записывать его в данные значило бы «зашить» способ доставки
    /// в предметную область. Поменяется способ — не придётся править таблицу.
    /// </para>
    /// <para>
    /// Имя файла содержит случайную часть, поэтому адрес меняется вместе с аватаром.
    /// Это же и сбрасывает кеш браузера: старый адрес перестаёт существовать,
    /// и «загрузил новое фото, а вижу старое» не случается.
    /// </para>
    /// </remarks>
    public string? AvatarUrl =>
        string.IsNullOrEmpty(AvatarFileName) ? null : $"{AvatarUrlPrefix}/{AvatarFileName}";

    /// <summary>Каталог, из которого отдаются аватары.</summary>
    public const string AvatarUrlPrefix = "/avatars";

    /// <summary>
    /// Пересчитывает момент следующего напоминания в Telegram.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Метод живёт на пользователе, а не на настройках, потому что расписание
    /// зависит от двух вещей сразу: от настроек (время, включённость) и от самого
    /// пользователя (часовой пояс, привязан ли Telegram). Настройки об этой связи
    /// не знают и знать не должны.
    /// </para>
    /// <para>
    /// Вызывается после каждой правки, влияющей на расписание: смена времени
    /// напоминания, смена часового пояса, привязка и отвязка Telegram. Иначе
    /// человек, перенёсший напоминание с 19:00 на 8:00, получил бы его по старому
    /// расписанию — и решил бы, что настройка не работает.
    /// </para>
    /// </remarks>
    public void ScheduleReminder(DateTime utcNow)
    {
        if (Settings is null)
        {
            return;
        }

        // Нет привязки — нет напоминания: отправлять его некуда. Выключенные
        // напоминания снимают расписание совсем, поэтому планировщик не тратит
        // на таких пользователей ни одной строки выборки.
        Settings.NextReminderAtUtc = TelegramId.HasValue && Settings.DailyReminderEnabled
            ? ReminderSchedule.NextOccurrence(Settings.ReminderTime, TimeZoneId, utcNow)
            : null;
    }

    public UserSettings? Settings { get; set; }

    public ICollection<CardState> CardStates { get; set; } = new List<CardState>();

    public ICollection<StudySession> Sessions { get; set; } = new List<StudySession>();

    public ICollection<DailyStat> DailyStats { get; set; } = new List<DailyStat>();

    public ICollection<FavoriteWord> Favorites { get; set; } = new List<FavoriteWord>();
}
