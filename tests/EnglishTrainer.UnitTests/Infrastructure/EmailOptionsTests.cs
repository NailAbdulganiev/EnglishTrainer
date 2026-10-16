using EnglishTrainer.Infrastructure.Security;
using Microsoft.Extensions.Configuration;

namespace EnglishTrainer.UnitTests.Infrastructure;

/// <summary>
/// Настройки почты: разбор конфигурации и выбор режима доставки.
/// </summary>
/// <remarks>
/// Проверяется именно **связывание** настроек, а не отправка. Это не
/// формальность: имена ключей в конфигурации пишет человек в `appsettings`
/// и `.env`, и разойдясь с классом настроек, они не дают ни ошибки, ни
/// предупреждения — приложение просто продолжает писать письма в журнал,
/// а в развёртывании это выглядит как «почта настроена, а письма не приходят».
/// </remarks>
public sealed class EmailOptionsTests
{
    /// <summary>Разбирает конфигурацию так же, как это делает приложение.</summary>
    private static EmailOptions Read(params (string Key, string Value)[] pairs)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(pair =>
                new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

        return configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>()
            ?? new EmailOptions();
    }

    [Fact]
    public void Binding_ReadsEverythingFromTheSmtpSubsection()
    {
        var options = Read(
            ("Email:Smtp:Host", "smtp.mail.ru"),
            ("Email:Smtp:Port", "587"),
            ("Email:Smtp:User", "you@mail.ru"),
            ("Email:Smtp:Password", "пароль-приложения"),
            ("Email:Smtp:EnableSsl", "true"),
            ("Email:From", "you@mail.ru"),
            ("Email:FromName", "EnglishTrainer"),
            ("Email:PublicUrl", "https://trainer.example.com"));

        Assert.True(options.IsConfigured);
        Assert.Equal("email", options.Delivery);
        Assert.Equal("smtp.mail.ru", options.Smtp.Host);
        Assert.Equal(587, options.Smtp.Port);
        Assert.Equal("you@mail.ru", options.Smtp.User);
        Assert.Equal("пароль-приложения", options.Smtp.Password);
        Assert.True(options.Smtp.EnableSsl);
        Assert.Equal("you@mail.ru", options.From);
        Assert.Equal("EnglishTrainer", options.FromName);
        Assert.Equal("https://trainer.example.com", options.PublicUrl);
    }

    [Fact]
    public void WithoutSettings_DeliveryGoesToTheLog()
    {
        var options = Read();

        Assert.False(options.IsConfigured);
        Assert.Equal("log", options.Delivery);

        // Порт по умолчанию — 587: на 465 шифрование начинается с первого байта,
        // и SmtpClient его не умеет, поэтому «по умолчанию» обязан быть рабочий порт.
        Assert.Equal(587, options.Smtp.Port);
    }

    [Fact]
    public void LoginWithoutPassword_CountsAsNotConfigured()
    {
        // Логин без пароля — незаполненная настройка, а не сервер без
        // аутентификации: такой вход сервер отклонит, письмо не уйдёт,
        // а человек будет ждать ссылку. Честнее писать письмо в журнал.
        var options = Read(
            ("Email:Smtp:Host", "smtp.mail.ru"),
            ("Email:Smtp:User", "you@mail.ru"),
            ("Email:From", "you@mail.ru"));

        Assert.False(options.IsConfigured);
        Assert.Equal("log", options.Delivery);
    }

    [Fact]
    public void WithoutSenderAddress_CountsAsNotConfigured()
    {
        var options = Read(
            ("Email:Smtp:Host", "smtp.mail.ru"),
            ("Email:Smtp:User", "you@mail.ru"),
            ("Email:Smtp:Password", "пароль-приложения"));

        Assert.False(options.IsConfigured);
        Assert.Equal("log", options.Delivery);
    }

    [Fact]
    public void ServerWithoutAuthentication_IsConfigured()
    {
        // Локальный релей без аутентификации — законный случай: логина нет,
        // значит и пароля не требуется.
        var options = Read(
            ("Email:Smtp:Host", "localhost"),
            ("Email:Smtp:EnableSsl", "false"),
            ("Email:From", "trainer@localhost"));

        Assert.True(options.IsConfigured);
        Assert.Equal("email", options.Delivery);
        Assert.False(options.Smtp.EnableSsl);
    }

    [Fact]
    public void ProviderBrevo_ReadsTheApiKeyAndIgnoresTheSmtpServer()
    {
        // Отправка через HTTP API — единственный путь там, где хостинг закрывает
        // исходящие почтовые порты. Настроенным считается ключ, а не SMTP-сервер:
        // заполненный Host без ключа письмо не отправит.
        var options = Read(
            ("Email:Provider", "brevo"),
            ("Email:Brevo:ApiKey", "xkeysib-пример"),
            ("Email:From", "you@mail.ru"),
            ("Email:PublicUrl", "https://trainer.example.com"));

        Assert.True(options.UsesBrevo);
        Assert.True(options.IsConfigured);
        Assert.Equal("email", options.Delivery);
        Assert.Equal("xkeysib-пример", options.Brevo.ApiKey);

        // SMTP-сервер при этом пуст, и это не ошибка настройки.
        Assert.Equal(string.Empty, options.Smtp.Host);
    }

    [Fact]
    public void ProviderBrevo_WithoutApiKey_CountsAsNotConfigured()
    {
        var options = Read(
            ("Email:Provider", "brevo"),
            ("Email:From", "you@mail.ru"),
            ("Email:Smtp:Host", "smtp.mail.ru"),
            ("Email:Smtp:User", "you@mail.ru"),
            ("Email:Smtp:Password", "пароль-приложения"));

        // Выбран HTTP-провайдер, но ключа нет: SMTP-настройки здесь ни при чём,
        // и письмо уйдёт в журнал — так же, как при незаполненной почте вообще.
        Assert.True(options.UsesBrevo);
        Assert.False(options.IsConfigured);
        Assert.Equal("log", options.Delivery);
    }

    [Fact]
    public void ProviderSmtp_IsTheDefault()
    {
        // Старые настройки без Provider продолжают работать по SMTP: иначе
        // обновление приложения молча отключило бы уже настроенную почту.
        var options = Read(
            ("Email:Smtp:Host", "smtp.mail.ru"),
            ("Email:Smtp:User", "you@mail.ru"),
            ("Email:Smtp:Password", "пароль-приложения"),
            ("Email:From", "you@mail.ru"));

        Assert.False(options.UsesBrevo);
        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void TimeoutSeconds_IsSharedByBothProviders()
    {
        var options = Read(("Email:TimeoutSeconds", "45"));

        Assert.Equal(45, options.TimeoutSeconds);
    }
}
