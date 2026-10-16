namespace EnglishTrainer.Application.Telegram;

/// <summary>
/// Настройки Telegram: общие для API и для процесса бота.
/// </summary>
/// <remarks>
/// <para>
/// Один класс на оба процесса, потому что и токен, и имя бота нужны обоим:
/// API проверяет подпись данных мини-аппа, бот открывает окно чата. Две копии
/// настроек разошлись бы, и «мини-апп открывается, а вход в нём не работает»
/// выяснялось бы полдня.
/// </para>
/// <para>
/// Токен в репозитории не хранится: он приходит из <c>appsettings.Local.json</c>
/// (файл вне репозитория) или из переменной окружения <c>Telegram__BotToken</c>.
/// </para>
/// </remarks>
public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    /// <summary>Токен от @BotFather.</summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>Имя бота без «@» — из него собирается ссылка на привязку аккаунта.</summary>
    public string BotUsername { get; set; } = string.Empty;

    /// <summary>
    /// Адрес мини-аппа, который открывает Telegram. Обязателен HTTPS:
    /// по HTTP кнопка WebApp не работает, и это ограничение Telegram, а не кода.
    /// </summary>
    public string WebAppUrl { get; set; } = string.Empty;

    /// <summary>
    /// Выключенный бот не опрашивает Telegram, но API продолжает проверять
    /// подпись мини-аппа. Нужно, чтобы один и тот же набор настроек годился
    /// и для запуска только веб-версии.
    /// </summary>
    public bool BotEnabled { get; set; } = true;

    /// <summary>Сколько секунд держать одно соединение long polling.</summary>
    public int PollTimeoutSeconds { get; set; } = 30;

    /// <summary>Как часто проверять, кому пора напомнить, в секундах.</summary>
    public int ReminderTickSeconds { get; set; } = 60;

    /// <summary>Настроен ли бот: без токена ни мини-апп, ни бот работать не могут.</summary>
    public bool HasToken => !string.IsNullOrWhiteSpace(BotToken);

    /// <summary>Проверять подпись данных мини-аппа вообще возможно?</summary>
    public bool CanValidateInitData => HasToken;

    /// <summary>
    /// Ссылка, открывающая бота с параметром запуска.
    /// </summary>
    /// <remarks>
    /// <c>start</c> — а не <c>startapp</c>: параметр <c>startapp</c> открывает
    /// мини-апп сразу и доставляет код в интерфейс, но не приходит боту
    /// сообщением, а привязка происходит именно в чате с ботом.
    /// </remarks>
    public string? DeepLink(string payload) =>
        string.IsNullOrWhiteSpace(BotUsername)
            ? null
            : $"https://t.me/{BotUsername.TrimStart('@')}?start={Uri.EscapeDataString(payload)}";

    /// <summary>Адрес мини-аппа или <c>null</c>, если он не задан или задан без HTTPS.</summary>
    public string? SecureWebAppUrl =>
        Uri.TryCreate(WebAppUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri.ToString()
            : null;
}
