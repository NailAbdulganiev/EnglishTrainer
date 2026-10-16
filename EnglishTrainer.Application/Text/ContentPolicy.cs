using EnglishTrainer.Domain.Exceptions;

namespace EnglishTrainer.Application.Text;

/// <summary>
/// Правила личных колод.
/// </summary>
/// <remarks>
/// Проверки собраны в одном месте и не зависят ни от базы, ни от HTTP:
/// те же правила нужны и API, и тестам, а размазанные по обработчикам они
/// разошлись бы — в одном месте строже, в другом мягче.
/// </remarks>
public static class DeckPolicy
{
    public const int MaxTitleLength = 128;
    public const int MaxDescriptionLength = 512;

    public static string RequireTitle(string? value)
    {
        var title = (value ?? string.Empty).Trim();

        if (title.Length == 0)
            throw new ValidationException("У колоды должно быть название.");

        if (title.Length > MaxTitleLength)
            throw new ValidationException($"Название длиннее {MaxTitleLength} символов.");

        return title;
    }

    /// <summary>Описание необязательно. Пустая строка превращается в <c>null</c>.</summary>
    public static string? NormalizeDescription(string? value)
    {
        var description = (value ?? string.Empty).Trim();

        if (description.Length == 0)
            return null;

        if (description.Length > MaxDescriptionLength)
            throw new ValidationException($"Описание длиннее {MaxDescriptionLength} символов.");

        return description;
    }
}

/// <summary>
/// Правила слов, которые человек заводит сам.
/// </summary>
/// <remarks>
/// <para>
/// Часть речи — из закрытого списка, а не свободным текстом. По ней слова
/// различаются в словаре (`work` как глагол и как существительное — разные
/// слова), и опечатка в ней создала бы второе слово-двойник, которое никогда
/// не попадёт в упражнение с выбором: неверные варианты подбираются по той же
/// части речи.
/// </para>
/// <para>
/// Список совпадает с тем, что используется в импортированном словаре, иначе
/// свои слова жили бы в отдельном пространстве и не смешивались с общими.
/// </para>
/// </remarks>
public static class WordPolicy
{
    public const int MaxLemmaLength = 64;
    public const int MaxTranslationLength = 128;
    public const int MaxExampleLength = 512;

    /// <summary>Допустимые части речи. Первая — значение по умолчанию для интерфейса.</summary>
    public static readonly IReadOnlyList<string> PartsOfSpeech =
        ["noun", "verb", "adjective", "adverb", "phrase"];

    public static string RequireLemma(string? value)
    {
        var lemma = (value ?? string.Empty).Trim();

        if (lemma.Length == 0)
            throw new ValidationException("У слова должно быть написание.");

        if (lemma.Length > MaxLemmaLength)
            throw new ValidationException($"Слово длиннее {MaxLemmaLength} символов.");

        return lemma;
    }

    public static string RequireTranslation(string? value)
    {
        var translation = (value ?? string.Empty).Trim();

        if (translation.Length == 0)
            throw new ValidationException("У слова должен быть перевод.");

        if (translation.Length > MaxTranslationLength)
            throw new ValidationException($"Перевод длиннее {MaxTranslationLength} символов.");

        return translation;
    }

    public static string RequirePartOfSpeech(string? value)
    {
        var partOfSpeech = (value ?? string.Empty).Trim().ToLowerInvariant();

        if (!PartsOfSpeech.Contains(partOfSpeech))
        {
            throw new ValidationException(
                $"Часть речи должна быть одной из: {string.Join(", ", PartsOfSpeech)}.");
        }

        return partOfSpeech;
    }

    /// <summary>Пример необязателен. Пустая строка превращается в <c>null</c>.</summary>
    public static string? NormalizeExample(string? value)
    {
        var example = (value ?? string.Empty).Trim();

        if (example.Length == 0)
            return null;

        if (example.Length > MaxExampleLength)
            throw new ValidationException($"Пример длиннее {MaxExampleLength} символов.");

        return example;
    }
}
