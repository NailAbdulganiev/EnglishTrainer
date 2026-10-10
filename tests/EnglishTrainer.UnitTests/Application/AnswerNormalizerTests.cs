using EnglishTrainer.Application.Text;

namespace EnglishTrainer.UnitTests.Application;

/// <summary>
/// Проверка сравнения ответов пользователя с эталоном.
/// </summary>
/// <remarks>
/// Слишком строгое сравнение — самая частая причина, по которой человек бросает
/// тренажёр: он отвечает правильно, а получает «неверно» из-за заглавной буквы.
/// </remarks>
public sealed class AnswerNormalizerTests
{
    [Theory]
    [InlineData("Deposit", "deposit")]
    [InlineData("  deposit  ", "deposit")]
    [InlineData("ice  cream", "ice cream")]
    [InlineData("CAFÉ", "cafe")]
    [InlineData("Cafe", "cafe")]
    public void Normalize_BringsAnswersToComparableForm(string input, string expected) =>
        Assert.Equal(expected, AnswerNormalizer.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_ForEmptyInput_ReturnsEmptyString(string? input) =>
        Assert.Equal(string.Empty, AnswerNormalizer.Normalize(input));

    [Theory]
    [InlineData("deposit", "залог", "deposit")]
    [InlineData("ЗАЛОГ", "залог", "deposit")]
    [InlineData("  Deposit ", "залог", "deposit")]
    public void Matches_AcceptsAnswerInAnyAcceptedVariant(
        string answer, string firstAccepted, string secondAccepted) =>
        Assert.True(AnswerNormalizer.Matches(answer, [firstAccepted, secondAccepted]));

    [Fact]
    public void Matches_RejectsUnrelatedAnswer() =>
        Assert.False(AnswerNormalizer.Matches("аренда", ["залог", "deposit"]));

    [Fact]
    public void Matches_WithNoAcceptedOptions_ReturnsFalse() =>
        Assert.False(AnswerNormalizer.Matches("deposit", []));
}
