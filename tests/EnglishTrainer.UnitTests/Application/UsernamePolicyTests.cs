using System.Globalization;
using EnglishTrainer.Application.Text;
using EnglishTrainer.Domain.Exceptions;

namespace EnglishTrainer.UnitTests.Application;

/// <summary>
/// Проверка правил ника.
/// </summary>
/// <remarks>
/// <para>
/// Ник — публичный идентификатор: он хранится в нижнем регистре, попадает
/// в адреса API и в списки друзей. Ошибка в нормализации означает двойников
/// («Ivan» и «ivan» как два разных человека), ошибка в проверке — мусорные ники,
/// которые нельзя ни показать, ни найти.
/// </para>
/// <para>
/// Самый ценный случай — согласованность политики с самой собой: ник, который
/// предлагает <c>SuggestFromEmail</c>, обязан проходить <c>Require</c>. Иначе
/// человек не может завершить регистрацию по собственному email, а причина
/// отказа ему не видна.
/// </para>
/// </remarks>
public sealed class UsernamePolicyTests
{
    [Theory]
    [InlineData("  Ivan  ", "ivan")]
    [InlineData("JOHN_DOE", "john_doe")]
    [InlineData("  IVAN-99  ", "ivan-99")]
    [InlineData("\tIvan\t", "ivan")]
    [InlineData("already-lower", "already-lower")]
    public void Normalize_BringsUsernameToCanonicalForm(string input, string expected) =>
        Assert.Equal(expected, UsernamePolicy.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_ForMissingInput_ReturnsEmptyString(string? input) =>
        // Отсутствующий ник не должен превращаться в null: нормализация вызывается
        // и из проверки, и из подбора ника, и падать на пустом входе ей нельзя.
        Assert.Equal(string.Empty, UsernamePolicy.Normalize(input));

    [Fact]
    public void Require_WithValidUsername_ReturnsCanonicalForm() =>
        // Нормализация до сохранения — единственный способ удержать уникальность:
        // в базе не должно оказаться «John_Doe» рядом с «john_doe».
        Assert.Equal("john_doe", UsernamePolicy.Require("John_Doe"));

    [Theory]
    [InlineData("ab")]
    [InlineData("a")]
    [InlineData("ivan petrov")]
    [InlineData("иван")]
    [InlineData("ivan.petrov")]
    [InlineData("ivan@mail")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Require_WithUnusableUsername_ThrowsValidationException(string? value) =>
        // Ожидается именно ValidationException: её текст показывается пользователю
        // и превращается в 400, а не в 500 с записью в логе об ошибке сервера.
        Assert.Throws<ValidationException>(() => UsernamePolicy.Require(value));

    [Fact]
    public void Require_WithUsernameLongerThanMaximum_ThrowsValidationException()
    {
        var tooLong = new string('a', UsernamePolicy.MaxLength + 1);

        Assert.Throws<ValidationException>(() => UsernamePolicy.Require(tooLong));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("  abc  ")]
    [InlineData("a-b")]
    [InlineData("a_b")]
    public void Require_WithUsernameAtMinimumLength_AcceptsIt(string value) =>
        // Граница min включительна: проверка «строго меньше» отсекала бы ровно те
        // ники, которые политика объявляет допустимыми. Пробелы по краям при этом
        // обрезаются до проверки длины, а не после.
        Assert.Equal(UsernamePolicy.Normalize(value), UsernamePolicy.Require(value));

    [Fact]
    public void Require_WithUsernameOfMaximumLength_AcceptsIt()
    {
        var longest = new string('a', UsernamePolicy.MaxLength);

        Assert.Equal(longest, UsernamePolicy.Require(longest));
    }

    [Theory]
    [InlineData("johannesmetz98@gmail.com", "johannesmetz98")]
    [InlineData("a.b+tag@x.com", "abtag")]
    [InlineData("Ivan.Petrov@Mail.ru", "ivanpetrov")]
    public void SuggestFromEmail_TakesLocalPartAndDropsUnusableSymbols(string email, string expected) =>
        // Регистрация не должна заставлять человека придумывать ник: предлагаем
        // узнаваемое значение из email, оставляя только допустимые символы.
        Assert.Equal(expected, UsernamePolicy.SuggestFromEmail(email));

    [Theory]
    [InlineData("ab@x.com")]
    [InlineData("a@x.com")]
    [InlineData("!!!@mail.ru")]
    [InlineData("Йван@mail.ru")]
    [InlineData("")]
    public void SuggestFromEmail_WithUnusableLocalPart_ReturnsFallbackAcceptedByPolicy(string email)
    {
        var suggested = UsernamePolicy.SuggestFromEmail(email);

        // Слишком короткий или нечитаемый local-part не повод отказывать
        // в регистрации: подставляем запасную основу, годную по длине.
        Assert.InRange(suggested.Length, UsernamePolicy.MinLength, UsernamePolicy.MaxLength);
        Assert.Equal(suggested, UsernamePolicy.Require(suggested));
    }

    [Theory]
    [InlineData("johannesmetz98@gmail.com")]
    [InlineData("a.b+tag@x.com")]
    [InlineData("ab@x.com")]
    [InlineData("x@y.z")]
    [InlineData("___@x.com")]
    [InlineData("!!!@mail.ru")]
    [InlineData("@example.com")]
    [InlineData("ivan@")]
    [InlineData("")]
    [InlineData("  John.Doe+news@Example.COM  ")]
    [InlineData("Йван@mail.ru")]
    public void SuggestFromEmail_AlwaysReturnsUsernameThatRequireAccepts(string email)
    {
        var suggested = UsernamePolicy.SuggestFromEmail(email);

        // Политика обязана быть согласована сама с собой: предложенное значение
        // должно приниматься без исключения. Иначе регистрация ломается на ровном
        // месте, и починить её со стороны пользователя невозможно.
        Assert.Equal(suggested, UsernamePolicy.Require(suggested));
        Assert.InRange(suggested.Length, UsernamePolicy.MinLength, UsernamePolicy.MaxLength);
        Assert.All(suggested, symbol => Assert.True(
            IsAllowedSymbol(symbol),
            $"Ник '{suggested}' для email '{email}' содержит недопустимый символ '{symbol}'."));
    }

    [Fact]
    public void SuggestFromEmail_WithOverlongLocalPart_TruncatesToMaximumLength()
    {
        var email = new string('a', UsernamePolicy.MaxLength + 10) + "@x.com";

        var suggested = UsernamePolicy.SuggestFromEmail(email);

        // Длинный local-part обрезается, а не отвергается: иначе владельцы
        // длинных адресов не смогли бы зарегистрироваться вообще.
        Assert.Equal(UsernamePolicy.MaxLength, suggested.Length);
        Assert.Equal(suggested, UsernamePolicy.Require(suggested));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(9999)]
    public void WithSuffix_ForMaximumLengthName_StaysWithinMaxLength(int suffix)
    {
        var suffixText = suffix.ToString(CultureInfo.InvariantCulture);
        var baseName = new string('a', UsernamePolicy.MaxLength);

        var candidate = UsernamePolicy.WithSuffix(baseName, suffix);

        // Хвост нужен, чтобы развести одинаковые ники. Если он вылезет за предел
        // длины, ник не пройдёт Require и второй пользователь не зарегистрируется.
        Assert.InRange(candidate.Length, UsernamePolicy.MinLength, UsernamePolicy.MaxLength);
        Assert.EndsWith(suffixText, candidate, StringComparison.Ordinal);
        Assert.Equal(candidate, UsernamePolicy.Require(candidate));
    }

    [Fact]
    public void WithSuffix_ForShortName_KeepsWholeName() =>
        // Короткий ник не режется: хвост дописывается к нему, а не вместо него,
        // иначе пользователь терял бы выбранное имя.
        Assert.Equal("ivan7", UsernamePolicy.WithSuffix("ivan", 7));

    [Fact]
    public void WithSuffix_DistinguishesCollidingUsernames() =>
        // Ради этого суффикс и существует: одинаковый результат означал бы, что
        // второй «Иван» упрётся в уникальный индекс при регистрации.
        Assert.NotEqual(UsernamePolicy.WithSuffix("ivan", 1), UsernamePolicy.WithSuffix("ivan", 2));

    /// <summary>
    /// Набор символов, объявленный политикой допустимым: латиница, цифры,
    /// дефис и подчёркивание.
    /// </summary>
    private static bool IsAllowedSymbol(char symbol) =>
        symbol is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_';
}
