using EnglishTrainer.Application.Dtos;
using EnglishTrainer.Application.Mapping;
using EnglishTrainer.Domain.Exceptions;
using EnglishTrainer.Domain.Repositories;

namespace EnglishTrainer.Application.Services;

/// <summary>Колоды и слова: то, что пользователь видит до начала занятий.</summary>
public sealed class DeckService(
    IDeckRepository decks,
    IWordRepository words,
    ICardStateRepository cardStates)
{
    public async Task<IReadOnlyList<DeckDto>> GetDecksAsync(int userId, CancellationToken ct = default)
    {
        var all = await decks.GetAllAsync(ct);
        if (all.Count == 0)
            return [];

        // Прогресс считаем по каждому слову колоды. Для больших колод это
        // станет узким местом — тогда сюда придёт кеш, а не «оптимизация вслепую».
        var result = new List<DeckDto>(all.Count);
        foreach (var deck in all.OrderBy(d => d.SortOrder).ThenBy(d => d.Id))
        {
            var wordIds = await words.GetWordIdsInDeckAsync(deck.Id, ct);
            var states = await cardStates.GetForWordsAsync(userId, wordIds, ct);

            result.Add(new DeckDto(
                deck.Id,
                deck.Slug,
                deck.Title,
                deck.Description,
                wordIds.Count,
                states.Count,
                states.Count(s => s.IsLearned)));
        }

        return result;
    }

    public async Task<DeckDto> GetDeckAsync(int userId, string slug, CancellationToken ct = default)
    {
        var deck = await decks.GetBySlugAsync(slug, ct)
            ?? throw NotFoundException.For("Колода", slug);

        var wordIds = await words.GetWordIdsInDeckAsync(deck.Id, ct);
        var states = await cardStates.GetForWordsAsync(userId, wordIds, ct);

        return new DeckDto(
            deck.Id,
            deck.Slug,
            deck.Title,
            deck.Description,
            wordIds.Count,
            states.Count,
            states.Count(s => s.IsLearned));
    }

    /// <summary>Слова колоды со статусом изучения — для просмотра «что внутри».</summary>
    public async Task<IReadOnlyList<WordDto>> GetDeckWordsAsync(
        int userId, string slug, CancellationToken ct = default)
    {
        var deck = await decks.GetBySlugAsync(slug, ct)
            ?? throw NotFoundException.For("Колода", slug);

        var list = await words.GetByDeckAsync(deck.Id, includeDetails: false, ct);
        return list.Select(w => w.ToDto()).ToList();
    }

    public async Task<WordDetailDto> GetWordAsync(int wordId, CancellationToken ct = default)
    {
        var word = await words.GetWithDetailsAsync(wordId, ct)
            ?? throw NotFoundException.For("Слово", wordId);

        return word.ToDetailDto();
    }
}
