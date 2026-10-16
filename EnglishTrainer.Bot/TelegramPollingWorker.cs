using EnglishTrainer.Application.Telegram;
using EnglishTrainer.Bot.Telegram;
using Microsoft.Extensions.Options;

namespace EnglishTrainer.Bot;

/// <summary>
/// Опрос Telegram (long polling).
/// </summary>
/// <remarks>
/// <para>
/// Long polling выбран вместо вебхука осознанно. Вебхук требует, чтобы Telegram
/// сам достучался до сервера: публичный HTTPS-адрес, открытый порт и валидный
/// сертификат. Для приложения, которое запускается на своей машине, это лишний
/// барьер — а long polling работает сразу, потому что соединение открывает
/// сам бот.
/// </para>
/// <para>
/// Область видимости создаётся на каждое обновление: сервисы Application
/// работают с контекстом EF Core, а он не потокобезопасен и не рассчитан
/// на долгую жизнь.
/// </para>
/// </remarks>
internal sealed class TelegramPollingWorker(
    ITelegramApi api,
    IServiceScopeFactory scopes,
    IOptions<TelegramOptions> options,
    ILogger<TelegramPollingWorker> logger) : BackgroundService
{
    /// <summary>Пауза после сбоя, чтобы недоступный Telegram не превратился в поток запросов.</summary>
    private static readonly TimeSpan FailureDelay = TimeSpan.FromSeconds(5);

    /// <summary>Пауза при конфликте опроса: значит, запущен второй экземпляр бота.</summary>
    private static readonly TimeSpan ConflictDelay = TimeSpan.FromSeconds(30);

    /// <summary>Пауза перед повторной попыткой подключения, если сеть недоступна.</summary>
    private static readonly TimeSpan ConnectRetryDelay = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (!settings.HasToken)
        {
            // Не исключение, а предупреждение: сервис должен запускаться и без
            // бота — например, когда поднимают только веб-версию.
            logger.LogWarning(
                "Токен бота не задан (Telegram:BotToken) — опрос Telegram не запущен. " +
                "Раздел «Запуск бота» в README описывает, куда его положить.");
            return;
        }

        if (!settings.BotEnabled)
        {
            logger.LogInformation("Бот выключен настройкой Telegram:BotEnabled.");
            return;
        }

        TelegramBotInfo? me;

        try
        {
            me = await ConnectAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        if (me is null)
        {
            return;
        }

        logger.LogInformation("Бот @{Username} запущен.", me.Username);

        await PublishMenuAsync(stoppingToken);

        var offset = await SkipBacklogAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await api.GetUpdatesAsync(offset, settings.PollTimeoutSeconds, stoppingToken);

                foreach (var update in updates)
                {
                    // Смещение сдвигается до обработки: подтверждённое обновление
                    // Telegram не пришлёт повторно, даже если обработка упадёт.
                    // Повторное выполнение команды опаснее её потери — она могла
                    // уже изменить состояние.
                    offset = update.UpdateId + 1;

                    await HandleAsync(update, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (TelegramApiException ex) when (ex.StatusCode == 409)
            {
                logger.LogError(
                    "Telegram отвечает 409: этот бот уже опрашивается другим процессом. " +
                    "Остановите второй экземпляр — повтор через {Seconds} с.",
                    ConflictDelay.TotalSeconds);

                await Task.Delay(ConflictDelay, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Сбой опроса Telegram, повтор через {Seconds} с.", FailureDelay.TotalSeconds);

                await Task.Delay(FailureDelay, stoppingToken);
            }
        }

        logger.LogInformation("Опрос Telegram остановлен.");
    }

    private async Task HandleAsync(TelegramUpdate update, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();

        var handler = scope.ServiceProvider.GetRequiredService<TelegramBotService>();

        await handler.HandleAsync(update, ct);
    }

    /// <summary>
    /// Проверяет токен, повторяя попытку при недоступной сети.
    /// </summary>
    /// <remarks>
    /// Различие между «токен неверный» и «сети нет» здесь принципиальное.
    /// Неверный токен — это ошибка настройки: повторять бессмысленно, и человек
    /// должен увидеть причину сразу. Недоступная сеть — временное состояние:
    /// если сдаться, бот не поднимется после короткого обрыва связи, и виноватым
    /// окажется не он.
    /// </remarks>
    private async Task<TelegramBotInfo?> ConnectAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                return await api.GetMeAsync(ct);
            }
            catch (TelegramApiException ex)
            {
                logger.LogError(
                    "Telegram отклонил токен бота: {Message}. Проверьте Telegram:BotToken.", ex.Message);
                return null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    "Telegram недоступен ({Message}). Повтор через {Seconds} с.",
                    ex.Message, ConnectRetryDelay.TotalSeconds);

                await Task.Delay(ConnectRetryDelay, ct);
            }
        }

        return null;
    }

    /// <summary>
    /// Пропускает накопившиеся за время простоя обновления.
    /// </summary>
    /// <remarks>
    /// Иначе после перезапуска бот разом отвечает на всё, что написали за сутки,
    /// и заново обрабатывает старые команды. Для кодов привязки это особенно
    /// неприятно: они живут пятнадцать минут, и устаревший код либо не сработает,
    /// либо сработает не там, где человек ждёт.
    /// </remarks>
    private async Task<long?> SkipBacklogAsync(CancellationToken ct)
    {
        try
        {
            // offset = -1 просит Telegram вернуть только последнее обновление.
            var last = await api.GetUpdatesAsync(-1, timeoutSeconds: 0, ct);

            if (last.Count == 0)
            {
                return null;
            }

            var offset = last[^1].UpdateId + 1;

            logger.LogInformation("Накопившиеся обновления пропущены, опрос начнётся с #{Offset}.", offset);

            return offset;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось определить смещение обновлений — опрос начнётся с начала очереди.");
            return null;
        }
    }

    /// <summary>Публикует список команд и кнопку меню.</summary>
    /// <remarks>
    /// Сбой на этом шаге не останавливает бота: команды и кнопка — удобство,
    /// а не работа. Сообщение об ошибке важнее, чем отсутствие кнопки.
    /// </remarks>
    private async Task PublishMenuAsync(CancellationToken ct)
    {
        try
        {
            await api.SetMyCommandsAsync(
            [
                new TelegramBotCommand("word", "Слово дня"),
                new TelegramBotCommand("today", "Сводка за сегодня"),
                new TelegramBotCommand("stop", "Выключить напоминания"),
                new TelegramBotCommand("on", "Включить напоминания"),
                new TelegramBotCommand("help", "Список команд")
            ], ct);

            var url = options.Value.SecureWebAppUrl;

            if (url is null)
            {
                logger.LogWarning(
                    "Адрес мини-аппа не задан или задан без HTTPS (Telegram:WebAppUrl) — " +
                    "кнопка меню не установлена. Telegram открывает мини-апп только по HTTPS.");
                return;
            }

            await api.SetMenuButtonAsync("Тренажёр", url, ct);

            logger.LogInformation("Кнопка меню ведёт на {Url}.", url);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось опубликовать команды и кнопку меню.");
        }
    }
}
