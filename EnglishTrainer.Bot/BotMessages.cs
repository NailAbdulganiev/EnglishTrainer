using System.Text;
using EnglishTrainer.Application.Dtos;
using EnglishTrainer.Domain.Enums;

namespace EnglishTrainer.Bot;

/// <summary>
/// Тексты сообщений бота.
/// </summary>
/// <remarks>
/// <para>
/// Собраны в одном месте, а не разбросаны по обработчикам: сообщение в мессенджере
/// читают целиком, и править его формулировку удобнее рядом с остальными.
/// </para>
/// <para>
/// Разметка не используется намеренно — ни Markdown, ни HTML. Оба требуют
/// экранирования, а в текстах есть и транскрипция с символами вроде «*», и
/// переводы со скобками; ошибка в экранировании превращала бы сообщение
/// в мусор, причём незаметно на стороне сервера. Обычный текст выглядит проще
/// и ломаться не может.
/// </para>
/// </remarks>
internal static class BotMessages
{
    public static string Welcome(string? firstName, bool linked)
    {
        var greeting = string.IsNullOrWhiteSpace(firstName) ? "Привет!" : $"Привет, {firstName}!";

        var text = new StringBuilder();

        text.AppendLine("📘 EnglishTrainer");
        text.AppendLine();
        text.AppendLine($"{greeting} Я присылаю одно слово в день и напоминаю о повторениях.");
        text.AppendLine();

        if (linked)
        {
            text.AppendLine("Аккаунт привязан — прогресс общий с веб-версией.");
        }
        else
        {
            // Инструкция по шагам: без неё человек видит бота, который «ничего
            // не делает», и уходит. Кнопка мини-аппа в этом состоянии бесполезна —
            // она откроет приложение, которое не знает, чей это прогресс.
            text.AppendLine("Осталось привязать аккаунт:");
            text.AppendLine("1. Откройте тренажёр в браузере и зайдите в «Профиль».");
            text.AppendLine("2. Нажмите «Привязать Telegram» — появится код.");
            text.AppendLine("3. Отправьте мне команду /link и через пробел этот код.");
            text.AppendLine();
            text.AppendLine("Если аккаунта ещё нет, там же можно зарегистрироваться.");
        }

        text.AppendLine();
        text.Append("Список команд — /help");

        return text.ToString();
    }

    public static string Help() =>
        """
        Что я умею:

        /word — слово дня
        /today — сколько повторений ждёт и как идёт серия
        /stop — выключить напоминания
        /on — включить напоминания
        /help — этот список

        Привязка аккаунта: код из профиля на сайте отправьте как
        /link ВАШ_КОД

        Отвязать Telegram можно в профиле на сайте.
        """;

    public static string Linked(string displayName, string username) =>
        $"""
        ✅ Готово, аккаунт @{username} ({displayName}) привязан.

        Теперь прогресс общий с веб-версией, а напоминания будут приходить в выбранное вами время. Изменить его можно в настройках на сайте.
        """;

    public static string AlreadyLinked(string username) =>
        $"Аккаунт @{username} уже привязан. Прогресс общий с веб-версией.";

    public static string RemindersOff() =>
        """
        🔕 Напоминания выключены.

        Включить обратно — /on. Слово дня по команде /word будет приходить в любом случае.
        """;

    public static string RemindersOn(string time) =>
        $"""
        🔔 Напоминания включены. Буду писать около {time} по вашему часовому поясу.

        Время меняется в настройках на сайте.
        """;

    public static string Unknown() =>
        """
        Не понял команду.

        /word — слово дня, /today — сводка, /help — все команды.
        """;

    private static string LinkFailed(string reason) =>
        $"""
        Не удалось привязать: {reason}

        Новый код можно получить в профиле на сайте.
        """;

    public static string LinkFailedFrom(string reason) => LinkFailed(reason);

