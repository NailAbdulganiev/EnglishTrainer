using EnglishTrainer.Api.Authentication;
using EnglishTrainer.Application.Dtos;
using EnglishTrainer.Application.Services;

namespace EnglishTrainer.Api.Endpoints;

/// <summary>Друзья: заявки по нику и прогресс друг друга.</summary>
public static class FriendEndpoints
{
    public static IEndpointRouteBuilder MapFriendEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/friends")
            .WithTags("Друзья")
            .RequireAuthorization();

        group.MapGet("/", async (
            HttpContext context,
            FriendService service,
            CancellationToken ct) =>
        {
            var overview = await service.GetOverviewAsync(context.User.GetUserId(), ct);
            return Results.Ok(overview);
        })
        .WithSummary("Друзья, входящие и исходящие заявки")
        .WithDescription("Для каждого друга возвращается его прогресс: выучено слов, серия дней, ответы за сегодня.");

        // Маршрут объявлен до /{username}: иначе «leaderboard» можно было бы
        // принять за ник, и запрос уходил бы в обработчик удаления друга.
        group.MapGet("/leaderboard", async (
            HttpContext context,
            LeaderboardService service,
            CancellationToken ct) =>
        {
            var leaderboard = await service.GetAsync(context.User.GetUserId(), ct);
            return Results.Ok(leaderboard);
        })
        .WithSummary("Недельный рейтинг среди друзей")
        .WithDescription(
            "Окно — последние семь дней; считается в часовом поясе каждого участника, " +
            "поэтому у друзей из разных поясов «неделя» своя. " +
            "Порядок: слова за неделю, затем ответы, затем серия дней.");

        group.MapPost("/", async (
            AddFriendRequestDto request,
            HttpContext context,
            FriendService service,
            CancellationToken ct) =>
        {
            await service.SendRequestAsync(context.User.GetUserId(), request.Username, ct);
            return Results.NoContent();
        })
        .WithSummary("Отправить заявку в друзья по нику")
        .WithDescription(
            "Если встречная заявка уже есть, дружба оформляется сразу: " +
            "намерение обоих очевидно, и ждать повторного подтверждения незачем.");

        group.MapPost("/{username}/accept", async (
            string username,
            HttpContext context,
            FriendService service,
            CancellationToken ct) =>
        {
            await service.AcceptAsync(context.User.GetUserId(), username, ct);
            return Results.NoContent();
        })
        .WithSummary("Принять входящую заявку");

        group.MapPost("/{username}/decline", async (
            string username,
            HttpContext context,
            FriendService service,
            CancellationToken ct) =>
        {
            await service.DeclineAsync(context.User.GetUserId(), username, ct);
            return Results.NoContent();
        })
        .WithSummary("Отклонить входящую заявку")
        .WithDescription("Позже заявку можно отправить заново: отклонение не блокирует навсегда.");

        group.MapDelete("/{username}", async (
            string username,
            HttpContext context,
            FriendService service,
            CancellationToken ct) =>
        {
            await service.RemoveAsync(context.User.GetUserId(), username, ct);
            return Results.NoContent();
        })
        .WithSummary("Убрать из друзей или отменить свою заявку");

        var users = app.MapGroup("/api/users")
            .WithTags("Друзья")
            .RequireAuthorization();

        users.MapGet("/search", async (
            string? query,
            HttpContext context,
            FriendService service,
            CancellationToken ct) =>
        {
            var found = await service.SearchAsync(context.User.GetUserId(), query, ct);
            return Results.Ok(found);
        })
        .WithSummary("Поиск людей по началу ника")
        .WithDescription(
            "Для каждого найденного возвращается состояние связи: можно отправить заявку, " +
            "заявка уже отправлена, вас пригласили или вы уже друзья.");

        // Маршрут объявлен отдельным шаблоном, а не через /api/friends: профиль
        // открывается и из списка друзей, и из рейтинга, где человек уже друг.
        users.MapGet("/{username}/profile", async (
            string username,
            HttpContext context,
            ProfileService profiles,
            CancellationToken ct) =>
        {
            var profile = await profiles.GetPublicAsync(context.User.GetUserId(), username, ct);
            return Results.Ok(profile);
        })
        .WithSummary("Профиль другого игрока")
        .WithDescription(
            "Открыт только друзьям и самому пользователю. Для постороннего отвечает 404, " +
            "а не 403: отказ «запрещено» подтверждал бы, что такой ник существует.");

        return app;
    }
}
