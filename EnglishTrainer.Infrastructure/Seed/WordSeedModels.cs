namespace EnglishTrainer.Infrastructure.Seed;

/// <summary>
/// Содержимое одного файла словаря: тематическая колода и её слова.
/// </summary>
/// <remarks>
/// Модели изменяемы только при создании (<c>init</c>) — это данные из файла,
/// их не нужно править в коде. Имена свойств совпадают с ключами JSON,
/// поэтому разбор идёт без атрибутов.
/// </remarks>
internal sealed class WordSeedFile
{
    public string Slug { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public int SortOrder { get; init; }

    public IReadOnlyList<WordSeed> Words { get; init; } = [];
}

/// <summary>Одно слово словаря вместе с переводами и примерами.</summary>
internal sealed class WordSeed
{
    public string Lemma { get; init; } = string.Empty;

    public string PartOfSpeech { get; init; } = string.Empty;

    public string? IpaUk { get; init; }

    public string? IpaUs { get; init; }

    /// <summary>Уровень CEFR строкой: <c>A1</c>, <c>A2</c>, <c>B1</c>, <c>B2</c>.</summary>
    public string Level { get; init; } = "A1";

    public IReadOnlyList<TranslationSeed> Translations { get; init; } = [];

    public IReadOnlyList<ExampleSeed> Examples { get; init; } = [];
}

internal sealed class TranslationSeed
{
    public string Text { get; init; } = string.Empty;

    public string? Sense { get; init; }

    public bool IsPrimary { get; init; }
}

internal sealed class ExampleSeed
{
    public string TextEn { get; init; } = string.Empty;

    public string? TextRu { get; init; }

    public string? AudioUrl { get; init; }

    public string? Attribution { get; init; }
}
