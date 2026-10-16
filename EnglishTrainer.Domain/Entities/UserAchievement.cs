using EnglishTrainer.Domain.Common;

namespace EnglishTrainer.Domain.Entities;

/// <summary>
/// Полученное достижение.
/// </summary>
/// <remarks>
/// <para>
/// Хранится не «прогресс», а факт получения и его дата. Прогресс считается
/// из уже накопленных данных, поэтому дублировать его сюда нельзя: два
/// источника одного числа рано или поздно разойдутся, и человек увидит
/// «100 из 100», но закрытую плашку.
/// </para>
/// <para>
/// Дата нужна не для порядка, а для смысла: «получено 3 марта» — это то,
/// чем хочется поделиться, а «получено» без даты — просто галочка.
/// </para>
/// </remarks>
public class UserAchievement : Entity
{
    public int UserId { get; set; }

    /// <summary>Код достижения из <c>AchievementCatalog</c>.</summary>
    public string Code { get; set; } = string.Empty;

    public DateTime UnlockedAt { get; set; }

    public User? User { get; set; }
}
