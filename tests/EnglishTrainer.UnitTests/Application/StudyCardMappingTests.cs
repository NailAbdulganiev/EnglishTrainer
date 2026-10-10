using System.Text.Json;
using EnglishTrainer.Application.Mapping;
using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Enums;

namespace EnglishTrainer.UnitTests.Application;

/// <summary>
/// Проверка преобразования сущностей в DTO.
/// </summary>
/// <remarks>
/// Главное правило контракта: карточка задания не содержит правильного ответа.
/// Если однажды кто-то вернёт в неё английское слово «для удобства клиента»,
/// упражнения потеряют смысл, а заметят это не сразу. Поэтому правило закреплено тестом.
/// </remarks>
public sealed class StudyCardMappingTests
{
    private const string Lemma = "deposit";
    private const string Translation = "залог";

    [Fact]
    public void CardForTypingExercise_DoesNotContainTheAnswer()
    {
        var item = ItemWithWord();

        var card = item.ToStudyCard(
            ExerciseKind.Spelling, Translation, "ru", "/dɪˈpɒzɪt/", null, null, null, Lemma.Length);

        var json = JsonSerializer.Serialize(card);

        // Слово не должно встречаться в сериализованной карточке ни в одном поле.
        Assert.DoesNotContain(Lemma, json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Translation, card.Prompt);
        Assert.Equal("ru", card.PromptLanguage);
        Assert.Equal(Lemma.Length, card.AnswerLength);
    }

    [Fact]
    public void CardForChoiceExercise_ShowsWordAndOffersTranslations()
    {
        var item = ItemWithWord();

        var card = item.ToStudyCard(
            ExerciseKind.MultipleChoice, Lemma, "en", "/dɪˈpɒzɪt/", null, null, [Translation, "аренда"]);

        // В упражнении с выбором вопрос — английское слово, а ответ прячется
        // среди вариантов, поэтому поле с ответом здесь допустимо.
        Assert.Equal(Lemma, card.Prompt);
        Assert.Equal("en", card.PromptLanguage);
        Assert.Equal(2, card.Options.Count);
    }

    [Fact]
    public void Card_CarriesPositionAndExerciseKind()
    {
        var item = ItemWithWord();

        var card = item.ToStudyCard(ExerciseKind.MultipleChoice, Lemma, "en", null, null, null, [Translation, "аренда"]);

        Assert.Equal(item.Id, card.ItemId);
        Assert.Equal(item.Position, card.Position);
        Assert.Equal(item.Kind, card.Kind);
        Assert.Equal(ExerciseKind.MultipleChoice, card.Exercise);
    }

    [Fact]
    public void Card_WithoutLoadedWord_FailsLoudly()
    {
        // Молчаливая подстановка пустого слова маскировала бы забытый Include,
        // и пользователь увидел бы пустую карточку.
        var item = new StudySessionItem { Id = 1, Position = 1, WordId = 10 };

        Assert.Throws<InvalidOperationException>(() =>
            item.ToStudyCard(ExerciseKind.Typing, Translation, "ru", null, null, null));
    }

    [Fact]
    public void WordDetailDto_IncludesTranslationsAndExamples()
    {
        var word = ItemWithWord().Word!;

        var dto = word.ToDetailDto();

        Assert.Equal(Lemma, dto.Lemma);
        Assert.Single(dto.Translations);
        Assert.True(dto.Translations[0].IsPrimary);
        Assert.Single(dto.Examples);
    }

    private static StudySessionItem ItemWithWord()
    {
        var word = new Word
        {
            Id = 7,
            Lemma = Lemma,
            PartOfSpeech = "noun",
            IpaUk = "/dɪˈpɒzɪt/",
            Level = CefrLevel.B1
        };

        word.Translations.Add(new WordTranslation
        {
            Id = 1,
            WordId = word.Id,
            LanguageCode = "ru",
            Text = Translation,
            IsPrimary = true
        });

        word.Examples.Add(new WordExample
        {
            Id = 1,
            WordId = word.Id,
            TextEn = "The landlord asked for a deposit.",
            TextRu = "Арендодатель попросил залог."
        });

        return new StudySessionItem
        {
            Id = 3,
            WordId = word.Id,
            Word = word,
            Position = 1,
            Kind = SessionItemKind.New,
            Exercise = ExerciseKind.Spelling
        };
    }
}
