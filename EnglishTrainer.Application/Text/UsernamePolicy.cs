using EnglishTrainer.Domain.Exceptions;

namespace EnglishTrainer.Application.Text;

/// <summary>
/// Правила ника: по нему человека находят другие пользователи.
/// </summary>
/// <remarks>
/// <para>
/// Ник хранится в нижнем регистре, поэтому «Ivan» и «ivan» — один и тот же
/// человек. Иначе уникальный индекс пропускал бы двойников, а пользователи
/// жаловались бы, что не могут найти друг друга.
/// </para>
/// <para>
/// Набор символов ограничен латиницей, цифрами, дефисом и подчёркиванием:
/// ник участвует в адресах API (<c>/api/friends/{username}</c>), и посторонние
/// символы там пришлось бы экранировать.
/// </para>
/// </remarks>
public static class UsernamePolicy
{
    public const int MinLength = 3;
    public const int MaxLength = 32;

    /// <summary>Приводит ник к каноническому виду без проверки.</summary>
    public static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Приводит ник к каноническому виду и проверяет его.</summary>
    public static string Require(string? value)
    {
        var normalized = Normalize(value);

        if (normalized.Length is < MinLength or > MaxLength)
            throw new ValidationException($"Ник должен быть от {MinLength} до {MaxLength} символов.");

        if (!normalized.All(IsAllowed))
            throw new ValidationException("В нике допустимы только латинские буквы, цифры, дефис и подчёркивание.");

        return normalized;
    }

    /// <summary>
    /// Подбирает ник из email: <c>johannesmetz98@gmail.com</c> → <c>johannesmetz98</c>.
    /// </summary>
    public static string SuggestFromEmail(string email)
    {
        var local = Normalize(email).Split('@')[0];
        var cleaned = new string(local.Where(IsAllowed).ToArray());

        if (cleaned.Length >= MinLength)
            return Truncate(cleaned, MaxLength);

        // Короткий или пустой email даёт непригодный ник, поэтому добавляем
        // узнаваемую основу: лучше «user42», чем отказ в регистрации.
        var fallback = $"user{Math.Abs(email.GetHashCode(StringComparison.Ordinal)) % 10000}";
        return Truncate(fallback.Length >= MinLength ? fallback : "user", MaxLength);
    }

    /// <summary>Добавляет к нику числовой хвост, не выходя за пределы длины.</summary>
    public static string WithSuffix(string baseName, int suffix)
    {
        var tail = suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Truncate(baseName, MaxLength - tail.Length) + tail;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static bool IsAllowed(char symbol) =>
        symbol is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_';
}
