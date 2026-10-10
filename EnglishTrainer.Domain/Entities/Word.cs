using EnglishTrainer.Domain.Common;
using EnglishTrainer.Domain.Enums;

namespace EnglishTrainer.Domain.Entities;

/// <summary>
/// Слово в общем справочнике. Данные этой сущности одинаковы для всех
/// пользователей и меняются только импортом — значит, это идеальный
/// кандидат на кеширование.
/// </summary>
public class Word : Entity
{
    /// <summary>Слово в базовой форме (лемма). Уникально в пределах части речи.</summary>
    public string Lemma { get; set; } = string.Empty;

    /// <summary>Часть речи: noun, verb, adjective и т. д.</summary>
    public string PartOfSpeech { get; set; } = string.Empty;

    /// <summary>Транскрипция в британском варианте, например <c>/həˈləʊ/</c>.</summary>
    public string? IpaUk { get; set; }

    /// <summary>Транскрипция в американском варианте.</summary>
    public string? IpaUs { get; set; }

    /// <summary>
    /// Частотный ранг: 1 — самое частое слово языка. По нему отбираются слова
    /// для новых карточек, чтобы первыми шли самые полезные, а не случайные.
    /// </summary>
    public int? FrequencyRank { get; set; }

    public CefrLevel Level { get; set; } = CefrLevel.Unknown;

    public ICollection<WordTranslation> Translations { get; set; } = new List<WordTranslation>();

    public ICollection<WordExample> Examples { get; set; } = new List<WordExample>();

    public ICollection<DeckWord> Decks { get; set; } = new List<DeckWord>();
}
