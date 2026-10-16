using EnglishTrainer.Domain.Abstractions;
using EnglishTrainer.Domain.Exceptions;

namespace EnglishTrainer.Application.Media;

/// <summary>Проверенное изображение, готовое к записи в хранилище.</summary>
/// <param name="Content">Байты файла.</param>
/// <param name="Extension">Расширение с точкой, выведенное из содержимого, а не из имени файла.</param>
public sealed record AvatarImage(byte[] Content, string Extension);

/// <summary>
/// Правила приёма аватара.
/// </summary>
/// <remarks>
/// <para>
/// Проверка идёт по <b>содержимому</b>, а не по имени файла и не по заголовку
/// <c>Content-Type</c>: и то и другое присылает клиент, поэтому «avatar.php»
/// с типом <c>image/png</c> прошёл бы такую проверку и лёг бы в каталог,
/// откуда статика отдаётся как есть. Магические байты подделать в пределах
/// допустимого размера нельзя — чтобы пройти проверку, файл должен начинаться
/// как JPEG, PNG или WebP.
/// </para>
/// <para>
/// Расширение берётся из содержимого по той же причине: имя, придуманное
/// клиентом, не должно попадать в файловую систему.
/// </para>
/// </remarks>
public static class AvatarPolicy
{
    /// <summary>
    /// Предельный размер файла.
    /// </summary>
    /// <remarks>
    /// 2 МиБ — это с запасом больше, чем нужно аватару, который показывается
    /// кружком в 40 пикселей, но достаточно мало, чтобы потоковая запись
    /// на диск не превращалась в способ занять место на сервере.
    /// </remarks>
    public const long MaxBytes = 2 * 1024 * 1024;

    /// <summary>Значение атрибута accept для поля выбора файла.</summary>
    public const string AcceptedContentTypes = "image/jpeg,image/png,image/webp";

    /// <summary>
    /// Читает и проверяет загруженный файл.
    /// </summary>
    /// <exception cref="ValidationException">
    /// Файл пуст, слишком велик или не является изображением поддерживаемого формата.
    /// </exception>
    public static async Task<AvatarImage> ReadAsync(AvatarUpload upload, CancellationToken ct = default)
    {
        if (upload.Length <= 0)
            throw new ValidationException("Файл пуст.");

        if (upload.Length > MaxBytes)
            throw new ValidationException(TooLargeMessage());

        // Читаем на байт больше предела: если клиент соврал в размере, лишний байт
        // обнаружится здесь, а не после того, как файл целиком окажется в памяти.
        using var buffer = new MemoryStream();
        await using (var source = upload.Content)
        {
            await source.CopyToAsync(buffer, ct);
        }

        if (buffer.Length > MaxBytes)
            throw new ValidationException(TooLargeMessage());

        var content = buffer.ToArray();
        var extension = DetectExtension(content)
            ?? throw new ValidationException("Поддерживаются только изображения JPEG, PNG и WebP.");

        return new AvatarImage(content, extension);
    }

    /// <summary>
    /// Определяет формат по первым байтам. <c>null</c> — формат не поддерживается.
    /// </summary>
    /// <remarks>
    /// Публичный, потому что это единственное место, где закодировано знание
    /// о сигнатурах форматов, и его нужно проверять тестом напрямую.
    /// </remarks>
    public static string? DetectExtension(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
            return ".jpg";

        if (content.Length >= 8 &&
            content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47 &&
            content[4] == 0x0D && content[5] == 0x0A && content[6] == 0x1A && content[7] == 0x0A)
            return ".png";

        // WebP — это контейнер RIFF: сигнатура разорвана четырьмя байтами длины.
        if (content.Length >= 12 &&
            content[0] == (byte)'R' && content[1] == (byte)'I' && content[2] == (byte)'F' && content[3] == (byte)'F' &&
            content[8] == (byte)'W' && content[9] == (byte)'E' && content[10] == (byte)'B' && content[11] == (byte)'P')
            return ".webp";

        return null;
    }

    private static string TooLargeMessage() =>
        $"Файл больше {MaxBytes / (1024 * 1024)} МиБ.";
}
