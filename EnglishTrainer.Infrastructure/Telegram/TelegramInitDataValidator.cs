using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EnglishTrainer.Application.Telegram;
using EnglishTrainer.Domain.Abstractions;
using Microsoft.Extensions.Options;

namespace EnglishTrainer.Infrastructure.Telegram;

/// <summary>
/// Проверка подписи данных, которые Telegram передаёт мини-аппу.
/// </summary>
/// <remarks>
/// <para>
/// Алгоритм из документации Telegram: строкой <c>data_check_string</c> служат
/// пары «ключ=значение», отсортированные по ключу и соединённые переводами
/// строки; ключом подписи служит HMAC-SHA256 от токена бота с ключом
/// <c>WebAppData</c>. Совпадение с полем <c>hash</c> и означает, что данные
/// действительно выдал Telegram этому боту.
/// </para>
/// <para>
/// Сравнение — <see cref="CryptographicOperations.FixedTimeEquals"/>: обычное
/// сравнение строк выдаёт по времени ответа, сколько первых символов подписи
/// угадано, и подпись можно подобрать посимвольно.
/// </para>
/// </remarks>
public sealed class TelegramInitDataValidator(
    IOptions<TelegramOptions> options,
    IClock clock) : ITelegramInitDataValidator
{
    /// <summary>
    /// Сколько живут данные мини-аппа.
    /// </summary>
    /// <remarks>
    /// Строка <c>initData</c> не одноразовая: она действует, пока Telegram
    /// считает сеанс открытым. Сутки — компромисс: за это время перехваченная
    /// строка успевает протухнуть, а человек, открывший приложение вчера
    /// вечером и вернувшийся утром, не видит форму входа.
    /// </remarks>
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    /// <summary>Допустимое расхождение часов с Telegram — на случай неточных часов клиента.</summary>
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    public TelegramInitData? Validate(string? initData)
    {
        var token = options.Value.BotToken;

        // Без токена проверить нечего, а доверять непроверенному нельзя:
        // «не настроено» обязано означать отказ, а не пропуск.
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(initData))
        {
            return null;
        }

        var fields = Parse(initData);

        if (!fields.TryGetValue("hash", out var hash) || !IsHex(hash))
        {
            return null;
        }

        if (!IsSignatureValid(token, fields, hash))
        {
            return null;
        }

        return ReadPayload(fields);
    }

    /// <summary>
    /// Сравнивает подпись с ожидаемой.
    /// </summary>
    /// <remarks>
    /// Проверяются два варианта строки: с полем <c>signature</c> и без него.
    /// Поле появилось в Bot API 8.0 как отдельная подпись Ed25519 для сторонних
    /// потребителей, и в разных источниках оно то входит в подписываемую строку,
    /// то исключается из неё. Перебор двух вариантов ничего не ослабляет —
    /// подпись в обоих случаях считается на секрете бота, подделать её нельзя, —
    /// но снимает целый класс «вход перестал работать после обновления клиента».
    /// </remarks>
    private static bool IsSignatureValid(string token, Dictionary<string, string> fields, string hash)
    {
        var withoutSignature = BuildCheckString(fields, includeSignature: false);

        if (Matches(token, withoutSignature, hash))
        {
            return true;
        }

        return fields.ContainsKey("signature") && Matches(token, BuildCheckString(fields, includeSignature: true), hash);
    }

    private static bool Matches(string token, string checkString, string hash)
    {
        // Ключ подписи — не сам токен, а HMAC от него с ключом «WebAppData».
        // Это разделяет пространства ключей: тем же токеном подписываются
        // другие данные, и общая схема позволила бы переносить подписи между ними.
        var secretKey = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("WebAppData"),
            Encoding.UTF8.GetBytes(token));

        var expected = HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(checkString));

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Convert.ToHexString(expected)),
            Encoding.ASCII.GetBytes(hash.ToUpperInvariant()));
    }

    private static string BuildCheckString(Dictionary<string, string> fields, bool includeSignature)
    {
        var pairs = fields
            .Where(pair => pair.Key != "hash" && (includeSignature || pair.Key != "signature"))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}");

        return string.Join('\n', pairs);
    }

    /// <summary>Разбирает строку <c>initData</c> вида <c>key=value&amp;…</c>.</summary>
    /// <remarks>
    /// Значения разбираются из формы в обычный текст до подписи: Telegram
    /// подписывает уже раскодированные значения, и «%20» в строке проверки
    /// дал бы несовпадающую подпись.
    /// </remarks>
    private static Dictionary<string, string> Parse(string initData)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in initData.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');

            if (separator <= 0)
            {
                continue;
            }

            var key = pair[..separator];

            // Последнее значение побеждает: дубликаты ключей в подписанной
            // строке всё равно не пройдут проверку, а падать из-за них незачем.
            fields[key] = Decode(pair[(separator + 1)..]);
        }

        return fields;
    }

    private static string Decode(string value)
    {
        try
        {
            // В application/x-www-form-urlencoded пробел кодируется плюсом,
            // а Uri.UnescapeDataString плюс не трогает.
            return Uri.UnescapeDataString(value.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            // Битая процентная последовательность: отдаём как есть, подпись
            // всё равно не сойдётся, и разбирать её незачем.
            return value;
        }
    }

    private TelegramInitData? ReadPayload(Dictionary<string, string> fields)
    {
        if (!fields.TryGetValue("user", out var userJson) ||
            !fields.TryGetValue("auth_date", out var authDateRaw) ||
            !long.TryParse(authDateRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var authDateUnix))
        {
            return null;
        }

        TelegramUserPayload? user;

        try
        {
            user = JsonSerializer.Deserialize<TelegramUserPayload>(userJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (user is null || user.Id == 0)
        {
            return null;
        }

        var authDate = DateTimeOffset.FromUnixTimeSeconds(authDateUnix).UtcDateTime;
        var now = clock.UtcNow;

        // Подпись верна, но выдана слишком давно или помечена будущим временем.
        // Без этой проверки перехваченная строка initData работала бы вечно:
        // подпись у неё настоящая, а срок действия нигде не записан.
        if (authDate > now + ClockSkew || now - authDate > MaxAge)
        {
            return null;
        }

        return new TelegramInitData(user.Id, user.Username, user.FirstName, user.LanguageCode, authDate);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    private static bool IsHex(string value) =>
        value.Length == 64 && value.All(symbol => Uri.IsHexDigit(symbol));

    /// <summary>Пользователь внутри <c>initData</c>. Разбирается только то, что нужно.</summary>
    private sealed record TelegramUserPayload(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("username")] string? Username,
        [property: JsonPropertyName("first_name")] string? FirstName,
        [property: JsonPropertyName("language_code")] string? LanguageCode);
}
