using System.Text.RegularExpressions;
using EnglishTrainer.Domain.Abstractions;

namespace EnglishTrainer.Infrastructure.Storage;

/// <summary>
/// Аватары на диске в каталоге, который отдаётся статикой.
/// </summary>
/// <remarks>
/// <para>
/// Каталог задаётся настройкой <c>Storage:AvatarRoot</c>, а по умолчанию лежит
/// внутри <c>wwwroot</c>. Это не мелочь: файл, положенный в <c>wwwroot/avatars</c>,
/// начинает отдаваться тем же <c>UseStaticFiles</c>, что и остальной интерфейс,
/// поэтому отдельный маршрут отдачи не нужен, а браузер получает картинку
/// обычным GET-запросом и умеет её кешировать.
/// </para>
/// <para>
/// Имя файла генерирует сервер (GUID), а не клиент. Это снимает целый класс
/// проблем разом: обход каталогов через «../» в имени, перезапись чужого файла
/// и коллизии при одновременной загрузке.
/// </para>
/// </remarks>
public sealed partial class FileSystemAvatarStorage : IAvatarStorage
{
    private readonly string _root;

    public FileSystemAvatarStorage(string root)
    {
        _root = Path.GetFullPath(root);

        // Каталог создаётся на старте, а не при первой загрузке: так отсутствие
        // прав на запись выясняется при запуске, а не в момент, когда пользователь
        // нажал «Загрузить фото».
        Directory.CreateDirectory(_root);
    }

    /// <summary>Каталог хранения. Нужен приложению, чтобы раздавать его статикой.</summary>
    public string Root => _root;

    public async Task<string> SaveAsync(byte[] content, string extension, CancellationToken ct = default)
    {
        var fileName = $"{Guid.NewGuid():N}{extension}";

        await File.WriteAllBytesAsync(Path.Combine(_root, fileName), content, ct);

        return fileName;
    }

    public Task DeleteAsync(string fileName, CancellationToken ct = default)
    {
        // Имя приходит из базы, то есть когда-то было сгенерировано здесь же.
        // Проверка всё равно нужна: одна строка, исправленная руками в базе,
        // не должна превращаться в удаление произвольного файла на диске.
        if (!SafeName().IsMatch(fileName))
        {
            return Task.CompletedTask;
        }

        var path = Path.Combine(_root, fileName);

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    /// <summary>Ровно то, что порождает <see cref="SaveAsync"/>: 32 hex-символа и известное расширение.</summary>
    [GeneratedRegex(@"^[0-9a-f]{32}\.(jpg|png|webp)$")]
    private static partial Regex SafeName();
}
