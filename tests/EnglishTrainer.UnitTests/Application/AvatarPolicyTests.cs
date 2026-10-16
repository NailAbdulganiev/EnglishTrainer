using System.Text;
using EnglishTrainer.Application.Media;
using EnglishTrainer.Domain.Abstractions;
using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Exceptions;

namespace EnglishTrainer.UnitTests.Application;

/// <summary>
/// Проверка правил приёма аватара.
/// </summary>
/// <remarks>
/// <para>
/// Каталог аватаров раздаётся статикой, поэтому проверка формата — это не
/// косметика, а граница безопасности: файл, прошедший её, станет доступен
/// по прямой ссылке. Отсюда и главный случай в наборе: файл с расширением
/// картинки и заголовком <c>image/png</c>, который картинкой не является.
/// </para>
/// <para>
/// Проверяется и граница размера: ровно предел принимается, на байт больше —
/// нет. Ошибка «на единицу» здесь означала бы либо отказ валидному файлу,
/// либо пропуск лишнего.
/// </para>
/// </remarks>
public sealed class AvatarPolicyTests
{
    /// <summary>Начало настоящего PNG: сигнатура из восьми байт.</summary>
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF, 0xE0];

    private static readonly byte[] WebpSignature =
        [(byte)'R', (byte)'I', (byte)'F', (byte)'F', 0x20, 0x00, 0x00, 0x00, (byte)'W', (byte)'E', (byte)'B', (byte)'P'];

    [Fact]
    public async Task ReadAsync_WithJpeg_ReturnsJpgExtension()
    {
        var image = await AvatarPolicy.ReadAsync(Upload(JpegSignature));

        Assert.Equal(".jpg", image.Extension);
        Assert.Equal(JpegSignature, image.Content);
    }

    [Fact]
    public async Task ReadAsync_WithPng_ReturnsPngExtension()
    {
        var image = await AvatarPolicy.ReadAsync(Upload(PngSignature));

        Assert.Equal(".png", image.Extension);
    }

    [Fact]
    public async Task ReadAsync_WithWebp_ReturnsWebpExtension()
    {
        // WebP — контейнер RIFF: сигнатура разорвана четырьмя байтами длины,
        // поэтому проверка «по первым четырём байтам» его бы не поймала.
        var image = await AvatarPolicy.ReadAsync(Upload(WebpSignature));

        Assert.Equal(".webp", image.Extension);
    }

    [Fact]
    public async Task ReadAsync_WithEmptyFile_ThrowsValidationException() =>
        await Assert.ThrowsAsync<ValidationException>(
            () => AvatarPolicy.ReadAsync(Upload([])));

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>")]
    [InlineData("<?php echo shell_exec($_GET['c']); ?>")]
    [InlineData("GIF89a")]
    [InlineData("RIFF____WAVE")]
    public async Task ReadAsync_WithContentThatOnlyPretendsToBeAnImage_ThrowsValidationException(string content)
    {
        // Именно этот случай защищает каталог статики: содержимое решает,
        // а не имя файла и не заголовок Content-Type, которые присылает клиент.
        var upload = Upload(Encoding.UTF8.GetBytes(content), contentType: "image/png");

        await Assert.ThrowsAsync<ValidationException>(() => AvatarPolicy.ReadAsync(upload));
    }

    [Fact]
    public async Task ReadAsync_TrustsContentAndIgnoresDeclaredContentType()
    {
        // Объявленный тип не участвует в решении: клиент может не указать его
        // вовсе или указать неверно, а картинка от этого картинкой быть не перестаёт.
        var image = await AvatarPolicy.ReadAsync(Upload(PngSignature, contentType: "application/octet-stream"));

        Assert.Equal(".png", image.Extension);
    }

    [Fact]
    public async Task ReadAsync_WithFileExactlyAtLimit_AcceptsIt()
    {
        var content = new byte[AvatarPolicy.MaxBytes];
        PngSignature.CopyTo(content, 0);

        var image = await AvatarPolicy.ReadAsync(Upload(content));

        Assert.Equal(".png", image.Extension);
    }

    [Fact]
    public async Task ReadAsync_WithFileOneByteOverLimit_ThrowsValidationException()
    {
        var content = new byte[AvatarPolicy.MaxBytes + 1];
        PngSignature.CopyTo(content, 0);

        await Assert.ThrowsAsync<ValidationException>(() => AvatarPolicy.ReadAsync(Upload(content)));
    }

    [Fact]
    public async Task ReadAsync_WithUnderstatedLength_StillRejectsOversizedFile()
    {
        // Клиент волен соврать в размере: тело запроса проверяет конвейер,
        // но полагаться только на объявленное значение нельзя.
        var content = new byte[AvatarPolicy.MaxBytes + 1];
        PngSignature.CopyTo(content, 0);

        var upload = new AvatarUpload(new MemoryStream(content), "image/png", Length: 10);

        await Assert.ThrowsAsync<ValidationException>(() => AvatarPolicy.ReadAsync(upload));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0xFF })]
    [InlineData(new byte[] { 0xFF, 0xD8 })]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47 })]
    public void DetectExtension_WithTruncatedSignature_ReturnsNull(byte[] content) =>
        // Обрезанный файл не должен опознаваться: проверка «начинается с FF D8»
        // пропустила бы два байта мусора как JPEG.
        Assert.Null(AvatarPolicy.DetectExtension(content));

    [Fact]
    public void DetectExtension_WithWebpMissingFourCC_ReturnsNull()
    {
        // RIFF без «WEBP» на своём месте — это WAV или AVI, а не картинка.
        byte[] riff = [(byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'A', (byte)'V', (byte)'E'];

        Assert.Null(AvatarPolicy.DetectExtension(riff));
    }

    [Fact]
    public void AcceptedContentTypes_MatchWhatThePolicyRecognizes()
    {
        // Список для поля выбора файла и список распознаваемых форматов обязаны
        // совпадать: иначе пользователь выберет файл, который браузер предложил,
        // а сервер его отклонит.
        var accepted = AvatarPolicy.AcceptedContentTypes.Split(',');

        Assert.Equal(new[] { "image/jpeg", "image/png", "image/webp" }, accepted);
    }

    private static AvatarUpload Upload(byte[] content, string? contentType = "image/png") =>
        new(new MemoryStream(content), contentType, content.Length);
}

/// <summary>
/// Адрес аватара, который видит клиент.
/// </summary>
/// <remarks>
/// Свойство вычисляемое, и это не деталь: адрес не хранится в базе, поэтому
/// смена способа отдачи не требует миграции. Проверяется и то, что без файла
/// адреса нет вовсе, — интерфейс по этому признаку рисует инициалы.
/// </remarks>
public sealed class UserAvatarUrlTests
{
    [Fact]
    public void AvatarUrl_WithoutFile_IsNull() =>
        Assert.Null(new User { Username = "ivan" }.AvatarUrl);

    [Fact]
    public void AvatarUrl_WithEmptyFileName_IsNull() =>
        // Пустая строка — это тоже «фото нет»: она приходит из старых строк
        // или из ручной правки базы, и превращать её в ссылку «/avatars/» нельзя.
        Assert.Null(new User { AvatarFileName = string.Empty }.AvatarUrl);

    [Fact]
    public void AvatarUrl_WithFile_IsBuiltFromFileName()
    {
        var user = new User { AvatarFileName = "0123456789abcdef0123456789abcdef.png" };

        Assert.Equal("/avatars/0123456789abcdef0123456789abcdef.png", user.AvatarUrl);
    }
}
