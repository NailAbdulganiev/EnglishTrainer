using EnglishTrainer.Application.Dtos;
using EnglishTrainer.Application.Text;
using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Exceptions;
using EnglishTrainer.Domain.Repositories;

namespace EnglishTrainer.Application.Services;

/// <summary>
/// Избранные слова: список, который пользователь ведёт сам.
/// </summary>
/// <remarks>
/// Алгоритм повторений расставляет приоритеты по вероятности забывания, но не знает,
/// какие слова важны человеку прямо сейчас. Избранное — ручное управление этим
/// приоритетом: отмеченные слова идут в начале очереди и доступны отдельной
/// тренировкой вне расписания.
/// </remarks>
public sealed class FavoriteService(
    IWordRepository words,
    IFavoriteWordRepository favorites,
    ICardStateRepository cardStates,
    IUnitOfWork unitOfWork)
{
    public async Task<IReadOnlyList<FavoriteWordDto>> GetAsync(int userId, CancellationToken ct = default)
    {
        var items = await favorites.GetWithWordsAsync(userId, ct);
        if (items.Count == 0)
            return [];

        var wordIds = items.Select(item => item.WordId).ToList();
        var states = await cardStates.GetForWordsAsync(userId, wordIds, ct);
        var statesByWord = states.ToDictionary(state => state.WordId);

        var result = new List<FavoriteWordDto>(items.Count);

        foreach (var item in items)
        {
            var word = item.Word ?? throw new InvalidOperationException(
                "Слово не загружено вместе с отметкой избранного.");

            statesByWord.TryGetValue(item.WordId, out var state);

            result.Add(new FavoriteWordDto(
                word.Id,
                word.Lemma,
                word.PartOfSpeech,
                word.IpaUk,
                word.IpaUs,
                WordTranslations.Primary(word) ?? string.Empty,
                word.Level,
                state?.Phase,
                state?.IntervalDays,
                state?.DueAt,
                state?.IsLearned ?? false,
                item.CreatedAt));
        }

        return result;
    }

    /// <summary>Идентификаторы избранных слов — интерфейс отмечает их звёздочкой.</summary>
    public Task<IReadOnlyCollection<int>> GetWordIdsAsync(int userId, CancellationToken ct = default) =>
        favorites.GetWordIdsAsync(userId, ct);

    /// <summary>
    /// Ставит или снимает отметку.
    /// </summary>
    /// <remarks>
    /// Операция идемпотентна: повторная установка или снятие не считается ошибкой.
    /// Интерфейс переключает звёздочку одним запросом и не должен думать о том,
    /// в каком состоянии она была до этого.
    /// </remarks>
    public async Task<bool> SetAsync(int userId, int wordId, bool isFavorite, CancellationToken ct = default)
    {
        if (await words.GetByIdAsync(wordId, ct) is null)
            throw NotFoundException.For("Слово", wordId);

        var exists = await favorites.ExistsAsync(userId, wordId, ct);

        if (isFavorite == exists)
            return isFavorite;

        if (isFavorite)
        {
            await favorites.AddAsync(new FavoriteWord { UserId = userId, WordId = wordId }, ct);
        }
        else
        {
            await favorites.RemoveAsync(userId, wordId, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return isFavorite;
    }
}
