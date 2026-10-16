using EnglishTrainer.Application.Text;

namespace EnglishTrainer.UnitTests.Application;

/// <summary>
/// Проверка сравнения ответов пользователя с эталоном.
/// </summary>
/// <remarks>
/// <para>
/// Слишком строгое сравнение — самая частая причина, по которой человек бросает
/// тренажёр: он отвечает правильно, а получает «неверно» из-за заглавной буквы.
/// </para>
/// <para>
/// Отдельно зафиксирована граница прощения опечаток: она должна пропускать
/// «depsoit» вместо «deposit» и не пропускать другую форму того же русского
/// слова («красная» вместо «красный»).
/// </para>
/// </remarks>
public sealed class AnswerNormalizerTests
{
    [Theory]
    [InlineData("Deposit", "deposit")]
    [InlineData("  deposit  ", "deposit")]
    [InlineData("ice  cream", "ice cream")]
    [InlineData("CAFÉ", "cafe")]
    [InlineData("Cafe", "cafe")]
    // Знаки препинания не участвуют в сравнении: ни в конце, ни внутри.
    [InlineData("Который час?", "который час")]
    [InlineData("Hello!!!", "hello")]
    [InlineData("don't", "dont")]
    [InlineData("well-known", "wellknown")]
    [InlineData("U.S.A.", "usa")]
    // «й» и «ё» — буквы, а не буква со знаком: убирать у них знак значило бы
    // склеить разные слова («мой» и «мои»).
    [InlineData("Йога", "йога")]
    [InlineData("Ёлка", "ёлка")]
    public void Normalize_BringsAnswersToComparableForm(string input, string expected) =>
        Assert.Equal(expected, AnswerNormalizer.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void Normalize_ForEmptyInput_ReturnsEmptyString(string? input) =>
        Assert.Equal(string.Empty, AnswerNormalizer.Normalize(input));

    [Theory]
    [InlineData("deposit", "залог", "deposit")]
    [InlineData("ЗАЛОГ", "залог", "deposit")]
    [InlineData("  Deposit ", "залог", "deposit")]
    // Тот самый случай с вопросительным знаком: подсказка набрана как перевод,
    // и требовать знак в поле ответа нельзя.
    [InlineData("который час", "Который час?", "what time is it")]
    [InlineData("What time is it?", "Который час?", "what time is it")]
    // Дефис и апостроф пишут и слитно, и раздельно.
    [InlineData("well known", "well-known", "известный")]
    [InlineData("dont", "don't", "не делать")]
    public void Matches_AcceptsAnswerInAnyAcceptedVariant(
        string answer, string firstAccepted, string secondAccepted) =>
        Assert.True(AnswerNormalizer.Matches(answer, [firstAccepted, secondAccepted]));

    [Fact]
    public void Matches_RejectsUnrelatedAnswer() =>
        Assert.False(AnswerNormalizer.Matches("аренда", ["залог", "deposit"]));

    [Fact]
    public void Matches_WithNoAcceptedOptions_ReturnsFalse() =>
        Assert.False(AnswerNormalizer.Matches("deposit", []));

    [Fact]
    public void Matches_WithEmptyAnswer_ReturnsFalse() =>
        Assert.False(AnswerNormalizer.Matches("   ", ["deposit"]));

    [Theory]
    [InlineData("depsoit", "deposit")]          // перестановка букв
    [InlineData("deposit", "depsoit")]          // то же в другую сторону
    [InlineData("recieve", "receive")]          // самая частая опечатка в английском
    [InlineData("apartmant", "apartment")]      // пропущенная буква
    [InlineData("neccessary", "necessary")]     // лишняя буква
    [InlineData("tomorow", "tomorrow")]
    public void Matches_ForgivesTypoInLongWord(string answer, string accepted)
    {
        var match = AnswerNormalizer.Compare(answer, [accepted]);

        Assert.True(match.IsMatch);
        // Опечатка отличается от точного ответа: интерфейс должен показать эталон.
        Assert.True(match.IsApproximate);
    }

    [Fact]
    public void Compare_ForExactAnswer_DoesNotFlagTypo()
    {
        var match = AnswerNormalizer.Compare("Deposit", ["deposit"]);

        Assert.True(match.IsMatch);
        Assert.False(match.IsApproximate);
    }

    [Theory]
    // Короткое слово прощается целиком: одна буква в нём меняет смысл.
    [InlineData("cut", "cat")]
    // Другая форма русского слова — не опечатка, а другое слово: отличается окончание.
    [InlineData("красная", "красный")]
    // Близкое по написанию, но другое слово: отличается начало.
    [InlineData("работа", "забота")]
    // Отличие слишком велико для опечатки.
    [InlineData("аренда", "залог")]
    public void Matches_RejectsDifferentWord(string answer, string accepted) =>
        Assert.False(AnswerNormalizer.Matches(answer, [accepted]));
}
