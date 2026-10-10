using EnglishTrainer.Domain.Entities;

namespace EnglishTrainer.Application.Text;

/// <summary>
/// Работа с переводами слова.
/// </summary>
/// <remarks>
/// Порядок переводов и выбор основного нужны в нескольких местах: при показе
/// карточки, при проверке ответа, в избранном и в списках слов. Продублированное
/// правило сортировки рано или поздно разойдётся, и в одном месте слово будет
/// показываться одним значением, а в другом — другим.
/// </remarks>
public static class WordTranslations
{
    /// <summary>Все переводы, основной — первым.</summary>
    public static List<string> All(Word word) =>
        word.Translations
            .OrderByDescending(t => t.IsPrimary)
            .Select(t => t.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

    /// <summary>Основной перевод или <c>null</c>, если переводов нет.</summary>
    public static string? Primary(Word word) => All(word).FirstOrDefault();
}
