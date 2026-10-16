namespace EnglishTrainer.Application.Dtos;

// ─────────────────────────── Рейтинг друзей ───────────────────────────

/// <summary>Строка недельного рейтинга.</summary>
/// <param name="Rank">Место. У равных результатов место одинаковое.</param>
/// <param name="IsMe">Своя строка: интерфейс подсвечивает её, чтобы не искать себя глазами.</param>
public sealed record LeaderboardEntryDto(
    int Rank,
    string Username,
    string DisplayName,
    string? AvatarUrl,
    bool IsMe,
    int WeeklyWords,
    int WeeklyReviews,
    int CurrentStreak);

/// <summary>Недельный рейтинг: я и мои друзья.</summary>
/// <param name="From">Первый день окна.</param>
/// <param name="To">Последний день окна — сегодня в поясе смотрящего.</param>
/// <param name="FriendCount">Сколько друзей участвует. Ноль означает, что рейтинг пока из одной строки.</param>
public sealed record LeaderboardDto(
    DateOnly From,
    DateOnly To,
    int FriendCount,
    IReadOnlyList<LeaderboardEntryDto> Entries);
