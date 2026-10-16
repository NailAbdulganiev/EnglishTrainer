using Microsoft.Extensions.Configuration;

namespace EnglishTrainer.Infrastructure.DependencyInjection;

/// <summary>
/// Подключение локальных секретов разработки.
/// </summary>
/// <remarks>
/// <para>
/// У приложения два независимых процесса — API и бот, — и обоим нужен один и тот
/// же токен Telegram. User-secrets решают это только при совпадающем
/// <c>UserSecretsId</c>, но требуют записи в профиль пользователя, что не всегда
/// доступно. Поэтому поддержаны оба способа: user-secrets подхватывает сам хост
/// в режиме Development, а <c>appsettings.Local.json</c> добавляется здесь.
/// </para>
/// <para>
/// Файл ищется рядом с проектом и на уровень выше — в корне решения, откуда его
/// читают и API, и бот. Один файл на оба процесса лучше двух копий одного секрета:
/// копии расходятся, и «бот не работает, а API работает» выясняется полдня.
/// </para>
/// <para>
/// В репозитории файла нет: он перечислен в <c>.gitignore</c>. В развёртывании
/// секреты приходят переменными окружения, которые переопределяют всё остальное.
/// </para>
/// </remarks>
public static class LocalSettings
{
    private const string FileName = "appsettings.Local.json";

    /// <summary>Подключает локальный файл секретов, если он есть. Иначе — ничего.</summary>
    /// <param name="configuration">Куда добавлять источник.</param>
    /// <param name="contentRootPath">Каталог запуска процесса (обычно каталог проекта).</param>
    public static void AddLocalOverrides(ConfigurationManager configuration, string contentRootPath)
    {
        var path = Find(contentRootPath);

        if (path is null)
        {
            return;
        }

        // reloadOnChange: правка токена не требует перезапуска процесса — в отладке
        // это заметная экономия времени.
        configuration.AddJsonFile(path, optional: true, reloadOnChange: true);

        // Переменные окружения добавляются ещё раз — уже после файла. В конфигурации
        // .NET побеждает источник, добавленный позже, а секреты развёртывания приходят
        // именно переменными окружения и обязаны перекрывать локальный файл.
        // Без этой строки файл разработчика оказался бы «сильнее» развёртывания.
        configuration.AddEnvironmentVariables();
    }

    /// <summary>Ищет файл секретов вверх по дереву каталогов, но не бесконечно.</summary>
    private static string? Find(string contentRootPath)
    {
        var directory = new DirectoryInfo(contentRootPath);

        // Шести уровней хватает на оба реальных случая: каталог проекта и корень
        // решения, — а также на запуск собранного приложения из bin/Debug/net10.0.
        // Подниматься выше не нужно: имя файла специфичное, но искать его
        // в чужих каталогах всё равно не следует.
        for (var depth = 0; depth < 6 && directory is not null; depth++)
        {
            var candidate = Path.Combine(directory.FullName, FileName);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
