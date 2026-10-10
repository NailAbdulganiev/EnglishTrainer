using System.Security.Cryptography;
using EnglishTrainer.Domain.Abstractions;

namespace EnglishTrainer.Infrastructure.Security;

/// <summary>
/// Хеширование паролей средствами PBKDF2-HMAC-SHA256.
/// </summary>
/// <remarks>
/// <para>
/// Пароли не шифруются, а хешируются с солью и большим числом итераций: даже при утечке
/// таблицы пользователей восстановление пароля остаётся дорогим. Соль хранится рядом
/// с хешем — она не секретна, её задача в том, чтобы одинаковые пароли разных людей
/// давали разные хеши, и радужные таблицы перестали работать.
/// </para>
/// <para>
/// Формат хранения: <c>pbkdf2-sha256$итерации$соль$хеш</c>. Число итераций записано
/// в саму строку намеренно: когда его понадобится увеличить, старые хеши продолжат
/// проверяться с прежним значением, и перелогин всех пользователей не потребуется.
/// </para>
/// </remarks>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string Algorithm = "pbkdf2-sha256";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>
    /// Число итераций. Взято с запасом, но так, чтобы вход в аккаунт не занимал
    /// заметное время: ориентир OWASP — сотни тысяч итераций для PBKDF2-SHA256.
    /// </summary>
    private const int Iterations = 210_000;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);

        return string.Join('$', Algorithm, Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public bool Verify(string password, string passwordHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(passwordHash))
            return false;

        var parts = passwordHash.Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], Algorithm, StringComparison.Ordinal))
            return false;

        if (!int.TryParse(parts[1], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var iterations) || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            // Повреждённая строка хеша — это не «пароль подошёл», а отказ.
            return false;
        }

        // Длина результата берётся из сохранённого хеша: если алгоритм когда-нибудь
        // удлинит выход, старые записи продолжат проверяться корректно.
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
