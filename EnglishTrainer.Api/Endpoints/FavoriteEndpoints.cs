using EnglishTrainer.Api.Authentication;
using EnglishTrainer.Application.Dtos;
using EnglishTrainer.Application.Services;

namespace EnglishTrainer.Api.Endpoints;

/// <summary>
/// Избранные слова.
/// </summary>
/// <remarks>
/// Группа <c>/api/words</c> объявлена и здесь, и в <see cref="DeckEndpoints"/>:
/// маршруты складываются, а группировка по тегам даёт разным операциям
/// над словом свои разделы в документации API.
/// </remarks>
public static class FavoriteEndpoints
{
    public static IEndpointRouteBuilder MapFavoriteEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/favorites")
            .WithTags("Избранное")
            .RequireAuthorization();

        group.MapGet("/", async (
            HttpContext context,
            FavoriteService service,
            CancellationToken ct) =>
        {
            var list = await service.GetAsync(context.User.GetUserId(), ct);
            return Results.Ok(list);
        })
        .WithSummary("Избранные слова с прогрессом")
        .WithDescription(
            "Отмеченные слова идут в начале очереди повторений и доступны " +
            "отдельной тренировкой, которая не ждёт назначенного срока.");

        group.MapGet("/ids", async (
            HttpContext context,
            FavoriteService service,
            CancellationToken ct) =>
        {
            var ids = await service.GetWordIdsAsync(context.User.GetUserId(), ct);
            return Results.Ok(ids);
        })
        .WithSummary("Идентификаторы избранных слов")
        .WithDescription(
            "Отдельный эндпоинт: интерфейсу нужно отметить звёздочками слова " +
            "в списках, и тянуть ради этого переводы и примеры незачем.");

        var words = app.MapGroup("/api/words")
            .WithTags("Избранное")
            .RequireAuthorization();

        words.MapPut("/{id:int}/favorite", async (
            int id,
            SetFavoriteRequestDto request,
            HttpContext context,
            FavoriteService service,
            CancellationToken ct) =>
        {
            var isFavorite = await service.SetAsync(context.User.GetUserId(), id, request.IsFavorite, ct);
            return Results.Ok(new FavoriteStateDto(id, isFavorite));
        })
        .WithSummary("Поставить или снять отметку «в избранном»")
        .WithDescription("Операция идемпотентна: повторный вызов с тем же значением ничего не меняет.");

        return app;
    }
}
