using System.Net.Http.Json;
using System.Text.Json;
using EnglishTrainer.Domain.Abstractions;
using EnglishTrainer.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnglishTrainer.Infrastructure.Security;

/// <summary>
/// Отправка письма через HTTP API Brevo.
/// </summary>
/// <remarks>
/// <para>
/// Нужна там, где SMTP недоступен: многие хостинги закрывают все исходящие
/// почтовые порты (25, 465 и 587) как антиспам-меру, и обойти это изнутри
/// нельзя. HTTPS при этом открыт всегда, поэтому письмо уходит обычным POST-ом
/// на <c>api.brevo.com</c>.
/// </para>
/// <para>
/// Клиент живёт столько же, сколько приложение: создавать <see cref="HttpClient"/>
/// на каждое письмо — известный способ исчерпать сокеты, а письмо здесь одно.
/// Таймаут берётся из общих настроек почты, чтобы поведение при недоступном
/// сервисе совпадало с SMTP.
/// </para>
/// <para>
/// Отказ сервиса обрабатывается так же, как отказ SMTP: причина — в журнал,
/// письмо целиком — в журнал (ссылка не должна пропасть), наружу — понятная
/// ошибка. Форма «забыли пароль» при этом не обещает отправленное письмо.
/// </para>
/// </remarks>
public sealed class BrevoEmailSender : IEmailSender, IDisposable
{
    /// <summary>Адрес отправки писем в API Brevo.</summary>
    private const string Endpoint = "https://api.brevo.com/v3/smtp/email";

    /// <summary>Сколько символов ответа сервиса попадает в журнал.</summary>
    private const int MaxLoggedResponse = 500;

    private readonly EmailOptions _options;
    private readonly ILogger<BrevoEmailSender> _logger;
    private readonly HttpClient _client;

    public BrevoEmailSender(IOptions<EmailOptions> options, ILogger<BrevoEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 5, 120))
        };
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
        {
            // Сюда попасть можно только при ошибочной регистрации в DI: без
            // настроек выбирается журнальная реализация.
            throw new InvalidOperationException(
                "Почта не настроена: задайте Email:Brevo:ApiKey и Email:From.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);

        // Ключ передаётся заголовком, а не в адресе: адрес с параметрами оседает
        // в журналах прокси и в истории промежуточных узлов.
        request.Headers.Add("api-key", _options.Brevo.ApiKey);
        request.Headers.Add("accept", "application/json");

        request.Content = JsonContent.Create(new
        {
            sender = new { name = _options.FromName, email = _options.From },
            to = new[] { new { email = message.To } },
            subject = message.Subject,
            textContent = message.Body
        });

        try
        {
            using var response = await _client.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var body = await response.Content.ReadAsStringAsync(ct);

            throw new HttpRequestException(
                $"Brevo ответил {(int)response.StatusCode}: {Shorten(body)}");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogError(
                exception,
                "Не удалось отправить письмо на {To} через Brevo ({From}).",
                message.To,
                _options.From);

            // Ссылка не должна пропасть вместе с неудачной отправкой: то же письмо
            // целиком уходит в журнал — так же, как в режиме без почты.
            _logger.LogInformation(
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

    /// <summary>Ответ сервиса для журнала: одной строкой и не целиком.</summary>
    private static string Shorten(string body)
    {
        var single = body.Replace('\n', ' ').Replace('\r', ' ').Trim();

        return single.Length <= MaxLoggedResponse
            ? single
            : single[..MaxLoggedResponse] + "…";
    }

    public void Dispose() => _client.Dispose();
}
