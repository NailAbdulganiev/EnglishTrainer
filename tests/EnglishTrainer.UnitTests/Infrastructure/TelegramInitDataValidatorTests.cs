using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EnglishTrainer.Application.Telegram;
using EnglishTrainer.Infrastructure.Telegram;
using EnglishTrainer.UnitTests.TestDoubles;
using Microsoft.Extensions.Options;

namespace EnglishTrainer.UnitTests.Infrastructure;

/// <summary>
/// Проверка подписи данных мини-аппа Telegram.
/// </summary>
/// <remarks>
/// <para>
/// Это единственная защита входа в мини-апп: пароля там нет, и если подпись
/// не проверяется, чужой идентификатор Telegram достаточно просто отправить
/// в запросе. Поэтому набор проверяет не только «валидное принимается»,
/// но и каждый способ подделки по отдельности.
/// </para>
/// <para>
/// Строка <c>initData</c> собирается здесь тем же алгоритмом, что описан
/// в документации Telegram. Важная деталь, на которой легко ошибиться:
/// сама строка склеена амперсандами и закодирована как query string,
/// а <b>подписывается</b> другая строка — из раскодированных значений,
/// соединённых переводом строки. Тест повторяет именно это разделение,
/// иначе он проверял бы сам себя, а не алгоритм.
/// </para>
/// <para>
/// Совпадение с настоящим Telegram проверяется не тестом, а живым запуском:
/// здесь фиксируется само правило.
/// </para>
/// </remarks>
public sealed class TelegramInitDataValidatorTests
{
    private const string Token = "1234567890:TEST-TOKEN-FOR-UNIT-TESTS-ONLY";
    private const string OtherToken = "0987654321:ANOTHER-TOKEN";

    private static readonly DateTime Now = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

    private static TelegramInitDataValidator Validator(string token = Token, DateTime? now = null) =>
        new(Options.Create(new TelegramOptions { BotToken = token }), new FixedClock(now ?? Now));

    [Fact]
    public void Validate_WithCorrectlySignedData_ReturnsUser()
    {
        var initData = Build(userId: 4242, username: "ivan", firstName: "Иван");

        var result = Validator().Validate(initData);

        Assert.NotNull(result);
        Assert.Equal(4242L, result!.UserId);
        Assert.Equal("ivan", result.Username);
        Assert.Equal("Иван", result.FirstName);
    }

    [Fact]
    public void Validate_WithSignatureFromAnotherToken_ReturnsNull()
    {
        // Тот же приём, что и подделка JWT: данные настоящие, но подписаны
        // не нашим ботом. Без проверки ключа такой запрос вошёл бы в чужой аккаунт.
        var initData = Build(userId: 4242, token: OtherToken);

        Assert.Null(Validator().Validate(initData));
    }

    [Theory]
    [InlineData("user")]
    [InlineData("auth_date")]
    [InlineData("query_id")]
    public void Validate_WithTamperedField_ReturnsNull(string field)
    {
        var initData = Build(userId: 4242, queryId: "AAA");

        var tampered = initData.Replace($"{field}=", $"{field}=X", StringComparison.Ordinal);

        Assert.Null(Validator().Validate(tampered));
    }

    [Fact]
    public void Validate_WithoutHash_ReturnsNull()
    {
        var initData = Build(userId: 4242);

        var withoutHash = initData[..initData.LastIndexOf('&')];

        Assert.Null(Validator().Validate(withoutHash));
    }

    [Fact]
    public void Validate_WithMalformedHash_ReturnsNull()
    {
        // Хеш не той длины или не hex: сравнение обязано отказать, а не упасть.
        var initData = Build(userId: 4242);

        var broken = initData[..(initData.LastIndexOf("hash=", StringComparison.Ordinal) + "hash=".Length)]
                     + "not-a-hash";

        Assert.Null(Validator().Validate(broken));
    }

    [Fact]
    public void Validate_WithExpiredAuthDate_ReturnsNull()
    {
        // Подпись настоящая, но выдана двое суток назад. Без проверки срока
        // перехваченная строка initData работала бы вечно.
        var initData = Build(userId: 4242, authDate: Now.AddHours(-49));

        Assert.Null(Validator().Validate(initData));
    }

    [Fact]
    public void Validate_WithAuthDateFromFuture_ReturnsNull()
    {
        var initData = Build(userId: 4242, authDate: Now.AddHours(2));

        Assert.Null(Validator().Validate(initData));
    }

