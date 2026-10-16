namespace EnglishTrainer.Domain.Exceptions;

/// <summary>
/// Ожидаемая ошибка приложения: её текст можно показывать пользователю.
/// Всё, что не унаследовано от этого типа, считается внутренней ошибкой
/// и наружу отдаётся без подробностей.
/// </summary>
public abstract class AppException(string message) : Exception(message);

/// <summary>Запрошенная сущность не найдена.</summary>
public sealed class NotFoundException(string message) : AppException(message)
{
    public static NotFoundException For(string entity, object key) =>
        new($"{entity} с идентификатором '{key}' не найден.");
}

/// <summary>Нарушено правило предметной области или бизнес-ограничение.</summary>
public sealed class ConflictException(string message) : AppException(message);

/// <summary>Некорректные входные данные, которые не ловятся валидацией модели.</summary>
public sealed class ValidationException(string message) : AppException(message);

/// <summary>Ошибка аутентификации или авторизации.</summary>
public sealed class UnauthorizedException(string message = "Требуется аутентификация.") : AppException(message);

/// <summary>
/// Действие запрещено, хотя человек опознан.
/// </summary>
/// <remarks>
/// Отличается от <see cref="UnauthorizedException"/> намеренно. 401 означает
/// «представься заново», и клиент на него стирает токен и показывает форму
/// входа. Но «Telegram не привязан к аккаунту» — это не истёкший сеанс:
/// стирать токен и разлогинивать человека в этом случае нельзя, иначе вход
/// в мини-апп превращался бы в выход из веб-версии.
/// </remarks>
public sealed class ForbiddenException(string message) : AppException(message);

/// <summary>
/// Внешний сервис не выполнил действие: письмо не ушло, Telegram не ответил.
/// </summary>
/// <remarks>
/// Отдельный тип, а не внутренняя ошибка: сервер работает, отказал тот, к кому
/// он обращался, и человеку об этом нужно сказать. Молчание здесь дороже всего —
/// форма «забыли пароль» рапортовала бы об отправленном письме, которого нет,
/// и человек ждал бы ссылку, которую никто не отправил.
/// </remarks>
public sealed class DeliveryException(string message) : AppException(message);
