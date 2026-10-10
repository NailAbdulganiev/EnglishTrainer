using EnglishTrainer.Domain.Common;

namespace EnglishTrainer.Domain.Entities;

/// <summary>
/// Слово, отмеченное пользователем как избранное.
/// </summary>
/// <remarks>
/// <para>
/// Зачем это нужно, если уже есть интервальное повторение: алгоритм расставляет
/// приоритеты по вероятности забывания, но не знает, какие слова человеку важны
/// прямо сейчас — завтра собеседование, послезавтра визит к врачу. Избранное
/// позволяет влиять на это вручную.
/// </para>
/// <para>
/// Избранные слова получают приоритет в очереди повторений и доступны отдельной
/// тренировкой, которая не ждёт назначенного срока.
/// </para>
/// </remarks>
public class FavoriteWord : Entity
{
    public int UserId { get; set; }

    public int WordId { get; set; }

    public User? User { get; set; }

    public Word? Word { get; set; }
}
