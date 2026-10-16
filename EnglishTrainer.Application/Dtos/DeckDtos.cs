namespace EnglishTrainer.Application.Dtos;

// ─────────────────────────── Свои слова и колоды ───────────────────────────

/// <summary>Создание личной колоды.</summary>
public sealed record CreateDeckRequestDto(string Title, string? Description);

/// <summary>Слово, которое человек заводит сам.</summary>
/// <param name="PartOfSpeech">
/// Одна из <c>WordPolicy.PartsOfSpeech</c>. Проверяется на сервере, а не только
/// в интерфейсе: список приходит с сервера, но запрос может прийти и мимо него.
/// </param>
public sealed record CreateWordRequestDto(
    string Lemma,
    string Translation,
    string PartOfSpeech,
    string? ExampleEn,
    string? ExampleRu);

/// <summary>Добавление слова в колоду: существующее по идентификатору или новое.</summary>
/// <remarks>
/// Два способа в одном запросе, потому что для человека это одно действие —
/// «добавить слово». Разделять их на два эндпоинта значило бы заставить
/// интерфейс решать, «новое это слово или нет», а он этого не знает: словарь
/// общий, и заводить слово второй раз нельзя.
/// </remarks>
public sealed record AddDeckWordRequestDto(int? WordId, CreateWordRequestDto? Word);

/// <summary>Найденное в словаре слово — для добавления существующего.</summary>
public sealed record WordSearchResultDto(
    int Id,
    string Lemma,
    string PartOfSpeech,
    string PrimaryTranslation);
