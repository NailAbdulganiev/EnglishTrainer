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

        group.MapPost("/telegram", async (
            TelegramSignInRequestDto request,
            TelegramService telegram,
            CancellationToken ct) =>
        {
            var response = await telegram.SignInAsync(request.InitData, ct);
            return Results.Ok(response);
        })
        .WithSummary("Вход в мини-апп по данным Telegram")
        .WithDescription(
            "Принимает строку initData, подписанную Telegram, проверяет подпись токеном бота " +
            "и выдаёт обычный токен доступа. Пароль не участвует: подпись Telegram — " +
            "единственное доказательство, что запрос пришёл из его клиента. " +
            "Telegram, не привязанный ни к одному аккаунту, получает 403 с инструкцией.");

        group.MapPost("/telegram/link", async (
            TelegramLinkFromMiniAppRequestDto request,
            TelegramService telegram,
            CancellationToken ct) =>
        {
            var response = await telegram.LinkFromInitDataAsync(request.InitData, request.Code, ct);
            return Results.Ok(response);
        })
        .WithSummary("Привязать Telegram по коду из мини-аппа")
        .WithDescription(
            "То же, что команда /link у бота, но без бота: мини-апп уже знает свой аккаунт " +
            "Telegram, и человеку достаточно вставить код из профиля. Подпись initData " +
            "проверяется так же строго — иначе обходной путь стал бы дырой.");

        return app;
    }
}