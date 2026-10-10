using System.Buffers.Text;
using System.Security.Claims;
using System.Text;
using EnglishTrainer.Infrastructure.Security;
using EnglishTrainer.UnitTests.TestDoubles;
using Microsoft.Extensions.Options;

namespace EnglishTrainer.UnitTests.Infrastructure;

/// <summary>
/// Проверка выдачи и проверки токенов доступа.
/// </summary>
/// <remarks>
/// Токен — это единственное, что подтверждает личность пользователя. Поэтому
/// проверяются не только «свой токен проходит», но и все способы его подделать:
/// изменённая полезная нагрузка, чужой ключ подписи, отключённая подпись.
/// </remarks>
public sealed class JwtTokenTests
{
    private const string Secret = "unit-test-signing-key-with-enough-length-1234567890";
    private const string OtherSecret = "another-signing-key-with-enough-length-0987654321";
    private const string Issuer = "EnglishTrainer";
    private const string Audience = "EnglishTrainer";

    private static readonly DateTime Now = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IssuedToken_IsAcceptedAndCarriesUserClaims()
    {
        var (issuer, validator, _) = CreateServices();

        var token = issuer.Issue(userId: 42, publicId: Guid.NewGuid(), email: "user@example.com");

        var result = validator.Validate(token.Value);

        Assert.True(result.IsValid, result.Error);
        var principal = Assert.IsType<ClaimsPrincipal>(result.Principal);
        Assert.Equal("42", principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("user@example.com", principal.FindFirst(ClaimTypes.Name)?.Value);
    }

    [Fact]
    public void IssuedToken_ExpiresAtDeclaredMoment()
    {
        var (issuer, validator, _) = CreateServices(secret: Secret, lifetimeMinutes: 60);

        var token = issuer.Issue(42, Guid.NewGuid(), "user@example.com");

        Assert.Equal(Now.AddMinutes(60), token.ExpiresAtUtc);
        Assert.True(validator.Validate(token.Value).IsValid);
    }

    [Fact]
    public void ExpiredToken_IsRejected()
    {
        var (issuer, validator, clock) = CreateServices(secret: Secret, lifetimeMinutes: 60);
        var token = issuer.Issue(42, Guid.NewGuid(), "user@example.com");

        clock.Advance(TimeSpan.FromMinutes(61));

        var result = validator.Validate(token.Value);

        Assert.False(result.IsValid);
        Assert.Contains("истёк", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void TamperedPayload_IsRejected()
    {
        var (issuer, validator, _) = CreateServices();
        var token = issuer.Issue(42, Guid.NewGuid(), "user@example.com");

        // Подменяем полезную нагрузку, оставляя исходную подпись:
        // так выглядит попытка выдать себя за другого пользователя.
        var parts = token.Value.Split('.');
        var forgedPayload = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            $"{{\"sub\":\"1\",\"iss\":\"{Issuer}\",\"aud\":\"{Audience}\",\"exp\":9999999999}}"));

        var result = validator.Validate($"{parts[0]}.{forgedPayload}.{parts[2]}");

        Assert.False(result.IsValid);
        Assert.Contains("Подпись", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void TokenSignedWithAnotherKey_IsRejected()
    {
        var (issuer, _, _) = CreateServices(secret: Secret);
        var (_, validatorWithOtherKey, _) = CreateServices(secret: OtherSecret);

        var token = issuer.Issue(42, Guid.NewGuid(), "user@example.com");

        Assert.False(validatorWithOtherKey.Validate(token.Value).IsValid);
    }

    [Fact]
    public void TokenWithoutSignature_IsRejected()
    {
        var (_, validator, _) = CreateServices();

        // Классическая уязвимость самодельных токенов: заголовок с alg=none
        // и пустая подпись. Проверка алгоритма обязана это отсечь.
        var header = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}"));
        var expires = new DateTimeOffset(Now.AddHours(1)).ToUnixTimeSeconds();
        var payloadJson = $"{{\"sub\":\"42\",\"iss\":\"{Issuer}\",\"aud\":\"{Audience}\",\"exp\":{expires}}}";
        var payload = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payloadJson));

        var result = validator.Validate($"{header}.{payload}.");

        Assert.False(result.IsValid);
        Assert.Contains("алгоритм", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void TokenForAnotherAudience_IsRejected()
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = Issuer,
            Audience = "SomeOtherService",
            Secret = Secret,
            LifetimeMinutes = 60
        });
        var clock = new FixedClock(Now);
        var token = new JwtTokenIssuer(options, clock).Issue(42, Guid.NewGuid(), "user@example.com");

        var (_, validator, _) = CreateServices();

        Assert.False(validator.Validate(token.Value).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("не-токен")]
    [InlineData("a.b")]
    [InlineData("a.b.c.d")]
    public void MalformedToken_IsRejected(string? token) =>
        Assert.False(CreateServices().Validator.Validate(token).IsValid);

    private static (JwtTokenIssuer Issuer, JwtTokenValidator Validator, FixedClock Clock) CreateServices(
        string secret = Secret,
        int lifetimeMinutes = 60)
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = Issuer,
            Audience = Audience,
            Secret = secret,
            LifetimeMinutes = lifetimeMinutes,
            // Расхождение часов выключено: иначе тест на истечение срока
            // не отличит ожидаемое поведение от поблажки в минуту.
            ClockSkewSeconds = 0
        });

        var clock = new FixedClock(Now);
        return (new JwtTokenIssuer(options, clock), new JwtTokenValidator(options, clock), clock);
    }
}