    /// <summary>Напоминание в назначенное время.</summary>
    public static string Reminder(TelegramDigestDto digest)
    {
        var text = new StringBuilder();

        text.AppendLine(digest.DueNow > 0
            ? $"🔔 Пора повторить: {Plural(digest.DueNow, "слово", "слова", "слов")}"
            : "🔔 Время позаниматься");

        text.AppendLine();
        text.AppendLine(Progress(digest));
        text.AppendLine();

        if (digest.Word is { } word)
        {
            text.Append(DescribeWord(word, includeHeader: true));
        }
        else
        {
            text.Append("Слово дня появится, когда словарь загрузится.");
        }

        return text.ToString();
    }

    /// <summary>Сводка по команде /today.</summary>
    public static string Digest(TelegramDigestDto digest)
    {
        var text = new StringBuilder();

        text.AppendLine("📊 Сегодня");
        text.AppendLine();
        text.AppendLine(Progress(digest));

        if (digest.Word is { } word)
        {
            text.AppendLine();
            text.Append(DescribeWord(word, includeHeader: true));
        }

        return text.ToString();
    }

    /// <summary>Слово дня по команде /word.</summary>
    public static string WordOfTheDay(WordOfTheDayDto word) =>
        DescribeWord(word, includeHeader: true);

    private static string Progress(TelegramDigestDto digest)
    {
        var text = new StringBuilder();

        text.AppendLine($"К повторению: {digest.DueNow}");
        text.AppendLine($"Сегодня сделано: {digest.ReviewedToday} из {digest.DailyGoal}");

        // Серия показывается только когда она есть: «серия: 0 дней» — это упрёк,
        // а не мотивация, и в первом сообщении новичку он лишний.
        if (digest.CurrentStreak > 0)
        {
            text.Append($"Серия: {Plural(digest.CurrentStreak, "день", "дня", "дней")}");
        }
        else
        {
            text.Append("Серия начнётся с первого ответа сегодня");
        }

        return text.ToString();
    }

    private static string DescribeWord(WordOfTheDayDto word, bool includeHeader)
    {
        var text = new StringBuilder();

        if (includeHeader)
        {
            text.AppendLine(word.Origin switch
            {
                WordOfDaySource.Review => "Слово дня (пора повторить)",
                WordOfDaySource.Upcoming => "Слово дня (следующее на очереди)",
                _ => "Слово дня"
            });
        }

        text.AppendLine();
        text.AppendLine($"{word.Lemma} — {word.Translation}");
        text.AppendLine($"{PartOfSpeech(word.PartOfSpeech)} · {Transcription(word)}");

        if (!string.IsNullOrWhiteSpace(word.ExampleEn))
        {
            text.AppendLine();
            text.Append(word.ExampleEn);

            if (!string.IsNullOrWhiteSpace(word.ExampleRu))
            {
                text.Append($" — {word.ExampleRu}");
            }
        }

        return text.ToString();
    }

    private static string Transcription(WordOfTheDayDto word) =>
        new[] { word.IpaUs, word.IpaUk }.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
        // Транскрипции может не быть: часть слов добавлена без неё, и «[]»
        // в сообщении выглядело бы как сломанная вёрстка.
        ?? "транскрипция не указана";

    /// <summary>Часть речи по-русски. Незнакомое значение отдаётся как есть.</summary>
    private static string PartOfSpeech(string value) => value switch
    {
        "noun" => "существительное",
        "verb" => "глагол",
        "adjective" => "прилагательное",
        "adverb" => "наречие",
        "phrase" => "фраза",
        "preposition" => "предлог",
        "pronoun" => "местоимение",
        "numeral" => "числительное",
        "conjunction" => "союз",
        "interjection" => "междометие",
        _ => value
    };

    /// <summary>Русское согласование числительных: 1 слово, 2 слова, 5 слов.</summary>
    private static string Plural(int count, string one, string few, string many)
    {
        var abs = Math.Abs(count) % 100;
        var last = abs % 10;

        var form = abs is > 10 and < 20 ? many
            : last == 1 ? one
            : last is >= 2 and <= 4 ? few
            : many;

        return $"{count} {form}";
    }
}
