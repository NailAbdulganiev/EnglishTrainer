namespace EnglishTrainer.Infrastructure.Security;

/// <summary>
/// Настройки отправки писем.
/// </summary>
/// <remarks>
/// <para>
/// Почта нужна ровно для одного письма — ссылки на сброс пароля. Провайдер
/// задаётся целиком в конфигурации, потому что почтовых сервисов много, а
/// приложение не должно знать ни об одном из них: SMTP есть у всех.
/// </para>
/// <para>
/// Пустой <see cref="SmtpOptions.Host"/> означает «почта не настроена» — это
/// рабочий режим, а не ошибка: письмо уходит в журнал (<c>LogEmailSender</c>),
/// и ссылку администратор видит в окне запуска. Форма «забыли пароль» спрашивает
/// способ доставки у сервера и не отправляет человека проверять почту зря.
/// </para>
/// <para>
/// Настройки сервера лежат во вложенном разделе <c>Email:Smtp</c>, а не в корне
/// <c>Email</c>: так пара «логин и пароль сервера» не смешивается с адресом
/// отправителя, и в переменных окружения это читается как <c>Email__Smtp__Host</c>.
/// Вложенность здесь не украшение: плоские свойства класса и вложенный JSON
/// молча не совпали бы — настройка выглядела бы заданной, а письма не уходили.
/// </para>
/// </remarks>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// Способ отправки: <c>smtp</c> или <c>brevo</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Два способа, а не один, потому что не всякий хостинг пускает почту:
    /// на многих VPS закрыты все исходящие SMTP-порты (25, 465 и 587) — это
    /// антиспам-мера провайдера, и обойти её изнутри нельзя. HTTPS при этом
    /// открыт всегда, поэтому сервис с HTTP API остаётся единственным путём.
    /// </para>
    /// <para>
    /// Выбор задаётся явно, а не угадывается по заполненным полям: настройка,
    /// которая «сама догадалась», при заполненных сразу двух наборах ведёт себя
    /// непредсказуемо, а причина отказа ищется в логах почтового сервиса.
    /// </para>
    /// </remarks>
    public string Provider { get; set; } = "smtp";

    /// <summary>Сервер отправки по SMTP.</summary>
    public SmtpOptions Smtp { get; set; } = new();

    /// <summary>Отправка через HTTP API Brevo.</summary>
    public BrevoOptions Brevo { get; set; } = new();

    /// <summary>Адрес отправителя. Обязателен, если почта настроена.</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>Имя отправителя — то, что видит человек в списке писем.</summary>
    public string FromName { get; set; } = "EnglishTrainer";

    /// <summary>
    /// Публичный адрес приложения, из которого собираются ссылки в письмах.
    /// </summary>
    /// <remarks>
    /// Нужен за обратным прокси: внутренний адрес контейнера человеку бесполезен,
    /// а за прокси определить внешний адрес по запросу можно не всегда. Пусто —
    /// адрес берётся из самого запроса.
    /// </remarks>
    public string PublicUrl { get; set; } = string.Empty;

    /// <summary>
    /// Сколько секунд ждать почтовый сервис.
    /// </summary>
    /// <remarks>
    /// Умолчание <c>SmtpClient</c> — сто секунд, и это не теоретическая
    /// величина: когда хостинг блокирует исходящий порт, соединение не
    /// отвергается, а повисает до таймаута, и форма «забыли пароль» висит вместе
    /// с ним. Человек не понимает, идёт отправка или всё сломалось. Двадцати
    /// секунд хватает любому живому сервису, а отказ приходит быстро и с текстом.
    /// </remarks>
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>Отправка идёт через HTTP API, а не по SMTP.</summary>
    public bool UsesBrevo => string.Equals(Provider, "brevo", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Настроена ли отправка.
    /// </summary>
    /// <remarks>
    /// Половина настройки хуже её отсутствия: без сервера, ключа или адреса
    /// отправителя письмо отправить некуда, и лучше честно писать его в журнал —
    /// тогда форма «забыли пароль» скажет человеку правду, а не «проверьте почту».
    /// </remarks>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(From)
        && (UsesBrevo ? Brevo.IsConfigured : Smtp.IsConfigured);

    /// <summary>Способ доставки письма для интерфейса.</summary>
    public string Delivery => IsConfigured ? "email" : "log";
}

/// <summary>Параметры отправки через HTTP API Brevo.</summary>
public sealed class BrevoOptions
{
    /// <summary>
    /// Ключ API Brevo (начинается с <c>xkeysib-</c>).
    /// </summary>
    /// <remarks>
    /// Секрет: в репозитории его нет, он приходит переменной окружения
    /// <c>Email__Brevo__ApiKey</c> или из локального файла секретов.
    /// </remarks>
    public string ApiKey { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>Параметры SMTP-сервера.</summary>
public sealed class SmtpOptions
{
    /// <summary>SMTP-сервер. Пусто — письма идут в журнал.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Порт SMTP.
    /// </summary>
    /// <remarks>
    /// 587 — «отправка с шифрованием через STARTTLS», её понимают почти все
    /// провайдеры, включая Mail.ru. 465 — шифрование с первого байта, и его
    /// <c>System.Net.Mail.SmtpClient</c> не умеет: соединение просто не состоится.
    /// </remarks>
    public int Port { get; set; } = 587;

    /// <summary>Имя пользователя SMTP. Пусто — сервер без аутентификации.</summary>
    public string User { get; set; } = string.Empty;

    /// <summary>
    /// Пароль SMTP.
    /// </summary>
    /// <remarks>
    /// У большинства провайдеров это не пароль от аккаунта, а отдельный
    /// «пароль для внешнего приложения»: обычный они отклоняют или он просто
    /// не существует при двухфакторной аутентификации.
    /// </remarks>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Шифровать соединение.
    /// </summary>
    /// <remarks>
    /// Выключение осмысленно только для локального почтового сервера: пароль
    /// и ссылка для сброса пароля по открытому каналу — подарок тому, кто
    /// читает трафик.
    /// </remarks>
    public bool EnableSsl { get; set; } = true;

    /// <summary>
    /// Настроена ли отправка по SMTP.
    /// </summary>
    /// <remarks>
    /// Логин без пароля — это незаполненная настройка, а не «сервер без
    /// аутентификации»: сервер такой вход отклонит, письмо не уйдёт, а человек
    /// будет ждать ссылку. Поэтому такой набор считается ненастроенным.
    /// </remarks>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host)
        && (string.IsNullOrWhiteSpace(User) || !string.IsNullOrWhiteSpace(Password));
}
