using System.Globalization;
using System.Text;

namespace EnglishTrainer.Application.Text;

/// <summary>
/// Результат сравнения ответа с эталоном.
/// </summary>
/// <param name="IsMatch">Ответ зачтён.</param>
/// <param name="IsApproximate">
/// Зачтён с опечаткой: написание отличается, но не настолько, чтобы считать
/// ответ неверным. Нужен интерфейсу, чтобы показать эталон, не отбирая балл.
/// </param>
public readonly record struct AnswerMatch(bool IsMatch, bool IsApproximate)
{
    /// <summary>Не совпало ни точно, ни с опечаткой.</summary>
    public static AnswerMatch No => new(false, false);

    /// <summary>Совпало после нормализации: регистр, пробелы и знаки не важны.</summary>
    public static AnswerMatch Exact => new(true, false);
}

/// <summary>
/// Нормализация ответов пользователя перед сравнением.
/// </summary>
/// <remarks>
/// <para>
/// Пользователь не должен страдать из-за регистра, лишних пробелов, диакритики
/// или знаков препинания — иначе он будет получать «неверно» на правильный ответ
/// и потеряет доверие к системе.
/// </para>
/// <para>
/// Знаки препинания отбрасываются целиком, а не заменяются пробелом: перевод
/// «Который час?» и ответ «который час» — одно и то же, и требовать вопросительный
/// знак в поле ввода нельзя. Апостроф и дефис удаляются без пробела, поэтому
/// «don't» == «dont», а «well-known» == «wellknown»; на случай, когда их пишут
/// раздельно («well known»), сравнение дополнительно повторяется по строке
/// без пробелов.
/// </para>
/// <para>
/// Опечатки прощаются отдельно и осторожно (<see cref="IsTypo"/>): одна — в слове
/// от пяти букв, две — от девяти, и только если совпадает последняя буква.
/// Последняя буква — единственный дешёвый признак, который отсекает самую частую
/// ложную замену: разные формы одного русского слова («красный» и «красная»)
/// отличаются именно окончанием, а опечатки в английском слове почти всегда
/// приходятся на середину.
/// </para>
/// </remarks>
public static class AnswerNormalizer
{
    /// <summary>Короткое слово целиком: в нём одна опечатка меняет смысл.</summary>
    private const int ShortWordLength = 5;

    /// <summary>Длина, с которой прощается вторая опечатка.</summary>
    private const int LongWordLength = 9;

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                // Диакритика снимается только с латиницы: «café» и «cafe» — один ответ.
                // В кириллице «й» и «ё» — самостоятельные буквы, а не буква со знаком,
                // и снимать с них знак нельзя: «мой» и «мои» — разные слова. Знак
                // остаётся в строке и возвращается в букву при сборке обратно в FormC.
                if (builder.Length > 0 && IsLatinLetter(builder[^1]))
                    continue;

