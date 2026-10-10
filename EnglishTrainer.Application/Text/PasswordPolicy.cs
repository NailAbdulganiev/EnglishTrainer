using EnglishTrainer.Domain.Exceptions;

namespace EnglishTrainer.Application.Text;

/// <summary>
/// Требования к паролю.
/// </summary>
/// <remarks>
/// Правило вынесено из сервиса аутентификации, потому что проверять пароль
/// нужно в четырёх местах: регистрация, смена пароля, сброс по ссылке и сброс
/// из командной строки. Разошедшиеся требования к длине — классический способ
/// получить аккаунт, который нельзя ни создать, ни восстановить.
/// </remarks>
public static class PasswordPolicy
{
    /// <summary>Минимальная длина пароля.</summary>
    public const int MinimumLength = 8;

    /// <summary>Проверяет пароль и возвращает его же для дальнейшего использования.</summary>
    public static string Require(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinimumLength)
            throw new ValidationException($"Пароль должен быть не короче {MinimumLength} символов.");

        return password;
    }
}
