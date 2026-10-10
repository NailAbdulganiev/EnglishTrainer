using EnglishTrainer.Api.Authentication;
using EnglishTrainer.Application.Services;

namespace EnglishTrainer.Api.Endpoints;

/// <summary>Колоды и словарь: то, что пользователь видит до начала занятий.</summary>
public static class DeckEndpoints
{
    public static IEndpointRouteBuilder MapDeckEndpoints(this IEndpointRouteBuilder app)
    {
        var decks = app.MapGroup("/api/decks")
            .WithTags("Колоды")
            .RequireAuthorization();

        decks.MapGet("/", async (
            HttpContext context,
            DeckService service,
            CancellationToken ct) =>
        {
            var list = await service.GetDecksAsync(context.User.GetUserId(), ct);
            return Results.Ok(list);
        })
        .WithSummary("Колоды с прогрессом пользователя")
        .WithDescription("Прогресс считается по каждому слову колоды: начато и выучено.");

        decks.MapGet("/{slug}", async (
            string slug,
            HttpContext context,
            DeckService service,
            CancellationToken ct) =>
        {
            var deck = await service.GetDeckAsync(context.User.GetUserId(), slug, ct);
            return Results.Ok(deck);
        })
        .WithSummary("Одна колода по её slug");

        decks.MapGet("/{slug}/words", async (
            string slug,
            HttpContext context,
            DeckService service,
            CancellationToken ct) =>
        {
            var words = await service.GetDeckWordsAsync(context.User.GetUserId(), slug, ct);
            return Results.Ok(words);
        })
        .WithSummary("Слова колоды с переводами");

        var words = app.MapGroup("/api/words")
            .WithTags("Словарь")
            .RequireAuthorization();

        words.MapGet("/{id:int}", async (
            int id,
            DeckService service,
            CancellationToken ct) =>
        {
            var word = await service.GetWordAsync(id, ct);
            return Results.Ok(word);
        })
        .WithSummary("Полная карточка слова")
        .WithDescription("Транскрипция, переводы и примеры употребления.");

        return app;
    }
}
