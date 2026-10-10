using System.Buffers.Text;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EnglishTrainer.Domain.Abstractions;
using Microsoft.Extensions.Options;

namespace EnglishTrainer.Infrastructure.Security;

/// <summary>
/// Сборка и разбор JWT: общая часть для выдачи токена и его проверки.
/// </summary>
/// <remarks>
/// Вынесено в отдельный тип, чтобы подпись создавалась и проверялась одним и тем же
/// кодом. Классическая ошибка самодельных токенов — выдавать одним способом,
/// а проверять другим, и в результате принимать подделки.
/// </remarks>
internal static class JwtTokenFormat
{
    public const string Algorithm = "HS256";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        // Порядок полей не важен для подписи, но стабильный вывод удобнее читать.
        WriteIndented = false
    };

    /// <summary>Создаёт подписанный токен: <c>header.payload.signature</c>.</summary>
    public static string Create(
        IReadOnlyDictionary<string, object> header,
        IReadOnlyDictionary<string, object> payload,
        string secret)
    {
        var headerPart = Encode(JsonSerializer.SerializeToUtf8Bytes(header, SerializerOptions));
        var payloadPart = Encode(JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions));
        var signingInput = $"{headerPart}.{payloadPart}";

        var signature = Sign(signingInput, secret);
        return $"{signingInput}.{Encode(signature)}";
    }

    /// <summary>Разбирает токен на части. Возвращает <c>false</c>, если структура неверна.</summary>
    public static bool TrySplit(string token, out string headerPart, out string payloadPart, out string signaturePart)
    {
        headerPart = payloadPart = signaturePart = string.Empty;

        var parts = token.Split('.');
        if (parts.Length != 3)
            return false;

        headerPart = parts[0];
        payloadPart = parts[1];
        signaturePart = parts[2];
        return true;
    }

    /// <summary>Проверяет подпись. Сравнение — устойчивое по времени.</summary>
    public static bool VerifySignature(string headerPart, string payloadPart, string signaturePart, string secret)
    {
        byte[] provided;
        try
        {
            provided = Base64Url.DecodeFromChars(signaturePart);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = Sign($"{headerPart}.{payloadPart}", secret);

        // FixedTimeEquals, а не SequenceEqual: обычное сравнение прекращает работу
        // на первом несовпавшем байте и по времени ответа выдаёт правильный префикс подписи.
        return CryptographicOperations.FixedTimeEquals(provided, expected);
    }

    /// <summary>Читает полезную нагрузку. Возвращает <c>null</c>, если это не JSON-объект.</summary>
    public static JsonElement? ReadPayload(string payloadPart)
    {
        try
        {
            var json = Base64Url.DecodeFromChars(payloadPart);
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Читает имя алгоритма из заголовка.</summary>
    public static string? ReadAlgorithm(string headerPart)
    {
        try
        {
            var json = Base64Url.DecodeFromChars(headerPart);
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("alg", out var alg) ? alg.GetString() : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    public static long ToUnixSeconds(DateTime moment) =>
        new DateTimeOffset(DateTime.SpecifyKind(moment, DateTimeKind.Utc)).ToUnixTimeSeconds();

    private static byte[] Sign(string signingInput, string secret) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(signingInput));

    private static string Encode(byte[] data) => Base64Url.EncodeToString(data);
}

/// <summary>
/// Результат проверки токена: либо проверенные утверждения, либо причина отказа.
/// </summary>
/// <param name="IsValid">Прошёл ли токен проверку целиком.</param>
/// <param name="Error">Причина отказа — пишется в лог, наружу не отдаётся.</param>
/// <param name="Principal">Пользователь, если токен действителен.</param>
public sealed record JwtValidationResult(bool IsValid, string? Error, ClaimsPrincipal? Principal)
{
    public static JwtValidationResult Fail(string error) => new(false, error, null);

    public static JwtValidationResult Success(ClaimsPrincipal principal) => new(true, null, principal);
}

/// <summary>
/// Проверка токена: подпись, срок действия, издатель и получатель.
/// </summary>
/// <remarks>
/// Проверяются все четыре условия, и это не формальность. Без проверки <c>aud</c>
/// токен, выданный для другого сервиса с тем же секретом, принимается как свой;
/// без <c>alg</c> появляется классическая уязвимость «alg: none».
/// </remarks>
public sealed class JwtTokenValidator(IOptions<JwtOptions> options, IClock clock)
{
    private readonly JwtOptions _options = options.Value;

    public JwtValidationResult Validate(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return JwtValidationResult.Fail("Токен не передан.");

        if (!JwtTokenFormat.TrySplit(token, out var headerPart, out var payloadPart, out var signaturePart))
            return JwtValidationResult.Fail("Токен не соответствует формату header.payload.signature.");

        // Алгоритм берётся из заголовка, поэтому его обязательно нужно сверить
        // с ожидаемым: иначе достаточно прислать alg=none, и подпись не проверяется.
        var algorithm = JwtTokenFormat.ReadAlgorithm(headerPart);
        if (!string.Equals(algorithm, JwtTokenFormat.Algorithm, StringComparison.Ordinal))
            return JwtValidationResult.Fail($"Неподдерживаемый алгоритм подписи: '{algorithm}'.");

        if (!JwtTokenFormat.VerifySignature(headerPart, payloadPart, signaturePart, _options.Secret))
            return JwtValidationResult.Fail("Подпись не совпадает.");

        var payload = JwtTokenFormat.ReadPayload(payloadPart);
        if (payload is not { } claims)
            return JwtValidationResult.Fail("Полезная нагрузка токена не читается.");

        var now = JwtTokenFormat.ToUnixSeconds(clock.UtcNow);
        var skew = _options.ClockSkewSeconds;

        if (claims.TryGetProperty("nbf", out var notBefore)
            && notBefore.TryGetInt64(out var notBeforeSeconds)
            && now + skew < notBeforeSeconds)
        {
            return JwtValidationResult.Fail("Токен ещё не действует.");
        }

        if (!claims.TryGetProperty("exp", out var expires)
            || !expires.TryGetInt64(out var expiresSeconds))
        {
            return JwtValidationResult.Fail("В токене нет срока действия.");
        }

        if (now - skew >= expiresSeconds)
            return JwtValidationResult.Fail("Срок действия токена истёк.");

        if (!string.Equals(ReadString(claims, "iss"), _options.Issuer, StringComparison.Ordinal))
            return JwtValidationResult.Fail("Неверный издатель токена.");

        if (!string.Equals(ReadString(claims, "aud"), _options.Audience, StringComparison.Ordinal))
            return JwtValidationResult.Fail("Неверный получатель токена.");

        var subject = ReadString(claims, "sub");
        if (!int.TryParse(subject, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
            return JwtValidationResult.Fail("В токене нет корректного идентификатора пользователя.");

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString(CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Name, ReadString(claims, "email") ?? string.Empty),
                new Claim("pid", ReadString(claims, "pid") ?? string.Empty),
                new Claim("jti", ReadString(claims, "jti") ?? string.Empty)
            ],
            authenticationType: TokenAuthenticationDefaults.Scheme);

        return JwtValidationResult.Success(new ClaimsPrincipal(identity));
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>Имена, относящиеся к схеме аутентификации по токену.</summary>
public static class TokenAuthenticationDefaults
{
    public const string Scheme = "Bearer";
}
