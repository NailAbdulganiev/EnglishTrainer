using System.Reflection;

namespace EnglishTrainer.Api.Endpoints;

/// <summary>
/// Сведения о сборке: версия приложения.
/// </summary>
/// <remarks>
/// <para>
/// Версия берётся из самой сборки (<c>Directory.Build.props</c>), а не пишется
/// в интерфейсе второй раз: два места с одним номером расходятся при первой же
/// выкатке, и человек видит «v1.2» на странице обновлений, когда на сервере
/// работает 1.1.
/// </para>
/// <para>
/// Эндпоинт анонимный: номер версии ничего не раскрывает, а спрашивать его
/// до входа незачем — страница «Что нового» открывается и из формы входа
/// не хуже, чем из профиля.
/// </para>
/// </remarks>
public static class VersionEndpoints
{
    public static IEndpointRouteBuilder MapVersionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/version", () =>
        {
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? "0.0.0";

            // InformationalVersion вместе с номером несёт хеш коммита (…+a1b2c3d),
            // когда сборка идёт из git. В интерфейсе он не нужен: там место
            // короткому номеру, а полный остаётся в ответе для диагностики.
            var shortVersion = version.Split('+')[0];

            return Results.Ok(new { version = shortVersion, full = version });
        })
        .AllowAnonymous()
        .WithTags("Прочее")
        .WithSummary("Версия приложения")
        .WithDescription(
            "Номер версии из сборки. Показывается на странице «Что нового» — " +
            "по нему видно, обновилось ли приложение.");

        return app;
    }
}
