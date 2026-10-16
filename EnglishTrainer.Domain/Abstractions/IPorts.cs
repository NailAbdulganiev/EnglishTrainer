namespace EnglishTrainer.Domain.Abstractions;

/// <summary>
/// Единый источник времени. Внедряется вместо прямых обращений к
/// <see cref="DateTime.UtcNow"/>, иначе время нельзя ни подменить в тестах,
/// ни проверить граничные случаи вроде перехода через полночь.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }

    /// <summary>
    /// Локальная дата пользователя. Критично для дневной статистики и серии дней:
    /// «сегодня» для пользователя в UTC+3 и на сервере в UTC — разные даты.
    /// </summary>
    DateOnly TodayFor(string timeZoneId);

    /// <summary>Локальное время пользователя — для проверки «пора напомнить».</summary>
    TimeOnly LocalTimeFor(string timeZoneId);
}

/// <summary>Хеширование паролей. Реализация — в Infrastructure.</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>Проверка пароля. Сравнение обязано быть устойчивым по времени.</summary>
    bool Verify(string password, string passwordHash);
}

/// <summary>Генератор токенов доступа (JWT или иная схема).</summary>
public interface ITokenIssuer
{
    AccessToken Issue(int userId, Guid publicId, string email);
}

/// <param name="Value">Сам токен.</param>
/// <param name="ExpiresAtUtc">Момент истечения.</param>
public readonly record struct AccessToken(string Value, DateTime ExpiresAtUtc);

/// <summary>Письмо к отправке.</summary>
/// <param name="To">Адрес получателя.</param>
/// <param name="Subject">Тема.</param>
/// <param name="Body">Текст письма.</param>
public readonly record struct EmailMessage(string To, string Subject, string Body);

/// <summary>
/// Отправка писем.
/// </summary>
/// <remarks>
/// Порт, а не прямая работа с SMTP: приложение должно работать и без почтового
/// провайдера — например, когда оно развёрнуто для одного человека на его машине.
/// Куда именно уходит письмо, решает реализация.
/// </remarks>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

/// <summary>Загруженное изображение: то, что пришло от клиента, до проверки.</summary>/// <param name="Content">Поток с содержимым файла.</param>
/// <param name="ContentType">Тип, объявленный клиентом. Проверяется, а не принимается на веру.</param>
/// <param name="Length">Размер, объявленный клиентом.</param>
public readonly record struct AvatarUpload(Stream Content, string? ContentType, long Length);

/// <summary>
/// Хранилище аватаров.
/// </summary>
/// <remarks>
/// Порт, а не прямая запись в каталог: сегодня это файлы на диске, завтра —
/// S3 или диск, общий для нескольких экземпляров приложения. Application
/// о способе хранения не знает и знать не должен.
/// </remarks>
public interface IAvatarStorage
{
    /// <summary>
    /// Сохраняет изображение и возвращает имя файла, под которым оно лежит.
    /// Имя генерирует хранилище: клиент не должен влиять на путь.
    /// </summary>
    Task<string> SaveAsync(byte[] content, string extension, CancellationToken ct = default);

    /// <summary>Удаляет файл. Отсутствие файла ошибкой не считается.</summary>
    Task DeleteAsync(string fileName, CancellationToken ct = default);
}

/// <summary>
/// Разобранные и проверенные данные мини-аппа Telegram.
/// </summary>
/// <param name="UserId">Идентификатор пользователя в Telegram.</param>
/// <param name="Username">Ник в Telegram, если он у человека есть.</param>
/// <param name="FirstName">Имя из профиля Telegram — для приветствия.</param>
/// <param name="LanguageCode">Язык клиента, например <c>ru</c>.</param>
/// <param name="AuthDateUtc">Когда Telegram подписал эти данные.</param>
public sealed record TelegramInitData(
    long UserId,
    string? Username,
    string? FirstName,
    string? LanguageCode,
    DateTime AuthDateUtc);

/// <summary>
/// Проверка подписи данных, которые мини-апп получает от Telegram.
/// </summary>
/// <remarks>
/// <para>
/// Это единственная защита входа в мини-апп: пароля там нет, а есть строка
/// <c>initData</c>, подписанная токеном бота. Если подпись не проверять,
/// любой желающий отправит чужой идентификатор Telegram и войдёт в аккаунт
/// без пароля — то есть проверка здесь не «дополнительная», а основная.
/// </para>
/// <para>
/// Реализация живёт в Infrastructure: она знает токен бота, а Domain — нет
/// и знать не должен.
/// </para>
/// </remarks>
public interface ITelegramInitDataValidator
{
    /// <summary>
    /// Проверяет подпись и разбирает данные.
    /// </summary>
    /// <returns><c>null</c>, если подпись неверна, данные испорчены или срок их жизни истёк.</returns>
    TelegramInitData? Validate(string? initData);
}


