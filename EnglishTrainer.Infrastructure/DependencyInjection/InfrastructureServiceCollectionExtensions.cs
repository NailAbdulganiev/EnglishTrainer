using System.Text;
using EnglishTrainer.Application.Telegram;
using EnglishTrainer.Domain.Abstractions;
using EnglishTrainer.Domain.Repositories;
using EnglishTrainer.Infrastructure.Persistence;
using EnglishTrainer.Infrastructure.Repositories;
using EnglishTrainer.Infrastructure.Security;
using EnglishTrainer.Infrastructure.Seed;
using EnglishTrainer.Infrastructure.Storage;
using EnglishTrainer.Infrastructure.Telegram;
using EnglishTrainer.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EnglishTrainer.Infrastructure;

/// <summary>
/// Регистрация инфраструктурного слоя: база, репозитории, время, безопасность.
/// </summary>
/// <remarks>
/// Здесь и только здесь приложение узнаёт, что хранилище — это PostgreSQL,
/// а пароли хешируются PBKDF2. Слои Domain и Application о выборе не знают,
/// поэтому замена хранилища или способа хеширования не выходит за пределы этого файла.
/// </remarks>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Имя строки подключения в конфигурации.</summary>
    public const string ConnectionStringName = "Postgres";

    /// <summary>Настройка с каталогом аватаров.</summary>
    public const string AvatarRootKey = "Storage:AvatarRoot";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath)
    {
        AddPersistence(services, configuration);
        AddSecurity(services, configuration);
        AddStorage(services, configuration, contentRootPath);
        AddTelegram(services, configuration);
        AddEmail(services, configuration);

        // Часы — синглтон: состояние им не хранится, а создавать объект на каждый
        // запрос незачем. Репозитории и контекст — scoped, по одному на HTTP-запрос.
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IWordRepository, WordRepository>();
        services.AddScoped<IDeckRepository, DeckRepository>();
        services.AddScoped<ICardStateRepository, CardStateRepository>();
        services.AddScoped<IReviewLogRepository, ReviewLogRepository>();
        services.AddScoped<IStudySessionRepository, StudySessionRepository>();
        services.AddScoped<IDailyStatRepository, DailyStatRepository>();
        services.AddScoped<IFavoriteWordRepository, FavoriteWordRepository>();
        services.AddScoped<IFriendshipRepository, FriendshipRepository>();
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
        services.AddScoped<ITelegramLinkCodeRepository, TelegramLinkCodeRepository>();
        services.AddScoped<IUserAchievementRepository, AchievementRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<VocabularySeeder>();
        services.AddScoped<DatabaseInitializer>();

        return services;
    }

    /// <summary>
    /// Аватары. Каталог по умолчанию лежит внутри <c>wwwroot</c>, поэтому
    /// отдельный маршрут отдачи не нужен — файлы раздаёт статика интерфейса.
    /// </summary>
    private static void AddStorage(
        IServiceCollection services, IConfiguration configuration, string contentRootPath)
    {
        var configured = configuration[AvatarRootKey];

        // Относительный путь считается от каталога приложения, а не от текущего
        // каталога процесса: иначе запуск из другого места (двойной клик по exe,
        // служба Windows) уводил бы аватары в непредсказуемую папку.
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(contentRootPath, "wwwroot", "avatars")
            : Path.GetFullPath(configured, contentRootPath);

        services.AddSingleton<IAvatarStorage>(_ => new FileSystemAvatarStorage(root));
    }

    /// <summary>
    /// Telegram. Настройки читаются здесь, а не в точке входа, потому что их
    /// используют оба процесса — и API, и бот: разошедшись, они дадут «мини-апп
    /// открывается, а вход в нём не работает».
    /// </summary>
    private static void AddTelegram(IServiceCollection services, IConfiguration configuration)
    {
        var telegram = configuration.GetSection(TelegramOptions.SectionName).Get<TelegramOptions>()
            ?? new TelegramOptions();

        services.AddSingleton(Options.Create(telegram));
        services.AddSingleton<ITelegramInitDataValidator, TelegramInitDataValidator>();
    }

    /// <summary>
    /// Почта.
    /// </summary>
    /// <remarks>
    /// Реализация выбирается по настройкам, а не по режиму запуска. Три исхода:
    /// SMTP (обычный случай), HTTP API Brevo (там, где хостинг закрывает
    /// исходящие почтовые порты) и журнал (когда не настроено ничего — тогда
    /// письмо со ссылкой видно в журнале, а форма честно об этом говорит).
    /// </remarks>
    private static void AddEmail(IServiceCollection services, IConfiguration configuration)
    {
        var email = configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>()
            ?? new EmailOptions();

        services.AddSingleton(Options.Create(email));

        if (!email.IsConfigured)
        {
            services.AddSingleton<IEmailSender, LogEmailSender>();
            return;
        }

        if (email.UsesBrevo)
        {
            services.AddSingleton<IEmailSender, BrevoEmailSender>();
            return;
        }

        services.AddSingleton<IEmailSender, SmtpEmailSender>();
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Не задана строка подключения '{ConnectionStringName}'. " +
                $"Укажите её в appsettings.json или в переменной окружения " +
                $"ConnectionStrings__{ConnectionStringName}.");
        }

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);

            // Сеть до базы может моргнуть. Повторить запрос дешевле, чем показать
            // пользователю ошибку 500 на действии, которое он уже совершил.
            npgsql.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(2),
                errorCodesToAdd: null);
        }));
    }

    private static void AddSecurity(IServiceCollection services, IConfiguration configuration)
    {
        // Настройки токенов читаются и проверяются прямо здесь, а не «когда понадобятся».
        // Приложение с пустым секретом подписи не должно подниматься вообще:
        // иначе оно успешно стартует и выдаёт токены, которые способен подделать любой.
        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        Validate(jwtOptions);

        services.AddSingleton(Options.Create(jwtOptions));
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();
        services.AddSingleton<JwtTokenValidator>();
    }

    private static void Validate(JwtOptions options)
    {
        var secretBytes = Encoding.UTF8.GetByteCount(options.Secret ?? string.Empty);

        if (string.IsNullOrWhiteSpace(options.Secret))
        {
            throw new InvalidOperationException(
                "Не задан секрет подписи токенов (Jwt:Secret). Локально его удобно хранить " +
                "в user-secrets, в развёртывании — в переменной окружения Jwt__Secret.");
        }

        if (secretBytes < JwtOptions.MinimumSecretBytes)
        {
            throw new InvalidOperationException(
                $"Секрет подписи токенов слишком короткий: {secretBytes} байт. " +
                $"Для HMAC-SHA256 нужно не меньше {JwtOptions.MinimumSecretBytes} байт.");
        }

        if (options.LifetimeMinutes <= 0)
            throw new InvalidOperationException("Время жизни токена (Jwt:LifetimeMinutes) должно быть положительным.");
    }
}
