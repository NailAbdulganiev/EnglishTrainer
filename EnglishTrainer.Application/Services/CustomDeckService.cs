using System.Security.Cryptography;
using EnglishTrainer.Application.Dtos;
using EnglishTrainer.Application.Mapping;
using EnglishTrainer.Application.Text;
using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Exceptions;
using EnglishTrainer.Domain.Repositories;

namespace EnglishTrainer.Application.Services;

/// <summary>
/// Свои слова и колоды: то, что человек добавляет сам.
/// </summary>
/// <remarks>
/// <para>
/// Личная колода — обычная колода с владельцем. Отдельной сущности нет
/// намеренно: прогресс уже привязан к паре «пользователь + слово», а не к колоде,
/// поэтому личная колода работает всеми теми же механизмами — сессией,
/// расписанием, статистикой, — и ни одного нового не требует.
/// </para>
/// <para>
/// Слова живут в общем справочнике. Это не мелочь: добавив «landscape» в свою
/// колоду, человек не создаёт второе слово, а связывает существующее. Иначе
/// одно и то же слово училось бы дважды независимо, и статистика стала бы
/// неправдой — ровно та причина, по которой прогресс и привязан к слову.
/// </para>
/// </remarks>
public sealed class CustomDeckService(
    IDeckRepository decks,
    IWordRepository words,
    DeckService deckService,
    IUnitOfWork unitOfWork)
{
    /// <summary>Сколько слов возвращать в поиске по словарю.</summary>
    private const int SearchLimit = 20;

    /// <summary>Сколько раз пытаться подобрать свободный адрес колоды.</summary>
    private const int SlugAttempts = 5;

    public async Task<DeckDto> CreateAsync(
        int userId, CreateDeckRequestDto request, CancellationToken ct = default)
    {
        var deck = new Deck
        {
            Slug = await ReserveSlugAsync(ct),
            Title = DeckPolicy.RequireTitle(request.Title),
            Description = DeckPolicy.NormalizeDescription(request.Description),
            IsSystem = false,
            OwnerId = userId
        };

        await decks.AddAsync(deck, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return await deckService.GetDeckAsync(userId, deck.Slug, ct);
    }

    public async Task DeleteAsync(int userId, string slug, CancellationToken ct = default)
    {
        var deck = await RequireEditableAsync(userId, slug, ct);

        await decks.RemoveAsync(deck, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Добавляет в личную колоду новое слово или уже существующее.
    /// </summary>
    public async Task<DeckDto> AddWordAsync(
        int userId, string slug, AddDeckWordRequestDto request, CancellationToken ct = default)
    {
        var deck = await RequireEditableAsync(userId, slug, ct);
        var word = await ResolveWordAsync(request, ct);

        if (await decks.GetWordLinkAsync(deck.Id, word.Id, ct) is not null)
        {
            throw new ConflictException($"«{word.Lemma}» уже есть в этой колоде.");
        }

        await decks.AddWordAsync(new DeckWord
        {
            DeckId = deck.Id,
            WordId = word.Id,
            // Новое слово идёт в конец: человек добавляет его осознанно и ждёт
            // увидеть там же, а не в середине списка.
            Order = await decks.NextWordOrderAsync(deck.Id, ct)
        }, ct);

        await unitOfWork.SaveChangesAsync(ct);

        return await deckService.GetDeckAsync(userId, deck.Slug, ct);
    }

    public async Task<DeckDto> RemoveWordAsync(
        int userId, string slug, int wordId, CancellationToken ct = default)
    {
        var deck = await RequireEditableAsync(userId, slug, ct);

        var link = await decks.GetWordLinkAsync(deck.Id, wordId, ct)
            ?? throw new NotFoundException($"Слова с идентификатором '{wordId}' нет в этой колоде.");

        await decks.RemoveWordAsync(link, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return await deckService.GetDeckAsync(userId, deck.Slug, ct);
    }

    /// <summary>Поиск по общему словарю — чтобы добавить существующее слово, а не завести дубль.</summary>
    public async Task<IReadOnlyList<WordSearchResultDto>> SearchWordsAsync(
        string? query, CancellationToken ct = default)
    {
        var found = await words.SearchAsync(query ?? string.Empty, SearchLimit, ct);

        return [.. found.Select(word => new WordSearchResultDto(
            word.Id,
            word.Lemma,
            word.PartOfSpeech,
            WordTranslations.Primary(word) ?? string.Empty))];
    }

    /// <summary>
    /// Колода, которую этому пользователю разрешено править.
    /// </summary>
    /// <remarks>
    /// Чужая и системная колода дают 404, а не 403. Разница существенна:
    /// 403 подтверждает, что колода существует, и перебором адресов можно
    /// выяснить, какие личные колоды есть у других людей. Отказ «не найдено»
    /// не сообщает ничего.
    /// </remarks>
    private async Task<Deck> RequireEditableAsync(int userId, string slug, CancellationToken ct)
    {
        var deck = await decks.GetBySlugAsync(slug, ct)
            ?? throw NotFoundException.For("Колода", slug);

        if (!deck.IsEditableBy(userId))
        {
            throw NotFoundException.For("Колода", slug);
        }

        return deck;
    }

    private async Task<Word> ResolveWordAsync(AddDeckWordRequestDto request, CancellationToken ct)
    {
        if (request.WordId is { } wordId)
        {
            return await words.GetByIdAsync(wordId, ct)
                ?? throw NotFoundException.For("Слово", wordId);
        }

        if (request.Word is null)
        {
            throw new ValidationException(
                "Укажите слово: существующее (wordId) или новое (word).");
        }

        var lemma = WordPolicy.RequireLemma(request.Word.Lemma);
        var partOfSpeech = WordPolicy.RequirePartOfSpeech(request.Word.PartOfSpeech);
        var translation = WordPolicy.RequireTranslation(request.Word.Translation);

        // Слово с таким написанием и частью речи уже может быть в словаре.
        // Тогда оно переиспользуется как есть: словарь общий, и правка его
        // значений от имени одного человека меняла бы упражнения всем
        // остальным — чужая неверная формулировка стала бы засчитываться
        // как правильный ответ.
        var existing = await words.GetByLemmaAsync(lemma, partOfSpeech, ct);

        if (existing is not null)
        {
            return existing;
        }

        var word = new Word
        {
            Lemma = lemma,
            PartOfSpeech = partOfSpeech,
            Translations =
            {
                new WordTranslation
                {
                    LanguageCode = "ru",
                    Text = translation,
                    IsPrimary = true
                }
            }
        };

        var exampleEn = WordPolicy.NormalizeExample(request.Word.ExampleEn);
        var exampleRu = WordPolicy.NormalizeExample(request.Word.ExampleRu);

        if (exampleEn is not null || exampleRu is not null)
        {
            word.Examples.Add(new WordExample
            {
                TextEn = exampleEn ?? translation,
                TextRu = exampleRu
            });
        }

        await words.AddAsync(word, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return word;
    }

    /// <summary>
    /// Подбирает свободный адрес колоды.
    /// </summary>
    /// <remarks>
    /// Случайный хвост, а не транслитерация названия. Названия люди пишут
    /// по-русски, и «Моя колода» превратилось бы в «moya-koloda» — с таблицей
    /// транслитерации, разбором коллизий и всё равно непредсказуемым
    /// результатом. Адрес виден только в запросах, а человек работает
    /// с названием.
    /// </remarks>
    private async Task<string> ReserveSlugAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < SlugAttempts; attempt++)
        {
            var slug = $"own-{Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant()}";

            if (!await decks.SlugExistsAsync(slug, ct))
            {
                return slug;
            }
        }

        // Пять совпадений подряд на 48 битах — это не «редко», а «сломано»;
        // но падать из-за этого нельзя, поэтому берётся заведомо длинный хвост.
        return $"own-{Guid.NewGuid():N}";
    }
}
