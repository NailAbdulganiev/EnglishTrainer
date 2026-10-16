using EnglishTrainer.Application.Services;
using EnglishTrainer.Application.Telegram;
using EnglishTrainer.Bot.Telegram;
using EnglishTrainer.Domain.Exceptions;
using Microsoft.Extensions.Options;

namespace EnglishTrainer.Bot;

/// <summary>
/// Разбор команд бота.
/// </summary>
/// <remarks>
/// <para>
/// Обработчик живёт в области видимости (scope) на одно обновление: сервисы
/// Application работают с базой через контекст EF Core, а он не потокобезопасен
/// и не рассчитан на долгую жизнь. Поэтому scope создаёт вызывающий цикл
/// опроса, а не этот класс.
/// </para>
/// <para>
/// Ошибки наружу не пробрасываются: сбой на одном сообщении не должен
/// останавливать опрос. Человек в худшем случае не получит ответа, а следующий
/// запрос обработается нормально.
/// </para>
/// </remarks>
internal sealed class TelegramBotService(
    TelegramService telegram,
    TelegramReminderService reminders,
    ITelegramApi api,
    IOptions<TelegramOptions> options,
    ILogger<TelegramBotService> logger)
{
    public async Task HandleAsync(TelegramUpdate update, CancellationToken ct)
    {
        var message = update.Message;
        var chatId = message?.Chat?.Id;

        // Обновления приходят и на служебные события, а из сообщений интересны
        // только текстовые: на картинку или стикер бот ответить не может.
        if (chatId is null || string.IsNullOrWhiteSpace(message?.Text))
        {
            return;
        }

        var (command, argument) = Split(message.Text);

        logger.LogDebug("Команда {Command} от чата {ChatId}.", command, chatId);

        try
        {
            switch (command)
            {
                case "/start":
                    await StartAsync(chatId.Value, argument, message.From?.FirstName, ct);
                    break;

                case "/link":
                    await LinkAsync(chatId.Value, argument, ct);
                    break;

                case "/word":
                    await WordAsync(chatId.Value, ct);
                    break;

                case "/today":
                    await TodayAsync(chatId.Value, ct);
                    break;

                case "/stop":
                    await ToggleRemindersAsync(chatId.Value, enabled: false, ct);
                    break;

                case "/on":
                    await ToggleRemindersAsync(chatId.Value, enabled: true, ct);
                    break;

                case "/help":
                    await SendAsync(chatId.Value, BotMessages.Help(), withButton: true, ct);
                    break;

                default:
                    await SendAsync(chatId.Value, BotMessages.Unknown(), withButton: false, ct);
                    break;
            }
        }
        catch (AppException ex)
        {
            // Ожидаемая ошибка: её текст написан для человека, и показать его
            // честнее, чем молчать. Так выглядит, например, устаревший код привязки.
            logger.LogInformation("Команда {Command} отклонена: {Message}", command, ex.Message);
            await SendAsync(chatId.Value, ex.Message, withButton: false, ct);
        }
        catch (TelegramApiException ex)
        {
            // Ответить не удалось: чаще всего человека заблокировал бота.
            // Повторять бессмысленно, но и падать из-за этого незачем.
            logger.LogWarning("Не удалось ответить чату {ChatId}: {Message}", chatId, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Сбой при обработке команды {Command} от чата {ChatId}.", command, chatId);
        }
    }

    private async Task StartAsync(long chatId, string? payload, string? firstName, CancellationToken ct)
    {
        // Ссылка вида t.me/бот?start=КОД приходит именно так: код лежит
        // в параметре запуска, а не в тексте команды.
        if (!string.IsNullOrWhiteSpace(payload))
        {
            await LinkAsync(chatId, payload, ct);
            return;
        }

        var linked = await telegram.IsLinkedAsync(chatId, ct);

        await SendAsync(
            chatId,
            BotMessages.Welcome(firstName, linked),
            // Кнопка мини-аппа показывается только привязанному: иначе приложение
            // откроется и не поймёт, чей прогресс показывать.
            withButton: linked,
            ct);
    }

    private async Task LinkAsync(long chatId, string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            await SendAsync(
                chatId,
                "Пришлите код так: /link ВАШ_КОД\n\nКод показывается в профиле на сайте после нажатия «Привязать Telegram».",
                withButton: false,
                ct);
            return;
        }

        var result = await telegram.LinkAsync(code, chatId, ct);

        await SendAsync(chatId, BotMessages.Linked(result.DisplayName, result.Username), withButton: true, ct);
    }

    private async Task WordAsync(long chatId, CancellationToken ct)
    {
        var word = await reminders.GetWordOfTheDayForTelegramAsync(chatId, ct);

        await SendAsync(
            chatId,
            word is null ? "Словарь пока пуст — загляните позже." : BotMessages.WordOfTheDay(word),
            withButton: true,
            ct);
    }

    private async Task TodayAsync(long chatId, CancellationToken ct)
    {
        var digest = await reminders.GetDigestForTelegramAsync(chatId, ct);

        await SendAsync(chatId, BotMessages.Digest(digest), withButton: true, ct);
    }

    private async Task ToggleRemindersAsync(long chatId, bool enabled, CancellationToken ct)
    {
        await telegram.SetRemindersAsync(chatId, enabled, ct);

        var time = await telegram.GetReminderTimeAsync(chatId, ct);

        await SendAsync(
            chatId,
            enabled ? BotMessages.RemindersOn(time) : BotMessages.RemindersOff(),
            withButton: true,
            ct);
    }

    /// <summary>
    /// Отправляет сообщение с кнопкой мини-аппа, если её есть куда вести.
    /// </summary>
    /// <remarks>
    /// Кнопка WebApp работает только по HTTPS, и это ограничение Telegram.
    /// При запуске без туннеля сообщение уходит без кнопки — текст остаётся
    /// полезным, а не превращается в ошибку отправки.
    /// </remarks>
    private Task SendAsync(long chatId, string text, bool withButton, CancellationToken ct)
    {
        var button = withButton ? BuildButton() : null;

        return api.SendMessageAsync(chatId, text, button, ct);
    }

    private TelegramInlineButton? BuildButton()
    {
        var url = options.Value.SecureWebAppUrl;

        return url is null ? null : new TelegramInlineButton("Открыть тренажёр", WebApp: new TelegramWebAppInfo(url));
    }

    /// <summary>
    /// Разбирает текст команды на имя и аргумент.
    /// </summary>
    /// <remarks>
    /// В группах Telegram дописывает к команде имя бота: <c>/word@MyBot</c>.
    /// Без отсечения суффикса команда перестаёт опознаваться ровно там, где
    /// бота добавили в чат.
    /// </remarks>
    private static (string Command, string? Argument) Split(string text)
    {
        var parts = text.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0];

        var at = command.IndexOf('@', StringComparison.Ordinal);

        if (at > 0)
        {
            command = command[..at];
        }

        return (command.ToLowerInvariant(), parts.Length > 1 ? parts[1].Trim() : null);
    }
}
