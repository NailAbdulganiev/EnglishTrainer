using EnglishTrainer.Domain.Abstractions;
using Microsoft.Extensions.Options;

namespace EnglishTrainer.Infrastructure.Security;

/// <summary>
/// Выдача токенов доступа в формате JWT (HS256).
/// </summary>
/// <remarks>
/// <para>
/// Токен собран вручную, а не через библиотеку <c>Microsoft.AspNetCore.Authentication.JwtBearer</c>.
/// Причина конкретная: это единственная зависимость, которая не тянется из локального
/// кеша пакетов, а формат JWT — это три сегмента base64url и одна подпись HMAC,
/// то есть примерно сорок строк кода. Побочная выгода: видно, из чего токен состоит.
/// </para>
/// <para>
/// Когда появится доступ к nuget.org, этот класс заменяется на стандартный
/// обработчик аутентификации без изменений в остальном коде — порт
/// <see cref="ITokenIssuer"/> специально объявлен в Domain.
/// </para>
/// </remarks>
public sealed class JwtTokenIssuer(IOptions<JwtOptions> options, IClock clock) : ITokenIssuer
{
    private readonly JwtOptions _options = options.Value;

    public AccessToken Issue(int userId, Guid publicId, string email)
    {
        var issuedAt = clock.UtcNow;
        var expiresAt = issuedAt.AddMinutes(_options.LifetimeMinutes);

        var header = new Dictionary<string, object>
        {
            ["alg"] = JwtTokenFormat.Algorithm,
            ["typ"] = "JWT"
        };

        var payload = new Dictionary<string, object>
        {
            ["sub"] = userId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            // Публичный идентификатор положен отдельным полем: внутренний Id
            // наружу не отдаётся, но клиенту полезно видеть, чей это токен.
            ["pid"] = publicId.ToString(),
            ["email"] = email,
            ["iss"] = _options.Issuer,
            ["aud"] = _options.Audience,
            ["iat"] = JwtTokenFormat.ToUnixSeconds(issuedAt),
            ["nbf"] = JwtTokenFormat.ToUnixSeconds(issuedAt),
            ["exp"] = JwtTokenFormat.ToUnixSeconds(expiresAt),
            // Уникальный идентификатор токена: понадобится, когда появится отзыв
            // (выход из аккаунта, блокировка устройства) — отзывать нужно конкретный токен.
            ["jti"] = Guid.NewGuid().ToString("N")
        };

        var value = JwtTokenFormat.Create(header, payload, _options.Secret);
        return new AccessToken(value, expiresAt);
    }
}
