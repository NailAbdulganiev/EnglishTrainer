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
    /// пользователями. Пользовательские колоды создаёт сам человек, и править
    /// их может только он.
    /// </summary>
    public bool IsSystem { get; set; } = true;

    /// <summary>
    /// Владелец личной колоды. <c>null</c> — колода системная.
    /// </summary>
    /// <remarks>
    /// Ссылка на пользователя, а не флаг «личная»: по флагу нельзя ответить,
    /// чья это колода, а значит нельзя и решить, кому её разрешено править.
    /// Системные колоды остаются ничьими — их наполняет импорт словаря.
    /// </remarks>
    public int? OwnerId { get; set; }

    public User? Owner { get; set; }

    /// <summary>Порядок вывода в списке.</summary>
    public int SortOrder { get; set; }

    public ICollection<DeckWord> Words { get; set; } = new List<DeckWord>();

    /// <summary>
    /// Можно ли править эту колоду данному пользователю.
    /// </summary>
    /// <remarks>
    /// Правило живёт в сущности, а не в сервисе: проверка «чужую колоду менять
    /// нельзя» нужна и удалению колоды, и добавлению слова, и удалению слова —
    /// три места, в которых легко забыть одно и тем самым открыть дыру.
    /// </remarks>
    public bool IsEditableBy(int userId) => !IsSystem && OwnerId == userId;
}
