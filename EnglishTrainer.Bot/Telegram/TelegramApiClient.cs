using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnglishTrainer.Bot.Telegram;

/// <summary>Методы Bot API, которые нужны боту.</summary>
internal interface ITelegramApi
{
    /// <summary>Проверка токена и получение имени бота.</summary>
    Task<TelegramBotInfo> GetMeAsync(CancellationToken ct);

    /// <summary>
    /// Забирает обновления.
    /// </summary>
    /// <param name="offset">
    /// Идентификатор первого нужного обновления: всё, что меньше, Telegram
    /// считает подтверждённым и больше не присылает.
    /// </param>
    /// <param name="timeoutSeconds">Сколько секунд держать соединение, если новостей нет.</param>
    Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(
        long? offset, int timeoutSeconds, CancellationToken ct);

    /// <summary>Отправляет сообщение, при необходимости с кнопкой.</summary>
    Task SendMessageAsync(
        long chatId, string text, TelegramInlineButton? button, CancellationToken ct);

    /// <summary>Публикует список команд в меню бота.</summary>
    Task SetMyCommandsAsync(IReadOnlyList<TelegramBotCommand> commands, CancellationToken ct);

    /// <summary>Ставит кнопку меню, открывающую мини-апп.</summary>
    Task SetMenuButtonAsync(string text, string webAppUrl, CancellationToken ct);
}

/// <summary>
/// Клиент Bot API.
/// </summary>
/// <remarks>
/// <para>
/// Тонкая обёртка над HTTP: ни очередей, ни повторов, ни состояния. Повторы
/// и паузы — забота вызывающего кода, потому что правильное поведение при сбое
/// зависит от операции: потерянное напоминание пережить можно, потерянную
/// команду /start — нет (человек ждёт ответа).
/// </para>
/// <para>
/// Все методы вызываются POST с телом JSON. Telegram принимает JSON наравне
/// с form-urlencoded, а JSON не требует ручного кодирования кириллицы и
/// переносов строк — на них легко получить испорченное сообщение.
/// </para>
/// </remarks>
internal sealed class TelegramApiClient(HttpClient http, ILogger<TelegramApiClient> logger) : ITelegramApi
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<TelegramBotInfo> GetMeAsync(CancellationToken ct) =>
        await CallAsync<TelegramBotInfo>("getMe", new { }, ct)
        ?? throw new InvalidOperationException("Telegram не вернул сведения о боте.");

    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(
        long? offset, int timeoutSeconds, CancellationToken ct)
    {
        // Запас к времени ожидания: соединение обрывается не мгновенно, и клиент
        // не должен сдаваться раньше, чем Telegram сам закроет long polling.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds + 20));

        var updates = await CallAsync<List<TelegramUpdate>>(
            "getUpdates",
            new { offset, timeout = timeoutSeconds, allowed_updates = new[] { "message" } },
            timeout.Token);

        return updates ?? [];
    }

    public async Task SendMessageAsync(
        long chatId, string text, TelegramInlineButton? button, CancellationToken ct)
    {
        // Кнопка передаётся только когда она есть: поле reply_markup со значением
        // null Telegram отвергает, а не игнорирует.
        object payload = button is null
            ? new { chat_id = chatId, text, link_preview_options = new { is_disabled = true } }
            : new
            {
                chat_id = chatId,
                text,
                link_preview_options = new { is_disabled = true },
                reply_markup = new { inline_keyboard = new[] { new[] { button } } }
            };

        await CallAsync<TelegramMessage>("sendMessage", payload, ct);
    }

    public async Task SetMyCommandsAsync(IReadOnlyList<TelegramBotCommand> commands, CancellationToken ct) =>
        await CallAsync<bool>("setMyCommands", new { commands }, ct);

    public async Task SetMenuButtonAsync(string text, string webAppUrl, CancellationToken ct) =>
        await CallAsync<bool>("setChatMenuButton", new
        {
            menu_button = new { type = "web_app", text, web_app = new { url = webAppUrl } }
        }, ct);

    /// <summary>
    /// Вызывает метод и разбирает конверт ответа.
    /// </summary>
    /// <remarks>
    /// Telegram отвечает кодом 200 даже на ошибку: признак неудачи лежит в поле
    /// <c>ok</c>, а не в статусе HTTP. Проверять только статус — значит считать
    /// успешной отправку, которую Telegram отклонил.
    /// </remarks>
    private async Task<T?> CallAsync<T>(string method, object payload, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(method, payload, Json, ct);

        TelegramApiResponse<T>? envelope;

        try
        {
            envelope = await response.Content.ReadFromJsonAsync<TelegramApiResponse<T>>(Json, ct);
        }
        catch (JsonException ex)
        {
            throw new TelegramApiException(method, (int)response.StatusCode, $"неразборчивый ответ ({ex.Message})");
        }

        if (envelope is null)
        {
            throw new TelegramApiException(method, (int)response.StatusCode, "пустой ответ");
        }

        if (!envelope.Ok)
        {
            // Описание ошибки идёт в исключение: без него «бот молчит» невозможно
            // отладить — непонятно, дело в токене, в правах или в тексте.
            throw new TelegramApiException(
                method, envelope.ErrorCode ?? (int)response.StatusCode, envelope.Description ?? "без описания");
        }

        logger.LogDebug("Telegram: {Method} выполнен.", method);

        return envelope.Result;
    }
}
