using EnglishTrainer.Api.Authentication;
using EnglishTrainer.Application.Dtos;
using EnglishTrainer.Application.Services;

namespace EnglishTrainer.Api.Endpoints;

/// <summary>Профиль и персональные настройки текущего пользователя.</summary>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        // Вся группа требует токен: даже если один эндпоинт забудут закрыть,
        // правило группы продолжит действовать.
        var group = app.MapGroup("/api/me")
            .WithTags("Профиль")
            .RequireAuthorization();

        group.MapGet("/", async (
            HttpContext context,
            ProfileService profiles,
            CancellationToken ct) =>
        {
            var profile = await profiles.GetAsync(context.User.GetUserId(), ct);
            return Results.Ok(profile);
        })
        .WithSummary("Профиль, настройки и сводка прогресса")
        .WithDescription("Всё, что нужно странице профиля: ник, дата регистрации, выучено слов, серия, друзья, избранное.");

        group.MapPut("/profile", async (
            HttpContext context,
            UpdateProfileRequestDto request,
            ProfileService profiles,
            CancellationToken ct) =>
        {
            var user = await profiles.UpdateAsync(context.User.GetUserId(), request, ct);
            return Results.Ok(user);
        })
        .WithSummary("Сменить ник или отображаемое имя")
        .WithDescription("Занятый ник отклоняется: подменять выбранный ник «похожим» нельзя.");

        group.MapPut("/settings", async (
            HttpContext context,
            UpdateSettingsRequestDto request,
            AuthService auth,
            CancellationToken ct) =>
        {
            var settings = await auth.UpdateSettingsAsync(context.User.GetUserId(), request, ct);
            return Results.Ok(settings);
        })
        .WithSummary("Изменить настройки обучения")
        .WithDescription("Лимит новых слов, размер сессии, дневная цель, время напоминания.");

        group.MapPut("/timezone", async (
            HttpContext context,
            UpdateTimeZoneRequestDto request,
            AuthService auth,
            CancellationToken ct) =>
        {
            await auth.UpdateTimeZoneAsync(context.User.GetUserId(), request.TimeZoneId, ct);
            return Results.NoContent();
        })
        .WithSummary("Сменить часовой пояс")
        .WithDescription("Влияет на дневную статистику, серию дней и время напоминаний.");

        return app;
    }
}