    [Fact]
    public void Validate_WithSmallClockSkew_AcceptsIt()
    {
        // Часы клиента могут спешить на пару минут. Отказывать из-за этого
        // означало бы «вход не работает» у части людей без всякой причины.
        var initData = Build(userId: 4242, authDate: Now.AddMinutes(2));

        Assert.NotNull(Validator().Validate(initData));
    }

    [Fact]
    public void Validate_WithUppercaseHash_AcceptsIt()
    {
        // Telegram присылает hex в нижнем регистре, но сравнение не должно
        // зависеть от регистра: это разные записи одного и того же числа.
        var fields = Fields(userId: 4242, authDate: Now, queryId: null);

        Assert.NotNull(Validator().Validate(Sign(fields, Token, includeSignature: false, uppercaseHash: true)));
    }

    [Fact]
    public void Validate_WhenSignatureIsExcludedFromCheckString_AcceptsIt()
    {
        // Поле signature появилось в Bot API 8.0 для сторонней проверки, и разные
        // клиенты то включают его в подписываемую строку, то исключают.
        // Принимаются оба варианта: подпись в обоих считается на секрете бота,
        // подделать её нельзя, а «вход перестал работать после обновления
        // клиента» — вполне реальная поломка.
        var fields = Fields(userId: 4242, authDate: Now, queryId: null);
        fields["signature"] = "some-ed25519-signature";

        Assert.NotNull(Validator().Validate(Sign(fields, Token, includeSignature: false)));
        Assert.NotNull(Validator().Validate(Sign(fields, Token, includeSignature: true)));
    }

    [Fact]
    public void Validate_WithoutBotToken_ReturnsNull()
    {
        // Ненастроенный Telegram обязан означать отказ, а не пропуск:
        // «проверить нечем» и «проверка пройдена» — разные вещи.
        var initData = Build(userId: 4242, token: string.Empty);

        Assert.Null(Validator(token: string.Empty).Validate(initData));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("не-параметры-вовсе")]
    [InlineData("hash=&user=%7B%7D")]
    [InlineData("user=%7B%22id%22%3A1%7D&hash=0000000000000000000000000000000000000000000000000000000000000000")]
    public void Validate_WithUnusableInput_ReturnsNull(string? initData) =>
        Assert.Null(Validator().Validate(initData));

    // ── Сборка подписанной строки ────────────────────────────────────────────

    private static string Build(
        long userId,
        string? username = null,
        string? firstName = "Иван",
        DateTime? authDate = null,
        string? queryId = null,
        string token = Token) =>
        Sign(Fields(userId, authDate ?? Now, queryId, username, firstName), token, includeSignature: false);

    private static SortedDictionary<string, string> Fields(
        long userId,
        DateTime authDate,
        string? queryId,
        string? username = null,
        string? firstName = "Иван")
    {
        var user = new StringBuilder($"{{\"id\":{userId}");

        if (firstName is not null)
        {
            user.Append($",\"first_name\":\"{firstName}\"");
        }

        if (username is not null)
        {
            user.Append($",\"username\":\"{username}\"");
        }

        user.Append('}');

        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["user"] = user.ToString(),
            ["auth_date"] = ToUnix(authDate).ToString(CultureInfo.InvariantCulture)
        };

        if (queryId is not null)
        {
            fields["query_id"] = queryId;
        }

        return fields;
    }

    /// <summary>
    /// Собирает строку <c>initData</c> так, как её собирает Telegram.
    /// </summary>
    /// <param name="includeSignature">
    /// Входит ли поле <c>signature</c> в подписываемую строку. Само поле при этом
    /// остаётся в <c>initData</c> в обоих случаях — меняется только подпись.
    /// </param>
    private static string Sign(
        SortedDictionary<string, string> fields, string token, bool includeSignature, bool uppercaseHash = false)
    {
        var checkString = CheckString(fields, includeSignature);

        var hash = Convert.ToHexString(
            HMACSHA256.HashData(Secret(token), Encoding.UTF8.GetBytes(checkString)));

        if (!uppercaseHash)
        {
            hash = hash.ToLowerInvariant();
        }

        var query = string.Join('&', fields.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"));

        return $"{query}&hash={hash}";
    }

    private static string CheckString(SortedDictionary<string, string> fields, bool includeSignature) =>
        string.Join('\n', fields
            .Where(pair => includeSignature || pair.Key != "signature")
            .Select(pair => $"{pair.Key}={pair.Value}"));

    private static byte[] Secret(string token) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(token));

    private static long ToUnix(DateTime moment) =>
        new DateTimeOffset(DateTime.SpecifyKind(moment, DateTimeKind.Utc)).ToUnixTimeSeconds();
}
