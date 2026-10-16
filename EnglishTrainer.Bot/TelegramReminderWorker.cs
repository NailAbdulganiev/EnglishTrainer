using EnglishTrainer.Application.Services;
using EnglishTrainer.Application.Telegram;
using EnglishTrainer.Bot.Telegram;
using Microsoft.Extensions.Options;

namespace EnglishTrainer.Bot;

/// <summary>
/// Ежедневные напоминания.
/// </summary>
/// <remarks>
/// <para>
/// Планировщик просыпается раз в минуту и спрашивает у базы, кому пора
/// напомнить. Проверка идёт по индексу на момент следующего напоминания,
/// поэтому её стоимость не зависит от числа пользователей, — в отличие
/// от перебора всех, у кого локальное время совпало с настройкой.
/// </para>
/// <para>
/// Отдельный процесс, а не фоновая задача внутри API: напоминания должны
/// приходить независимо от того, открыт ли у кого-нибудь сайт.
/// </para>
/// </remarks>
internal sealed class TelegramReminderWorker(
    ITelegramApi api,
    IServiceScopeFactory scopes,
    IOptions<TelegramOptions> options,
    ILogger<TelegramReminderWorker> logger) : BackgroundService
{
    /// <summary>
    /// Сколько напоминаний обрабатывать за один тик.
    /// </summary>
    /// <remarks>
    /// Ограничение защищает от залпа при первом запуске: если накопилась
    /// тысяча просроченных напоминаний, отправлять их одним залпом нельзя —
    /// Telegram ограничивает частоту запросов, и часть сообщений потеряется.
    /// Остальные уйдут в следующие тики.
    /// </remarks>
    private const int MaxPerTick = 25;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (!settings.HasToken || !settings.BotEnabled)
        {
            return;
        }

        // Нижняя граница в 15 секунд: чаще проверять бессмысленно, напоминание
        // привязано к минуте, а лишние запросы к базе ничего не ускоряют.
        var period = TimeSpan.FromSeconds(Math.Max(15, settings.ReminderTickSeconds));

        logger.LogInformation("Планировщик напоминаний запущен, период {Seconds} с.", period.TotalSeconds);

        using var timer = new PeriodicTimer(period);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }

                await SendDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Планировщик не должен умирать из-за одного сбоя: недоступная
                // база или сеть — временные состояния, и следующий тик их переживёт.
                logger.LogError(ex, "Сбой при отправке напоминаний.");
            }
        }

        logger.LogInformation("Планировщик напоминаний остановлен.");
    }

    private async Task SendDueAsync(CancellationToken ct)
    {
        List<Application.Dtos.TelegramReminderDto> due;

        using (var scope = scopes.CreateScope())
        {
            var reminders = scope.ServiceProvider.GetRequiredService<TelegramReminderService>();

            due = [.. await reminders.TakeDueAsync(MaxPerTick, ct)];
        }

        if (due.Count == 0)
        {
            return;
        }

        logger.LogInformation("Отправка напоминаний: {Count}.", due.Count);

        var button = BuildButton();

        foreach (var reminder in due)
        {
            try
            {
                await api.SendMessageAsync(reminder.TelegramId, BotMessages.Reminder(reminder.Digest), button, ct);
            }
            catch (TelegramApiException ex)
            {
                // Чаще всего это «bot was blocked by the user»: человек просто
                // не хочет получать сообщения. Это не сбой сервиса, и повторять
                // нечего — расписание уже сдвинуто на завтра.
                logger.LogWarning(
                    "Напоминание для чата {ChatId} не доставлено: {Message}", reminder.TelegramId, ex.Message);
            }
        }
    }

    /// <summary>
    /// Кнопка мини-аппа. Без HTTPS-адреса её нет — сообщение уходит как есть.
    /// </summary>
    private TelegramInlineButton? BuildButton()
    {
        var url = options.Value.SecureWebAppUrl;

        return url is null ? null : new TelegramInlineButton("Открыть тренажёр", WebApp: new TelegramWebAppInfo(url));
    }
}
