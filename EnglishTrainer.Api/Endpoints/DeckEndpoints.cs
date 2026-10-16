using EnglishTrainer.Api.Authentication;
using EnglishTrainer.Application.Dtos;
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

        decks.MapPost("/", async (
            CreateDeckRequestDto request,
            HttpContext context,
            CustomDeckService service,
            CancellationToken ct) =>
        {
            var deck = await service.CreateAsync(context.User.GetUserId(), request, ct);
            return Results.Ok(deck);
        })
        .WithSummary("Создать свою колоду")
        .WithDescription(
            "Личная колода работает всеми теми же механизмами, что и системная: " +
            "по ней собирается сессия, ведётся расписание и считается статистика.");

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

        decks.MapDelete("/{slug}", async (
            string slug,
            HttpContext context,
            CustomDeckService service,
            CancellationToken ct) =>
        {
            await service.DeleteAsync(context.User.GetUserId(), slug, ct);
            return Results.NoContent();
        })
        .WithSummary("Удалить свою колоду")
        .WithDescription(
            "Чужая и системная колода отвечают 404, а не 403: 403 подтверждал бы, " +
            "что колода существует, и перебором адресов можно было бы выяснить, " +
            "какие личные колоды есть у других.");

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

        decks.MapPost("/{slug}/words", async (
            string slug,
            AddDeckWordRequestDto request,
            HttpContext context,
            CustomDeckService service,
            CancellationToken ct) =>
        {
            var deck = await service.AddWordAsync(context.User.GetUserId(), slug, request, ct);
            return Results.Ok(deck);
        })
        .WithSummary("Добавить слово в свою колоду")
        .WithDescription(
            "Либо существующее слово по wordId, либо новое в поле word. Слова живут " +
            "в общем словаре: новое слово с уже занятой парой «написание + часть речи» " +
            "переиспользуется, а не создаётся вторым — иначе одно слово училось бы дважды.");

        decks.MapDelete("/{slug}/words/{wordId:int}", async (
            string slug,
            int wordId,
            HttpContext context,
            CustomDeckService service,
            CancellationToken ct) =>
        {
            var deck = await service.RemoveWordAsync(context.User.GetUserId(), slug, wordId, ct);
            return Results.Ok(deck);
        })
        .WithSummary("Убрать слово из своей колоды")
        .WithDescription("Само слово остаётся в словаре: его могут использовать другие колоды.");

        var words = app.MapGroup("/api/words")
            .WithTags("Словарь")
            .RequireAuthorization();

        words.MapGet("/", async (
            string? query,
            CustomDeckService service,
            CancellationToken ct) =>
        {
            var found = await service.SearchWordsAsync(query, ct);
            return Results.Ok(found);
        })
        .WithSummary("Поиск слов в словаре")
        .WithDescription(
            "Совпадение ищется с начала написания или перевода — так запрос опирается " +
            "на индексы и не деградирует с ростом словаря.");

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
