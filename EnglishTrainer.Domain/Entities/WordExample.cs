using EnglishTrainer.Domain.Common;

namespace EnglishTrainer.Domain.Entities;

/// <summary>
/// Пример употребления слова. Контекст запоминается лучше, чем изолированное слово,
/// а собственные примеры под сценарий («deposit» в аренде жилья) — лучше
/// академичных словарных фраз.
/// </summary>
public class WordExample : Entity
{
    public int WordId { get; set; }

    public string TextEn { get; set; } = string.Empty;

    public string? TextRu { get; set; }

    /// <summary>Ссылка на аудио произношения, если есть.</summary>
    public string? AudioUrl { get; set; }

    /// <summary>
    /// Источник и лицензия контента. Хранится вместе с текстом, чтобы атрибуцию
    /// можно было показать в интерфейсе: для Wiktionary и Wikimedia Commons
    /// указание авторства обязательно по лицензии.
    /// </summary>
    public string? Attribution { get; set; }

    public Word? Word { get; set; }
}
