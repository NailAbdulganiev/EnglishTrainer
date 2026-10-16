using EnglishTrainer.Api.Authentication;
using EnglishTrainer.Application.Dtos;
using EnglishTrainer.Application.Media;
using EnglishTrainer.Application.Services;
using EnglishTrainer.Domain.Abstractions;
using EnglishTrainer.Domain.Exceptions;

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

        group.MapPut("/password", async (
            HttpContext context,
            ChangePasswordRequestDto request,
            AuthService auth,
            CancellationToken ct) =>
        {
            await auth.ChangePasswordAsync(context.User.GetUserId(), request, ct);
            return Results.NoContent();
        })
        .WithSummary("Сменить пароль")
        .WithDescription(
            "Текущий пароль обязателен: иначе украденный токен превращался бы " +
            "в полный захват аккаунта — достаточно сменить пароль и не пустить владельца.");

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

        // ── Аватар ───────────────────────────────────────────────────────────
        group.MapPost("/avatar", async (
            HttpContext context,
            IFormFile file,
            ProfileService profiles,
            CancellationToken ct) =>
        {
            // Предел длины тела запроса проверяет конвейер, и его нарушение —
            // это исключение чтения формы, а не понятная ошибка. Переводим его
            // в обычную ошибку валидации: клиент должен увидеть текст, а не 500.
            try
            {
                await using var content = file.OpenReadStream();

                var user = await profiles.SetAvatarAsync(
                    context.User.GetUserId(),
                    new AvatarUpload(content, file.ContentType, file.Length),
                    ct);

                return Results.Ok(user);
            }
            catch (InvalidDataException)
            {
                throw new ValidationException(
                    $"Файл больше {AvatarPolicy.MaxBytes / (1024 * 1024)} МиБ.");
            }
        })
        // Защита от CSRF здесь не нужна и не может работать: токен передаётся
        // заголовком Authorization, а не cookie, поэтому браузер не прикладывает
        // его к чужому запросу сам. Проверка антифорджери-маркера отклонила бы
        // легальную загрузку из приложения, у которого маркера нет.
        .DisableAntiforgery()
        .WithSummary("Загрузить или заменить аватар")
        .WithDescription(
            "Принимает изображение JPEG, PNG или WebP до 2 МиБ. Формат проверяется " +
            "по содержимому файла, а не по его имени: каталог аватаров раздаётся статикой.");

        group.MapDelete("/avatar", async (
            HttpContext context,
            ProfileService profiles,
            CancellationToken ct) =>
        {
            var user = await profiles.RemoveAvatarAsync(context.User.GetUserId(), ct);
            return Results.Ok(user);
        })
        .WithSummary("Удалить аватар")
        .WithDescription("Повторный вызов не ошибка: после удаления показываются инициалы.");

        // ── Telegram ─────────────────────────────────────────────────────────
        group.MapGet("/telegram", async (
            HttpContext context,
            TelegramService telegram,
            CancellationToken ct) =>
        {
            var status = await telegram.GetStatusAsync(context.User.GetUserId(), ct);
            return Results.Ok(status);
        })
        .WithSummary("Состояние привязки Telegram")
        .WithDescription("Привязан ли Telegram и когда привязка сделана.");

        group.MapPost("/telegram/link", async (
            HttpContext context,
            TelegramService telegram,
            CancellationToken ct) =>
        {
            var link = await telegram.CreateLinkCodeAsync(context.User.GetUserId(), ct);
            return Results.Ok(link);
        })
        .WithSummary("Получить код привязки Telegram")
        .WithDescription(
            "Выдаёт одноразовый код на 15 минут и ссылку на бота. Прежние коды при этом " +
            "гасятся: действующим остаётся ровно один, иначе отменить привязку было бы нельзя.");

        group.MapDelete("/telegram", async (
            HttpContext context,
            TelegramService telegram,
            CancellationToken ct) =>
        {
            var status = await telegram.UnlinkAsync(context.User.GetUserId(), ct);
            return Results.Ok(status);
        })
        .WithSummary("Отвязать Telegram")
        .WithDescription("После отвязки напоминания в Telegram приходить перестают.");

        return app;
    }
}