                builder.Append(ch);
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                continue;
            }

            // Апостроф и дефис исчезают бесследно: «don't» — это «dont», а не «don t».
            if (IsInsideWord(ch))
                continue;

            // Остальные знаки — разделители: «Который час?» — это два слова.
            builder.Append(' ');
        }

        // Схлопываем повторяющиеся пробелы: «ice  cream» == «ice cream».
        return string.Join(' ', builder.ToString()
            .Normalize(NormalizationForm.FormC)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Строка без пробелов: «ice cream» == «icecream» == «ice-cream».</summary>
    public static string Squash(string? value) => Normalize(value).Replace(" ", string.Empty);

    /// <summary>
    /// Сравнивает ответ с любым из принятых вариантов.
    /// </summary>
    /// <remarks>
    /// Возвращается не <c>bool</c>, а пара: интерфейсу нужно знать, был ли ответ
    /// точным. Оценка от этого не зависит — при опечатке ответ тоже зачтён.
    /// </remarks>
    public static AnswerMatch Compare(string? userAnswer, IEnumerable<string> accepted)
    {
        var answer = Normalize(userAnswer);

        if (answer.Length == 0)
            return AnswerMatch.No;

        var squashedAnswer = answer.Replace(" ", string.Empty);
        var approximate = false;

        foreach (var option in accepted)
        {
            var reference = Normalize(option);

            if (reference.Length == 0)
                continue;

            // Точное совпадение проверяется первым: оно самое частое, и платить
            // за него расстоянием Левенштейна незачем.
            if (reference == answer || reference.Replace(" ", string.Empty) == squashedAnswer)
                return AnswerMatch.Exact;

            if (IsTypo(squashedAnswer, reference.Replace(" ", string.Empty)))
                approximate = true;
        }

        return approximate ? new AnswerMatch(true, true) : AnswerMatch.No;
    }

    public static bool Matches(string? userAnswer, IEnumerable<string> accepted) =>
        Compare(userAnswer, accepted).IsMatch;

    /// <summary>Опечатка, а не другой ответ.</summary>
    private static bool IsTypo(string answer, string reference)
    {
        // Сравниваются строки без пробелов: «ice cream» и «ice crean» — это одна
        // опечатка, и разбирать её по словам значило бы усложнить правило без выгоды.
        var length = Math.Max(answer.Length, reference.Length);

        // Разная длина — это уже добавленная или потерянная буква, но не другое слово.
        if (length < ShortWordLength || Math.Abs(answer.Length - reference.Length) > 2)
            return false;

        // Совпадать должны начало и конец, а отличие — приходиться на середину.
        //
        // Это не украшение, а то, что отличает опечатку от другого слова. Русские
        // формы одного слова расходятся в окончании («красный» и «красная»),
        // а близкие по написанию разные слова — в начале («работа» и «забота»).
        // Опечатка же почти всегда в середине длинного слова, поэтому оба признака
        // отсекают ложные зачёты и не мешают настоящим: длина обеих строк здесь
        // не меньше трёх символов, и срез из двух букв всегда существует.
        if (answer[..2] != reference[..2] || answer[^1] != reference[^1])
            return false;

        var allowed = length >= LongWordLength ? 2 : 1;

        return EditDistance(answer, reference) <= allowed;
    }

    /// <summary>
    /// Расстояние Дамерау — Левенштейна.
    /// </summary>
    /// <remarks>
    /// Перестановка соседних букв считается одной опечаткой (<c>recieve</c> вместо
    /// <c>receive</c>) — без этого самая частая опечатка в английском не прощалась бы.
    /// Строки здесь короткие (слово или короткий перевод), поэтому таблица
    /// считается целиком, без ранних выходов: экономить нечего, а ошибка
    /// в оценке расстояния стоила бы зачтённого неверного ответа.
    /// </remarks>
    private static int EditDistance(string first, string second)
    {
        // Три строки таблицы, а не вся матрица: для перестановки соседних букв
        // нужна строка через одну, поэтому одной предыдущей строки мало.
        var twoRowsBack = new int[second.Length + 1];
        var previous = new int[second.Length + 1];
        var current = new int[second.Length + 1];

        for (var j = 0; j <= second.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= first.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= second.Length; j++)
            {
                var cost = first[i - 1] == second[j - 1] ? 0 : 1;

                var value = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);

                // Перестановка соседних символов: «ab» → «ba».
                if (i > 1 && j > 1 && first[i - 1] == second[j - 2] && first[i - 2] == second[j - 1])
                    value = Math.Min(value, twoRowsBack[j - 2] + 1);

                current[j] = value;
            }

            (twoRowsBack, previous, current) = (previous, current, twoRowsBack);
        }

        return previous[second.Length];
    }

    /// <summary>Знак, который пишется внутри слова и не разделяет его на два.</summary>
    private static bool IsInsideWord(char ch) =>
        ch is '\'' or '\u2019' or '\u02BC' or '-' or '\u2010' or '\u2011' or '\u2012'
            or '\u2013' or '\u2014' or '\u2212' or '.';

    private static bool IsLatinLetter(char ch) => ch is >= 'a' and <= 'z';
}
