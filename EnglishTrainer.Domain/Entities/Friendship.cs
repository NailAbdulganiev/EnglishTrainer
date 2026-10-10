using EnglishTrainer.Domain.Common;
using EnglishTrainer.Domain.Enums;

namespace EnglishTrainer.Domain.Entities;

/// <summary>
/// Заявка в друзья между двумя пользователями.
/// </summary>
/// <remarks>
/// <para>
/// Дружба — это не флаг, а состояние с историей: одна сторона предложила,
/// вторая ещё не ответила, потом согласилась или отказалась. Поэтому отдельная
/// сущность, а не два идентификатора в таблице пользователей.
/// </para>
/// <para>
/// Направление заявки хранится явно (<see cref="RequesterId"/> и
/// <see cref="AddresseeId"/>), а не «пара без порядка»: иначе невозможно
/// показать, кто кого позвал, и понять, кому отвечать на заявку.
/// </para>
/// </remarks>
public class Friendship : Entity
{
    /// <summary>Кто отправил заявку.</summary>
    public int RequesterId { get; set; }

    /// <summary>Кому отправлена заявка.</summary>
    public int AddresseeId { get; set; }

    public FriendshipStatus Status { get; set; } = FriendshipStatus.Pending;

    /// <summary>Когда на заявку ответили. <c>null</c>, пока она висит.</summary>
    public DateTime? RespondedAt { get; set; }

    public User? Requester { get; set; }

    public User? Addressee { get; set; }

    /// <summary>Дружба действует: заявка принята.</summary>
    public bool IsActive => Status == FriendshipStatus.Accepted;
}
