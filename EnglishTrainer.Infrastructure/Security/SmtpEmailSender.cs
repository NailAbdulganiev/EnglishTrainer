using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Security.Authentication;
using EnglishTrainer.Domain.Abstractions;
using EnglishTrainer.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnglishTrainer.Infrastructure.Security;

/// <summary>
/// Отправка письма по SMTP.
/// </summary>
/// <remarks>
/// <para>
/// Регистрируется только тогда, когда почта настроена: без сервера и адреса
/// отправителя письмо отправить некуда, и вместо ошибки на экране «забыли
/// пароль» работает журнальный режим (<see cref="LogEmailSender"/>).
/// </para>
/// <para>
/// Клиент создаётся на каждое письмо, а не держится полем. Писем в этом
/// приложении единицы в день, а долгоживущий <see cref="SmtpClient"/> требует
/// ручной синхронизации: одновременная отправка двух писем через один экземпляр
/// заканчивается исключением «уже занят».
/// </para>
/// <para>
/// Текст письма приходит уже готовым: тема, тело и ссылка собираются в слое
/// Application, потому что письмо — часть сценария восстановления доступа,
/// а не деталь транспорта.
/// </para>
/// </remarks>
public sealed class SmtpEmailSender(
    IOptions<EmailOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var settings = options.Value;
        var smtp = settings.Smtp;

        if (!settings.IsConfigured)
        {
            // Сюда попасть можно только при ошибочной регистрации в DI: без
            // настроек выбирается журнальная реализация. Молча «отправить»
            // письмо в никуда нельзя — человек будет ждать ссылку, которой нет.
            throw new InvalidOperationException(
                "Почта не настроена: задайте Email:Smtp:Host, Email:From и пароль приложения.");
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(settings.From, settings.FromName),
            Subject = message.Subject,
            Body = message.Body,
            IsBodyHtml = false
        };

        mail.To.Add(message.To);

        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = smtp.EnableSsl,

            // Таймаут задаётся явно: умолчание в сто секунд превращает заблокированный
            // исходящий порт в двухминутное ожидание на форме «забыли пароль».
            Timeout = Math.Clamp(smtp.TimeoutSeconds, 5, 120) * 1000
        };

        if (!string.IsNullOrWhiteSpace(smtp.User))
        {
            client.Credentials = new NetworkCredential(smtp.User, smtp.Password);
        }

        try
        {
            await client.SendMailAsync(mail, ct);
        }
        catch (Exception exception) when (IsDeliveryFailure(exception))
        {
            logger.LogError(
                exception,
                "Не удалось отправить письмо на {To} через {Host}:{Port}.",
                message.To,
                smtp.Host,
                smtp.Port);

            // Ссылка не должна пропасть вместе с неудачной отправкой: то же
            // письмо целиком уходит в журнал — так же, как в режиме без
            // почтового сервера. Доступ человек себе вернёт даже при сломанной
            // почте, а причина отказа останется в логе.
            logger.LogInformation(
                """
                ─── Письмо (отправка не удалась, письмо выводится в журнал) ───
                Кому:  {To}
                Тема:  {Subject}

                {Body}
                ───────────────────────────────────────────────────────────────
                """,
                message.To,
                message.Subject,
                message.Body);

            throw new DeliveryException(
                "Письмо со ссылкой отправить не удалось. Проверьте настройки почты " +
                "(Email) — ссылка записана в журнал сервера.");
        }
    }

    /// <summary>
    /// Отказ доставки, а не ошибка в самом письме.
    /// </summary>
    /// <remarks>
    /// Ловится всё, что приходит от сети и почтового сервера: недоступный хост,
    /// обрыв, отказ аутентификации, отказ принять получателя. Узкий фильтр
    /// («только SmtpException») оставлял бы часть отказов необработанными:
    /// при недоступном сервере прилетает SocketException, при таймауте —
    /// TimeoutException, и оба превращались бы в 500 без объяснения причины.
    /// </remarks>
    private static bool IsDeliveryFailure(Exception exception) =>
        exception is SmtpException or InvalidOperationException or SocketException
            or IOException or TimeoutException or AuthenticationException;
}
