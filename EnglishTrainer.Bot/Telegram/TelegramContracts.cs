using System.Text.Json.Serialization;

namespace EnglishTrainer.Bot.Telegram;

// ─────────────────────────────────────────────────────────────────────────────
// Модели Bot API. Описаны только те поля, которые бот действительно использует.
//
// Полная копия схемы Telegram здесь вредна: она велика, меняется каждый месяц
// и создаёт ложное чувство, что бот поддерживает всё описанное. Лишние поля
// в ответе просто игнорируются — System.Text.Json по умолчанию терпим к ним.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Ответ Bot API: либо результат, либо описание ошибки.</summary>
internal sealed record TelegramApiResponse<T>(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("result")] T? Result,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("error_code")] int? ErrorCode);

/// <summary>Обновление из очереди Telegram. Обрабатываются только сообщения.</summary>
internal sealed record TelegramUpdate(
    [property: JsonPropertyName("update_id")] long UpdateId,
    [property: JsonPropertyName("message")] TelegramMessage? Message);

internal sealed record TelegramMessage(
    [property: JsonPropertyName("message_id")] long MessageId,
    [property: JsonPropertyName("from")] TelegramUser? From,
    [property: JsonPropertyName("chat")] TelegramChat? Chat,
    [property: JsonPropertyName("text")] string? Text);

internal sealed record TelegramChat(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("type")] string? Type);

internal sealed record TelegramUser(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("first_name")] string? FirstName);

/// <summary>Сведения о самом боте — то, что возвращает <c>getMe</c>.</summary>
internal sealed record TelegramBotInfo(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("first_name")] string? FirstName);

/// <summary>Кнопка под сообщением.</summary>
/// <param name="Url">Обычная ссылка. Взаимоисключающая с <paramref name="WebApp"/>.</param>
/// <param name="WebApp">Кнопка запуска мини-аппа. Работает только по HTTPS.</param>
internal sealed record TelegramInlineButton(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("url")] string? Url = null,
    [property: JsonPropertyName("web_app")] TelegramWebAppInfo? WebApp = null);

internal sealed record TelegramWebAppInfo(
    [property: JsonPropertyName("url")] string Url);

/// <summary>Команда в меню бота.</summary>
internal sealed record TelegramBotCommand(
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("description")] string Description);

/// <summary>Ответ Telegram, который не удалось выполнить.</summary>
internal sealed class TelegramApiException(string method, int statusCode, string description)
    : Exception($"Telegram отклонил {method} ({statusCode}): {description}")
{
    public int StatusCode { get; } = statusCode;
}
