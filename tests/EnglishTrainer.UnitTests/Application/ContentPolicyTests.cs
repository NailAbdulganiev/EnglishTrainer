using EnglishTrainer.Application.Text;
using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Exceptions;

namespace EnglishTrainer.UnitTests.Application;

/// <summary>
/// Правила личных колод и своих слов.
/// </summary>
/// <remarks>
/// <para>
/// Это первый пользовательский ввод, который попадает не в личные настройки,
/// а в общий словарь: слова видят все, и колода потом тренируется теми же
/// упражнениями, что и системная. Поэтому проверяется не только «пустое
/// отклоняется», но и согласованность правил с самими собой — список частей
/// речи обязан проходить собственную проверку, иначе часть списка окажется
/// нерабочей.
/// </para>
/// <para>
/// Отдельно закреплено правило владения: системную колоду не редактирует
/// никто, чужую — тем более. Это единственная защита личных колод, потому что
/// проверка живёт в сущности, а не в сервисе.
/// </para>
/// </remarks>
public sealed class ContentPolicyTests
{
    [Theory]
    [InlineData("  Моя колода  ", "Моя колода")]
    [InlineData("Слова с работы", "Слова с работы")]
    public void RequireTitle_TrimsAndReturnsTitle(string input, string expected) =>
        Assert.Equal(expected, DeckPolicy.RequireTitle(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequireTitle_WithoutTitle_ThrowsValidationException(string? value) =>
        // Пустая колода без названия неопознаваема в списке: человек увидит
        // несколько одинаковых пустых строк.
        Assert.Throws<ValidationException>(() => DeckPolicy.RequireTitle(value));

    [Fact]
    public void RequireTitle_LongerThanLimit_ThrowsValidationException()
    {
        var tooLong = new string('a', DeckPolicy.MaxTitleLength + 1);

        Assert.Throws<ValidationException>(() => DeckPolicy.RequireTitle(tooLong));
    }

    [Fact]
    public void RequireTitle_AtLimit_AcceptsIt()
    {
        // Граница включительна: проверка «строго меньше» отсекала бы ровно то,
        // что политика объявляет допустимым.
        var longest = new string('a', DeckPolicy.MaxTitleLength);

        Assert.Equal(longest, DeckPolicy.RequireTitle(longest));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeDescription_ForEmptyInput_ReturnsNull(string? value) =>
        // Описание необязательно, и пустая строка в базе означала бы «описание
        // есть, но пустое» — два разных состояния вместо одного.
        Assert.Null(DeckPolicy.NormalizeDescription(value));

    [Fact]
    public void NormalizeDescription_LongerThanLimit_ThrowsValidationException()
    {
        var tooLong = new string('a', DeckPolicy.MaxDescriptionLength + 1);

        Assert.Throws<ValidationException>(() => DeckPolicy.NormalizeDescription(tooLong));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequireLemma_WithoutWord_ThrowsValidationException(string? value) =>
        Assert.Throws<ValidationException>(() => WordPolicy.RequireLemma(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequireTranslation_WithoutTranslation_ThrowsValidationException(string? value) =>
        // Слово без перевода нечем переспросить: в упражнении на ввод вопрос
        // задаётся именно переводом.
        Assert.Throws<ValidationException>(() => WordPolicy.RequireTranslation(value));

    [Fact]
    public void RequireLemma_Trims()
    {
        Assert.Equal("landscape", WordPolicy.RequireLemma("  landscape  "));
    }

    [Fact]
    public void RequireLemma_LongerThanLimit_ThrowsValidationException()
    {
        var tooLong = new string('a', WordPolicy.MaxLemmaLength + 1);

        Assert.Throws<ValidationException>(() => WordPolicy.RequireLemma(tooLong));
    }

    [Theory]
    [InlineData("noun", "noun")]
    [InlineData("VERB", "verb")]
    [InlineData("  Adjective  ", "adjective")]
    public void RequirePartOfSpeech_NormalizesCaseAndSpaces(string input, string expected) =>
        Assert.Equal(expected, WordPolicy.RequirePartOfSpeech(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("существительное")]
    [InlineData("preposition")]
    [InlineData("noun-verb")]
    public void RequirePartOfSpeech_WithValueOutsideTheList_ThrowsValidationException(string? value) =>
        // Часть речи — из закрытого списка. Свободный текст создал бы слово-двойник,
        // которое никогда не попадёт в упражнение с выбором: неверные варианты
        // подбираются по той же части речи.
        //
        // Пробелы по краям при этом не ошибка: они обрезаются до проверки,
        // и «noun » — это то же самое, что «noun».
        Assert.Throws<ValidationException>(() => WordPolicy.RequirePartOfSpeech(value));

    [Fact]
    public void EveryListedPartOfSpeech_PassesItsOwnCheck()
    {
        // Политика обязана быть согласована сама с собой: значение из списка,
        // которое проверка не принимает, — это нерабочая опция в интерфейсе,
        // и заметить её иначе нечем.
        Assert.NotEmpty(WordPolicy.PartsOfSpeech);
        Assert.All(WordPolicy.PartsOfSpeech, part =>
            Assert.Equal(part, WordPolicy.RequirePartOfSpeech(part)));
    }

    [Fact]
    public void PartsOfSpeech_AreUnique() =>
        Assert.Equal(
            WordPolicy.PartsOfSpeech.Count,
            WordPolicy.PartsOfSpeech.Distinct(StringComparer.Ordinal).Count());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeExample_ForEmptyInput_ReturnsNull(string? value) =>
        Assert.Null(WordPolicy.NormalizeExample(value));

    [Fact]
    public void NormalizeExample_LongerThanLimit_ThrowsValidationException()
    {
        var tooLong = new string('a', WordPolicy.MaxExampleLength + 1);

        Assert.Throws<ValidationException>(() => WordPolicy.NormalizeExample(tooLong));
    }

    /* ── Владение колодой ───────────────────────────────────────────────── */

    [Fact]
    public void SystemDeck_IsNotEditableEvenByItsOwner()
    {
        // Системная колода ничья: её наполняет импорт словаря, и правка
        // из интерфейса сломала бы одинаковый словарь у всех пользователей.
        var deck = new Deck { IsSystem = true, OwnerId = 7 };

        Assert.False(deck.IsEditableBy(7));
    }

    [Fact]
    public void OwnDeck_IsEditable()
    {
        var deck = new Deck { IsSystem = false, OwnerId = 7 };

        Assert.True(deck.IsEditableBy(7));
    }

    [Fact]
    public void SomeoneElsesDeck_IsNotEditable()
    {
        var deck = new Deck { IsSystem = false, OwnerId = 7 };

        Assert.False(deck.IsEditableBy(8));
    }

    [Fact]
    public void DeckWithoutOwner_IsNotEditable()
    {
        // Ничья несистемная колода — состояние, которого быть не должно,
        // но правило обязано отказать, а не разрешить правку «всем».
        var deck = new Deck { IsSystem = false, OwnerId = null };

        Assert.False(deck.IsEditableBy(7));
    }
}
