namespace EnglishTrainer.Domain.Abstractions;

/// <summary>
/// Единый источник времени. Внедряется вместо прямых обращений к
/// <see cref="DateTime.UtcNow"/>, иначе время нельзя ни подменить в тестах,
/// ни проверить граничные случаи вроде перехода через полночь.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }

    /// <summary>
    /// Локальная дата пользователя. Критично для дневной статистики и серии дней:
    /// «сегодня» для пользователя в UTC+3 и на сервере в UTC — разные даты.
    /// </summary>
    DateOnly TodayFor(string timeZoneId);

    /// <summary>Локальное время пользователя — для проверки «пора напомнить».</summary>
    TimeOnly LocalTimeFor(string timeZoneId);
}

/// <summary>Хеширование паролей. Реализация — в Infrastructure.</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>Проверка пароля. Сравнение обязано быть устойчивым по времени.</summary>
    bool Verify(string password, string passwordHash);
}

/// <summary>Генератор токенов доступа (JWT или иная схема).</summary>
public interface ITokenIssuer
{
    AccessToken Issue(int userId, Guid publicId, string email);
}

/// <param name="Value">Сам токен.</param>
/// <param name="ExpiresAtUtc">Момент истечения.</param>
public readonly record struct AccessToken(string Value, DateTime ExpiresAtUtc);
