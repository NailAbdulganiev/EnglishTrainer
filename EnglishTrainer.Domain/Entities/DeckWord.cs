using EnglishTrainer.Domain.Common;

namespace EnglishTrainer.Domain.Entities;

/// <summary>Связь «слово входит в колоду» с порядком внутри колоды.</summary>
public class DeckWord : Entity
{
    public int DeckId { get; set; }

    public int WordId { get; set; }

    /// <summary>Порядок внутри колоды. Обычно совпадает с полезностью, а не с алфавитом.</summary>
    public int Order { get; set; }

    public Deck? Deck { get; set; }

    public Word? Word { get; set; }
}
