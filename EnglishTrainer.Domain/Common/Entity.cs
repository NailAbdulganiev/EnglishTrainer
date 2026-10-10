namespace EnglishTrainer.Domain.Common;

/// <summary>
/// Базовый тип для всех сущностей с суррогатным целочисленным ключом.
/// </summary>
/// <remarks>
/// Ключ целочисленный, а не Guid: он компактнее в индексах и, что важнее,
/// монотонно растёт — это лучший вариант для кластеризованного индекса
/// PostgreSQL. Наружу идентификатор пользователя отдаётся отдельным полем
/// <see cref="Entities.User.PublicId"/>, чтобы не раскрывать количество записей.
/// </remarks>
public abstract class Entity
{
    public int Id { get; set; }

    /// <summary>Момент создания записи (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
