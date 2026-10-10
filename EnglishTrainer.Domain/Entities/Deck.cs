using EnglishTrainer.Domain.Common;

namespace EnglishTrainer.Domain.Entities;

/// <summary>
/// Тематический набор слов. Именно тематика, а не частотный список, —
/// то, что ищут целенаправленно: «слова для собеседования», «для аренды жилья».
/// </summary>
public class Deck : Entity
{
    /// <summary>Человекочитаемый идентификатор для URL: <c>job-interview</c>.</summary>
    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Системная колода поставляется вместе с приложением и не редактируется
    /// пользователями; пользовательские колоды появятся позже.
    /// </summary>
    public bool IsSystem { get; set; } = true;

    /// <summary>Порядок вывода в списке.</summary>
    public int SortOrder { get; set; }

    public ICollection<DeckWord> Words { get; set; } = new List<DeckWord>();
}
