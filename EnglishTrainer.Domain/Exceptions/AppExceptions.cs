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
