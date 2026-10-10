using EnglishTrainer.Infrastructure.Security;

namespace EnglishTrainer.UnitTests.Infrastructure;

/// <summary>
/// Проверка хеширования паролей.
/// </summary>
/// <remarks>
/// Ошибка здесь означает либо утечку паролей, либо невозможность войти
/// в собственный аккаунт, поэтому проверяется и то, и другое.
/// </remarks>
public sealed class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Hash_DoesNotStorePasswordInClearText()
    {
        const string password = "SuperSecret-123";

        var hash = _hasher.Hash(password);

        Assert.DoesNotContain(password, hash, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_AcceptsCorrectPassword()
    {
        const string password = "SuperSecret-123";

        var hash = _hasher.Hash(password);

        Assert.True(_hasher.Verify(password, hash));
    }

    [Fact]
    public void Verify_RejectsWrongPassword()
    {
        var hash = _hasher.Hash("SuperSecret-123");

        Assert.False(_hasher.Verify("SuperSecret-124", hash));
    }

    [Fact]
    public void Hash_UsesUniqueSaltForSamePassword()
    {
        const string password = "SuperSecret-123";

        var first = _hasher.Hash(password);
        var second = _hasher.Hash(password);

        // Одинаковые пароли разных людей обязаны давать разные хеши,
        // иначе радужные таблицы снова начинают работать.
        Assert.NotEqual(first, second);
        Assert.True(_hasher.Verify(password, first));
        Assert.True(_hasher.Verify(password, second));
    }

    [Fact]
    public void Hash_RecordsAlgorithmAndIterationCount()
    {
        var hash = _hasher.Hash("SuperSecret-123");

        var parts = hash.Split('$');
        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2-sha256", parts[0]);
        // Число итераций хранится в самой строке: его увеличение не сломает старые хеши.
        Assert.True(int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) >= 100_000);
    }

    [Theory]
    [InlineData("")]
    [InlineData("не-хеш")]
    [InlineData("pbkdf2-sha256$210000$нет-такого-base64$и-тут-нет")]
    [InlineData("md5$1$aaaa$bbbb")]
    [InlineData("pbkdf2-sha256$0$AAAA$BBBB")]
    public void Verify_RejectsMalformedHash(string malformedHash) =>
        Assert.False(_hasher.Verify("SuperSecret-123", malformedHash));

    [Fact]
    public void Hash_RejectsEmptyPassword() =>
        Assert.Throws<ArgumentException>(() => _hasher.Hash(string.Empty));
}
