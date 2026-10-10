using System.Globalization;
using System.Security.Claims;
using EnglishTrainer.Domain.Exceptions;

namespace EnglishTrainer.Api.Authentication;

/// <summary>Доступ к данным текущего пользователя из <see cref="ClaimsPrincipal"/>.</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Внутренний идентификатор пользователя из токена.
    /// </summary>
    /// <remarks>
    /// Идентификатор берётся только из проверенного токена. Ни один эндпоинт
    /// не должен принимать его из тела запроса или строки адреса: иначе
    /// достаточно подставить чужой номер, чтобы читать чужой прогресс.
    /// </remarks>
    public static int GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId)
            ? userId
            : throw new UnauthorizedException("В токене нет идентификатора пользователя.");
    }
}
