namespace EnglishTrainer.Infrastructure.Security;

/// <summary>
/// Параметры выдачи и проверки токенов доступа.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Минимальная длина секрета в байтах.</summary>
    /// <remarks>
    /// HMAC-SHA256 требует ключ не короче размера своего выхода — 32 байта.
    /// Секрет из шести символов формально «работает», но подбирается перебором,
    /// поэтому короткий секрет считается ошибкой конфигурации, а не мелочью.
    /// </remarks>
    public const int MinimumSecretBytes = 32;

    public string Issuer { get; set; } = "EnglishTrainer";

    public string Audience { get; set; } = "EnglishTrainer";

    /// <summary>
    /// Симметричный ключ подписи. Обязателен: в appsettings.json его нет,
    /// он приходит из user-secrets или переменной окружения.
    /// </summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>Срок жизни токена в минутах. Неделя — компромисс для учебного приложения.</summary>
    public int LifetimeMinutes { get; set; } = 7 * 24 * 60;

    /// <summary>
    /// Допустимое расхождение часов при проверке exp/nbf.
    /// Сервер и клиент почти никогда не совпадают по времени до секунды.
    /// </summary>
    public int ClockSkewSeconds { get; set; } = 60;
}
