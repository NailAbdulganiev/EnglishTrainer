using EnglishTrainer.Application.Dtos;
using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Enums;

namespace EnglishTrainer.Application.Mapping;

/// <summary>
/// Преобразование доменных сущностей в DTO.
/// </summary>
/// <remarks>
/// Отдельный класс, а не методы в сервисах: правила «что именно уходит наружу»
/// должны быть в одном месте. Здесь же проходит защита от утечки ответов —
/// <see cref="ToStudyCard"/> намеренно не переносит перевод.
/// </remarks>
public static class DtoMapper
{
    public static UserDto ToDto(this User user) => new(
        user.PublicId,
        user.Email,
        user.Username,
        user.DisplayName,
        user.AvatarUrl,
        user.TimeZoneId,
        user.CreatedAt,
        user.TelegramId.HasValue);

    public static UserSettingsDto ToDto(this UserSettings settings) => new(
        settings.NewWordsPerDay,
        settings.MaxCardsPerSession,
        settings.DailyGoal,
        settings.DailyReminderEnabled,
        settings.ReminderTime,
        settings.ShowTranscriptionUpfront);

    public static WordTranslationDto ToDto(this WordTranslation translation) =>
        new(translation.Text, translation.Sense, translation.IsPrimary);

    public static WordExampleDto ToDto(this WordExample example) =>
        new(example.TextEn, example.TextRu, example.AudioUrl, example.Attribution);

    public static WordDto ToDto(this Word word) => new(
        word.Id,
        word.Lemma,
        word.PartOfSpeech,
        word.IpaUk,
        word.IpaUs,
        word.Examples.Select(e => e.AudioUrl).FirstOrDefault(url => url is not null),
        word.Level,
        word.Translations
            .OrderByDescending(t => t.IsPrimary)
            .Select(t => t.ToDto())
            .ToList());

    public static WordDetailDto ToDetailDto(this Word word) => new(
        word.Id,
        word.Lemma,
        word.PartOfSpeech,
        word.IpaUk,
        word.IpaUs,
        word.FrequencyRank,
        word.Level,
        word.Translations
            .OrderByDescending(t => t.IsPrimary)
            .Select(t => t.ToDto())
            .ToList(),
        word.Examples.Select(e => e.ToDto()).ToList());

    /// <summary>
    /// Карточка для показа.
    /// </summary>
    /// <remarks>
    /// Перевод сюда не попадает осознанно: ответ проверяется на сервере, иначе
    /// пользователь увидит его до своей попытки. Направление задания задаётся
    /// аргументами <paramref name="prompt"/> и <paramref name="promptLanguage"/>:
    /// при выборе варианта вопрос — английское слово, при вводе — перевод.
    /// </remarks>
    /// <param name="spokenText">
    /// Текст для озвучивания браузером. Заполняется только у упражнения на слух:
    /// в остальных оно было бы подсказкой — клиент произнёс бы ответ вслух
    /// до попытки.
    /// </param>
    public static StudyCardDto ToStudyCard(
        this StudySessionItem item,
        ExerciseKind exercise,
        string prompt,
        string promptLanguage,
        string? ipaUk,
        string? ipaUs,
        string? audioUrl,
        IReadOnlyList<string>? options = null,
        int? answerLength = null,
        string? spokenText = null)
    {
        var word = item.Word ?? throw new InvalidOperationException(
            "Слово не загружено вместе с элементом сессии. Нужен Include(x => x.Word).");

        return new StudyCardDto(
            item.Id,
            item.Position,
            word.Id,
            exercise,
            item.Kind,
            prompt,
            promptLanguage,
            word.PartOfSpeech,
            ipaUk,
            ipaUs,
            audioUrl,
            options ?? [],
            answerLength,
            spokenText);
    }
}
