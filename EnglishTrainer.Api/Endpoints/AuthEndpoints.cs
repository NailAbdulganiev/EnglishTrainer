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
        .WithDescription(
            "Создаёт пользователя с настройками по умолчанию и сразу выдаёт токен доступа. " +
            "Ник можно задать явно; если он занят, регистрация отклоняется — подменять " +
            "выбранный человеком ник «похожим» нельзя.");

        group.MapPost("/login", async (
            LoginRequestDto request,
            AuthService auth,
            CancellationToken ct) =>
        {
            var response = await auth.LoginAsync(request, ct);
            return Results.Ok(response);
        })
        .WithSummary("Вход по email или нику")
        .WithDescription(
            "В одном поле принимается и email, и ник: человек помнит ник, который сам " +
            "придумал, а email под рукой не всегда.");

        group.MapPost("/forgot-password", async (
            ForgotPasswordRequestDto request,
            HttpContext context,
            PasswordResetService service,
            CancellationToken ct) =>
        {
            // Адрес формы сброса собирается из самого запроса: интерфейс отдаёт
            // тот же сервер, поэтому отдельная настройка адреса не нужна.
            // За обратным прокси адрес придётся задавать явно.
            var resetLink = $"{context.Request.Scheme}://{context.Request.Host}/#/reset";

            await service.RequestAsync(request.Login, resetLink, ct);

            // Ответ всегда 204, даже если пользователя нет: иначе форма
            // превращается в способ узнать, какие адреса зарегистрированы.
            return Results.NoContent();
        })
        .WithSummary("Запросить ссылку для сброса пароля")
        .WithDescription(
            "Всегда отвечает 204. Почтовый провайдер в этой сборке не настроен, " +
            "поэтому письмо со ссылкой выводится в журнал сервера.");

        group.MapPost("/reset-password", async (
            ResetPasswordRequestDto request,
            PasswordResetService service,
            CancellationToken ct) =>
        {
            await service.ResetAsync(request.Token, request.NewPassword, ct);
            return Results.NoContent();
        })
        .WithSummary("Установить новый пароль по ссылке")
        .WithDescription("Ссылка действует ограниченное время и срабатывает один раз.");

        return app;
    }
}
