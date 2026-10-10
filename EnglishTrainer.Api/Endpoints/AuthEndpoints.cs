using EnglishTrainer.Application.Dtos;
using EnglishTrainer.Application.Services;

namespace EnglishTrainer.Api.Endpoints;

/// <summary>
/// Вход и регистрация.
/// </summary>
/// <remarks>
/// Обработчики предельно тонкие: разбор запроса, вызов сервиса, код ответа.
/// Вся проверка данных живёт в <see cref="AuthService"/>, потому что те же
/// правила нужны боту, фоновому воркеру и тестам — дублировать их в HTTP-слое нельзя.
/// </remarks>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("Аутентификация")
            .AllowAnonymous();

        group.MapPost("/register", async (
            RegisterRequestDto request,
            AuthService auth,
            CancellationToken ct) =>
        {
            var response = await auth.RegisterAsync(request, ct);
            return Results.Ok(response);
        })
        .WithSummary("Регистрация по email")
        .WithDescription("Создаёт пользователя с настройками по умолчанию и сразу выдаёт токен доступа.");

        group.MapPost("/login", async (
            LoginRequestDto request,
            AuthService auth,
            CancellationToken ct) =>
        {
            var response = await auth.LoginAsync(request, ct);
            return Results.Ok(response);
        })
        .WithSummary("Вход по email и паролю");

        return app;
    }
}
