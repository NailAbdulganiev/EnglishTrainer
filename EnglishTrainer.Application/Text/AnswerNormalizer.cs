using System.Globalization;
using System.Text;

namespace EnglishTrainer.Application.Text;

/// <summary>
/// Нормализация ответов пользователя перед сравнением.
/// </summary>
/// <remarks>
/// Пользователь не должен страдать из-за регистра, лишних пробелов или
/// диакритики — иначе он будет получать «неверно» на правильный ответ
/// и потеряет доверие к системе.
/// </remarks>
public static class AnswerNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            // Убираем диакритические знаки: «café» и «cafe» — один ответ.
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(ch);
        }

        // Схлопываем повторяющиеся пробелы: «ice  cream» == «ice cream».
        return string.Join(' ', builder.ToString()
            .Normalize(NormalizationForm.FormC)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static bool Matches(string? userAnswer, IEnumerable<string> accepted) =>
        accepted.Any(option => Normalize(option) == Normalize(userAnswer));
}
