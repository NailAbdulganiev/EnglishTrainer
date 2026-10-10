using System.Text.Encodings.Web;
using EnglishTrainer.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace EnglishTrainer.Api.Authentication;

/// <summary>
/// Аутентификация по токену в заголовке <c>Authorization: Bearer ...</c>.
/// </summary>
/// <remarks>
/// <para>
/// Обработчик встроен в стандартный конвейер аутентификации ASP.NET Core, поэтому
/// эндпоинты закрываются обычным <c>RequireAuthorization()</c>, а идентификатор
/// пользователя доступен через <c>HttpContext.User</c>. Ручная проверка заголовка
/// в каждом обработчике рано или поздно приводит к забытому эндпоинту без проверки.
/// </para>
/// <para>
/// Здесь же проходит граница между «токена нет» и «токен неверен»:
/// в первом случае возвращается <see cref="AuthenticateResult.NoResult"/> —
/// запрос может пройти дальше как анонимный, во втором — отказ.
/// </para>
/// </remarks>
public sealed class TokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private const string BearerPrefix = "Bearer ";

    private readonly JwtTokenValidator _validator;

    public TokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        JwtTokenValidator validator)
        : base(options, logger, encoder)
    {
        _validator = validator;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderNames.Authorization, out var headerValues))
            return Task.FromResult(AuthenticateResult.NoResult());

        var header = headerValues.ToString();

        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var validation = _validator.Validate(header[BearerPrefix.Length..].Trim());

        if (!validation.IsValid || validation.Principal is null)
        {
            // Причина отказа уходит в лог, но не в тело ответа: иначе клиент
            // получает подсказку, чем именно не устроен его токен.
            Logger.LogDebug("Токен отклонён: {Reason}", validation.Error);
            return Task.FromResult(AuthenticateResult.Fail(validation.Error ?? "Некорректный токен."));
        }

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(validation.Principal, Scheme.Name)));
    }
}
