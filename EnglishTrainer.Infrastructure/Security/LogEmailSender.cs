using EnglishTrainer.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace EnglishTrainer.Infrastructure.Security;

/// <summary>
/// Отправка письма в журнал приложения.
/// </summary>
/// <remarks>
/// <para>
/// Это не заглушка и не «временное решение». Приложение рассчитано на запуск
/// для себя: сервер стоит на той же машине, что и пользователь, а почтового
/// провайдера у него нет и, скорее всего, не будет. Писать письмо в лог —
/// честный режим работы: администратор и есть получатель, ссылку для сброса
/// пароля он видит прямо в окне, где запущен API.
/// </para>
/// <para>
/// Когда появится настоящая почта, достаточно добавить вторую реализацию
/// <see cref="IEmailSender"/> и зарегистрировать её вместо этой — порт объявлен
/// в Domain именно ради этого.
/// </para>
/// </remarks>
public sealed class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        // Тема и текст попадают в лог целиком: ссылка для сброса — это и есть
        // содержимое письма, и она должна быть видна в консоли.
        logger.LogInformation(
            """
            ─── Письмо (почтовый провайдер не настроен, письмо выводится в журнал) ───
            Кому:  {To}
            Тема:  {Subject}

            {Body}
            ──────────────────────────────────────────────────────────────────────────
            """,
            message.To,
            message.Subject,
            message.Body);

        return Task.CompletedTask;
    }
}
