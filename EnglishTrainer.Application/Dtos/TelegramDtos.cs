using EnglishTrainer.Domain.Enums;

namespace EnglishTrainer.Application.Dtos;

// ─────────────────────────── Telegram ───────────────────────────

/// <summary>Код привязки Telegram к аккаунту и ссылка, которая его переносит.</summary>
/// <param name="Code">Одноразовый код. Показывается человеку, чтобы он мог ввести его вручную.</param>
/// <param name="DeepLink">
/// Готовая ссылка <c>https://t.me/…?start=…</c>. <c>null</c>, если имя бота
/// не задано в настройках: тогда остаётся только ручной ввод.
/// </param>
/// <param name="AlreadyLinked">Уже привязан ли Telegram — код в этом случае заменяет прежнюю привязку.</param>
public sealed record TelegramLinkDto(
    string Code,
    string? DeepLink,
    DateTime ExpiresAtUtc,
    bool AlreadyLinked);

/// <summary>Результат привязки: кого именно привязали.</summary>
public sealed record TelegramLinkResultDto(string Username, string DisplayName);

/// <summary>Состояние привязки Telegram.</summary>
public sealed record TelegramStatusDto(bool Linked, DateTime? LinkedAt);

/// <summary>Вход в мини-апп по данным, подписанным Telegram.</summary>
/// <param name="InitData">Строка <c>initData</c> из <c>Telegram.WebApp</c>.</param>
public sealed record TelegramSignInRequestDto(string InitData);

/// <summary>
/// Привязка аккаунта прямо из мини-аппа: подписанные данные плюс код из профиля.
/// </summary>
/// <remarks>
/// Тот же код, что переносят через бота, но без бота: мини-апп уже знает,
/// какой это аккаунт Telegram, и знает код. Отправлять человека в чат ради
/// того же результата — лишний шаг, а на телефоне ещё и неудобный.
/// </remarks>
public sealed record TelegramLinkFromMiniAppRequestDto(string InitData, string Code);

/// <summary>Слово дня.</summary>
/// <param name="Origin">Откуда слово взялось — от этого зависит текст сообщения.</param>
public sealed record WordOfTheDayDto(
    int WordId,
    string Lemma,
    string PartOfSpeech,
    string? IpaUk,
    string? IpaUs,
    string Translation,
    string? ExampleEn,
    string? ExampleRu,
    WordOfDaySource Origin);

/// <summary>Сводка дня: то, что бот отправляет в напоминании и по команде «сегодня».</summary>
public sealed record TelegramDigestDto(
    DateOnly LocalDate,
    int DueNow,
    int ReviewedToday,
    int DailyGoal,
    int CurrentStreak,
    /// <summary><c>null</c>, если словарь пуст — показывать тогда нечего.</summary>
    WordOfTheDayDto? Word);

/// <summary>Кому и что отправить в очередном напоминании.</summary>
public sealed record TelegramReminderDto(
    int UserId,
    long TelegramId,
    string DisplayName,
    TelegramDigestDto Digest);
