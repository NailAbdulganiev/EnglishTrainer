using EnglishTrainer.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EnglishTrainer.Api.ErrorHandling;

/// <summary>
/// Единая обработка ошибок: доменные исключения превращаются в коды HTTP.
/// </summary>
/// <remarks>
/// <para>
/// Соответствие задаётся в одном месте, а не в каждом обработчике. Иначе
/// «пользователь не найден» в одном эндпоинте отдаёт 404, в другом 400,
/// а в третьем 500 — и клиент не может написать общую обработку.
/// </para>
/// <para>
/// Текст внутренних ошибок наружу не уходит: сообщение вида «relation
/// card_states does not exist» — это подсказка тому, кто ищет вход в систему.
/// Он остаётся в логах вместе со стеком.
/// </para>
/// </remarks>
public sealed class AppExceptionHandler(ILogger<AppExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Не найдено"),
            ConflictException => (StatusCodes.Status409Conflict, "Конфликт состояния"),
            ValidationException => (StatusCodes.Status400BadRequest, "Некорректные данные"),
            UnauthorizedException => (StatusCodes.Status401Unauthorized, "Требуется аутентификация"),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Действие запрещено"),

            // 502, а не 500: сервер работает, отказал тот, к кому он обращался
            // (почтовый сервер, Telegram). Текст показывается человеку — иначе
            // он будет ждать письмо, которое не отправлено.
            DeliveryException => (StatusCodes.Status502BadGateway, "Внешний сервис не ответил"),

            _ => (StatusCodes.Status500InternalServerError, "Внутренняя ошибка сервера")
        };

        var isExpected = statusCode != StatusCodes.Status500InternalServerError;

        if (isExpected)
        {
            logger.LogInformation(
                "Ожидаемая ошибка {StatusCode} на {Method} {Path}: {Message}",
                statusCode, httpContext.Request.Method, httpContext.Request.Path, exception.Message);
        }
        else
        {
            logger.LogError(
                exception,
                "Необработанная ошибка на {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);
        }

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = isExpected ? exception.Message : null,
            Instance = httpContext.Request.Path
        };

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }
}
