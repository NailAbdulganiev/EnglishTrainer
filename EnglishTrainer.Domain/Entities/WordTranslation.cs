using EnglishTrainer.Domain.Common;

namespace EnglishTrainer.Domain.Entities;

/// <summary>
/// Перевод слова на целевой язык. Отдельная таблица, а не колонка в <see cref="Word"/>:
/// у слова бывает несколько значений («bank» — берег и банк), и они должны быть
/// различимы, иначе примеры будут привязаны не к тому значению.
/// </summary>
public class WordTranslation : Entity
{
    public int WordId { get; set; }

    /// <summary>Код языка по BCP-47: <c>ru</c>, <c>en</c>.</summary>
    public string LanguageCode { get; set; } = "ru";

    public string Text { get; set; } = string.Empty;

    /// <summary>Пометка о значении, чтобы различать омонимы: «банк (финансы)».</summary>
    public string? Sense { get; set; }

    /// <summary>Основное значение — то, что показывается первым.</summary>
    public bool IsPrimary { get; set; }

    public Word? Word { get; set; }
}
