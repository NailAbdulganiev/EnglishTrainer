using EnglishTrainer.Api.Authentication;
using EnglishTrainer.Application.Services;

namespace EnglishTrainer.Api.Endpoints;

/// <summary>Прогресс: сводка и активность по дням.</summary>
public static class StatsEndpoints
{
    public static IEndpointRouteBuilder MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/stats")
            .WithTags("Прогресс")
            .RequireAuthorization();

        group.MapGet("/summary", async (
            HttpContext context,
            StatsService service,
            CancellationToken ct) =>
        {
            var summary = await service.GetSummaryAsync(context.User.GetUserId(), ct);
            return Results.Ok(summary);
        })
        .WithSummary("Сводка прогресса")
        .WithDescription("Выучено, в работе, к повторению, серия дней и удержание за 30 дней.");

        group.MapGet("/activity", async (
            HttpContext context,
            DateOnly? from,
            DateOnly? to,
            StatsService service,
            CancellationToken ct) =>
        {
            // Период по умолчанию — последние 30 дней: это то, что нужно
            // в 95% случаев, и клиенту не приходится считать даты самому.
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var range = new DateRange(from ?? today.AddDays(-29), to ?? today);

            var activity = await service.GetActivityAsync(context.User.GetUserId(), range.From, range.To, ct);
            return Results.Ok(activity);
        })
        .WithSummary("Активность по дням и кривая удержания")
        .WithDescription("Дни без занятий возвращаются с нулями, чтобы на графике были видны пропуски.");

        return app;
    }

    /// <summary>Период запроса. Отдельный тип нужен, чтобы не тащить два параметра через сигнатуру.</summary>
    private readonly record struct DateRange(DateOnly From, DateOnly To);
}
