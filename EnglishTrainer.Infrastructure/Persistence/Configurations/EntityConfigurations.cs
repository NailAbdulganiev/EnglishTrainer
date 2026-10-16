using EnglishTrainer.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishTrainer.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.PublicId).IsRequired();
        builder.HasIndex(u => u.PublicId).IsUnique();

        // Email хранится нормализованным (нижний регистр), поэтому уникальный
        // индекс делает то же, что и проверка в коде, но ещё и на уровне БД.
        builder.Property(u => u.Email).IsRequired().HasMaxLength(320);
        builder.HasIndex(u => u.Email).IsUnique();

        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(256);
        builder.Property(u => u.DisplayName).IsRequired().HasMaxLength(64);

        // Ник — это адрес, по которому пользователя находят другие: по нему
        // отправляют заявку в друзья, поэтому он уникален. Хранится в нижнем
        // регистре, чтобы «Ivan» и «ivan» не оказались разными людьми.
        builder.Property(u => u.Username).IsRequired().HasMaxLength(32);
        builder.HasIndex(u => u.Username).IsUnique();

        builder.Property(u => u.TimeZoneId).IsRequired().HasMaxLength(64).HasDefaultValue("UTC");

        // Имя файла аватара. Необязательное: у большинства пользователей фото нет,
        // и заставлять их загружать картинку при регистрации незачем.
        builder.Property(u => u.AvatarFileName).HasMaxLength(64);

        // Адрес аватара — вычисляемое свойство, в базе ему делать нечего.
        builder.Ignore(u => u.AvatarUrl);

        builder.Property(u => u.CreatedAt).IsRequired();

        // TelegramId уникален, но допускает NULL (пользователь без бота).
        // Фильтр записан именем столбца в базе: конвенция приводит имена к snake_case.
        builder.HasIndex(u => u.TelegramId).IsUnique().HasFilter("\"telegram_id\" IS NOT NULL");

        builder.HasOne(u => u.Settings)
            .WithOne(s => s.User!)
            .HasForeignKey<UserSettings>(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class UserSettingsConfiguration : IEntityTypeConfiguration<UserSettings>
{
    public void Configure(EntityTypeBuilder<UserSettings> builder)
    {
        builder.ToTable("user_settings");
        builder.HasKey(s => s.Id);

        // Ключ совпадает с ключом пользователя: настройки строго «один к одному».
        builder.HasIndex(s => s.UserId).IsUnique();

        builder.Property(s => s.NewWordsPerDay).HasDefaultValue(10);
        builder.Property(s => s.MaxCardsPerSession).HasDefaultValue(60);
        builder.Property(s => s.DailyGoal).HasDefaultValue(10);
        builder.Property(s => s.DailyReminderEnabled).HasDefaultValue(true);
        builder.Property(s => s.ShowTranscriptionUpfront).HasDefaultValue(true);

        // time без зоны: напоминание задаётся в локальном времени пользователя,
        // а не в UTC, иначе при смене пояса оно «уезжает».
        builder.Property(s => s.ReminderTime).HasColumnType("time without time zone");

        // Момент следующего напоминания — он-то как раз в UTC, потому что по нему
        // идёт выборка «кому пора», а она не должна зависеть от пояса сервера.
        // Индекс нужен именно под этот запрос: планировщик просыпается каждую
        // минуту и не должен читать таблицу настроек целиком.
        builder.HasIndex(s => s.NextReminderAtUtc);
    }
}

public sealed class UserAchievementConfiguration : IEntityTypeConfiguration<UserAchievement>
{
    public void Configure(EntityTypeBuilder<UserAchievement> builder)
    {
        builder.ToTable("user_achievements");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Code).IsRequired().HasMaxLength(64);

        // Достижение выдаётся один раз. Гарантия на уровне базы, а не проверкой
        // в коде: выдача идёт после каждого ответа, и два одновременных запроса
        // без этого индекса выдали бы награду дважды.
        builder.HasIndex(a => new { a.UserId, a.Code }).IsUnique();

        builder.HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TelegramLinkCodeConfiguration : IEntityTypeConfiguration<TelegramLinkCode>
{
    public void Configure(EntityTypeBuilder<TelegramLinkCode> builder)
    {
        builder.ToTable("telegram_link_codes");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.CodeHash).IsRequired().HasMaxLength(64);

        // Поиск идёт строго по хешу кода, поэтому индекс уникальный: совпадение
        // хешей означало бы, что два человека получили один и тот же код.
        builder.HasIndex(c => c.CodeHash).IsUnique();

        // Погашение действующих кодов пользователя при новой привязке.
        builder.HasIndex(c => new { c.UserId, c.ExpiresAt });

        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class WordConfiguration : IEntityTypeConfiguration<Word>
{
    public void Configure(EntityTypeBuilder<Word> builder)
    {
        builder.ToTable("words");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Lemma).IsRequired().HasMaxLength(64);
        builder.Property(w => w.PartOfSpeech).IsRequired().HasMaxLength(16);
        builder.Property(w => w.IpaUk).HasMaxLength(96);
        builder.Property(w => w.IpaUs).HasMaxLength(96);
        builder.Property(w => w.Level).HasConversion<int>();

        // Одно и то же написание бывает разными частями речи («work» — и глагол,
        // и существительное), поэтому уникальность по паре, а не по слову.
        builder.HasIndex(w => new { w.Lemma, w.PartOfSpeech }).IsUnique();

        // По этому индексу идёт отбор самых полезных слов для новых карточек.
        builder.HasIndex(w => w.FrequencyRank);

        builder.HasMany(w => w.Translations)
            .WithOne(t => t.Word!)
            .HasForeignKey(t => t.WordId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(w => w.Examples)
            .WithOne(e => e.Word!)
            .HasForeignKey(e => e.WordId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class WordTranslationConfiguration : IEntityTypeConfiguration<WordTranslation>
{
    public void Configure(EntityTypeBuilder<WordTranslation> builder)
    {
        builder.ToTable("word_translations");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.LanguageCode).IsRequired().HasMaxLength(8);
        builder.Property(t => t.Text).IsRequired().HasMaxLength(128);
        builder.Property(t => t.Sense).HasMaxLength(96);

        builder.HasIndex(t => new { t.WordId, t.LanguageCode });
    }
}

public sealed class WordExampleConfiguration : IEntityTypeConfiguration<WordExample>
{
    public void Configure(EntityTypeBuilder<WordExample> builder)
    {
        builder.ToTable("word_examples");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TextEn).IsRequired().HasMaxLength(512);
        builder.Property(e => e.TextRu).HasMaxLength(512);
        builder.Property(e => e.AudioUrl).HasMaxLength(512);
        builder.Property(e => e.Attribution).HasMaxLength(256);
    }
}

public sealed class DeckConfiguration : IEntityTypeConfiguration<Deck>
{
    public void Configure(EntityTypeBuilder<Deck> builder)
    {
        builder.ToTable("decks");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Slug).IsRequired().HasMaxLength(64);
        builder.HasIndex(d => d.Slug).IsUnique();

        builder.Property(d => d.Title).IsRequired().HasMaxLength(128);
        builder.Property(d => d.Description).HasMaxLength(512);
        builder.Property(d => d.IsSystem).HasDefaultValue(true);

        // Личные колоды ищутся по владельцу: список колод для пользователя
        // собирается как «системные плюс мои», и без индекса это чтение всей
        // таблицы на каждое открытие страницы.
        builder.HasIndex(d => d.OwnerId);

        builder.HasOne(d => d.Owner)
            .WithMany()
            .HasForeignKey(d => d.OwnerId)
            // Удаление аккаунта уносит его личные колоды, но не слова: слова
            // живут в общем справочнике и могут использоваться в других колодах.
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DeckWordConfiguration : IEntityTypeConfiguration<DeckWord>
{
    public void Configure(EntityTypeBuilder<DeckWord> builder)
    {
        builder.ToTable("deck_words");
        builder.HasKey(dw => dw.Id);

        // Слово не может лежать в одной колоде дважды.
        builder.HasIndex(dw => new { dw.DeckId, dw.WordId }).IsUnique();

        builder.HasOne(dw => dw.Deck)
            .WithMany(d => d.Words)
            .HasForeignKey(dw => dw.DeckId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(dw => dw.Word)
            .WithMany(w => w.Decks)
            .HasForeignKey(dw => dw.WordId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CardStateConfiguration : IEntityTypeConfiguration<CardState>
{
    public void Configure(EntityTypeBuilder<CardState> builder)
    {
        builder.ToTable("card_states");
        builder.HasKey(c => c.Id);

        // Главная гарантия целостности: одно состояние на пару (пользователь, слово).
        // Защищает от дублей при гонках, когда два запроса одновременно
        // решают, что слово новое.
        builder.HasIndex(c => new { c.UserId, c.WordId }).IsUnique();

        // Самый горячий запрос системы: «что пора повторить».
        // Составной индекс именно в этом порядке: сначала равенство по UserId,
        // затем диапазон по DueAt.
        builder.HasIndex(c => new { c.UserId, c.DueAt });

        builder.Property(c => c.Phase).HasConversion<int>();
        builder.Property(c => c.Stability).HasDefaultValue(0d);
        builder.Property(c => c.Difficulty).HasDefaultValue(0d);
        builder.Property(c => c.IntervalDays).HasDefaultValue(0d);

        // Вычисляемое свойство: в БД не хранится.
        builder.Ignore(c => c.IsLearned);

        builder.HasOne(c => c.User)
            .WithMany(u => u.CardStates)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Word)
            .WithMany()
            .HasForeignKey(c => c.WordId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ReviewLogConfiguration : IEntityTypeConfiguration<ReviewLog>
{
    public void Configure(EntityTypeBuilder<ReviewLog> builder)
    {
        builder.ToTable("review_logs");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Rating).HasConversion<int>();
        builder.Property(l => l.PhaseBefore).HasConversion<int>();
        builder.Property(l => l.Exercise).HasConversion<int>();

        // Журнал растёт быстрее всех таблиц, поэтому индексы под два реальных
        // запроса: история по слову и кривая удержания за период.
        builder.HasIndex(l => new { l.UserId, l.ReviewedAt });
        builder.HasIndex(l => new { l.CardStateId, l.ReviewedAt });

        builder.HasOne(l => l.CardState)
            .WithMany()
            .HasForeignKey(l => l.CardStateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class StudySessionConfiguration : IEntityTypeConfiguration<StudySession>
{
    public void Configure(EntityTypeBuilder<StudySession> builder)
    {
        builder.ToTable("study_sessions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Status).HasConversion<int>();

        // У пользователя может быть только одна активная сессия — гарантия на уровне БД.
        // Проверка в коде не спасёт от гонки двух одновременных запросов.
        // Условие фильтра — имя столбца в базе (snake_case), а не имя свойства.
        builder.HasIndex(s => s.UserId)
            .IsUnique()
            .HasFilter("\"status\" = 0")
            .HasDatabaseName("ix_study_sessions_active_per_user");

        builder.HasIndex(s => new { s.UserId, s.LocalDate });

        builder.Ignore(s => s.IsFinished);

        builder.HasOne(s => s.User)
            .WithMany(u => u.Sessions)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Deck)
            .WithMany()
            .HasForeignKey(s => s.DeckId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(s => s.Items)
            .WithOne(i => i.StudySession!)
            .HasForeignKey(i => i.StudySessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class StudySessionItemConfiguration : IEntityTypeConfiguration<StudySessionItem>
{
    public void Configure(EntityTypeBuilder<StudySessionItem> builder)
    {
        builder.ToTable("study_session_items");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Kind).HasConversion<int>();
        builder.Property(i => i.Exercise).HasConversion<int>();
        builder.Property(i => i.Rating).HasConversion<int?>();

        builder.HasIndex(i => new { i.StudySessionId, i.Position });

        builder.HasOne(i => i.Word)
            .WithMany()
            .HasForeignKey(i => i.WordId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.CardState)
            .WithMany()
            .HasForeignKey(i => i.CardStateId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class DailyStatConfiguration : IEntityTypeConfiguration<DailyStat>
{
    public void Configure(EntityTypeBuilder<DailyStat> builder)
    {
        builder.ToTable("daily_stats");
        builder.HasKey(d => d.Id);

        builder.HasIndex(d => new { d.UserId, d.Date }).IsUnique();

        builder.Ignore(d => d.Accuracy);

        builder.HasOne(d => d.User)
            .WithMany(u => u.DailyStats)
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class FavoriteWordConfiguration : IEntityTypeConfiguration<FavoriteWord>
{
    public void Configure(EntityTypeBuilder<FavoriteWord> builder)
    {
        builder.ToTable("favorite_words");
        builder.HasKey(f => f.Id);

        // Слово нельзя добавить в избранное дважды: пометка должна быть
        // идемпотентной и не плодить дубли при двойном нажатии звёздочки.
        builder.HasIndex(f => new { f.UserId, f.WordId }).IsUnique();

        builder.HasOne(f => f.User)
            .WithMany(u => u.Favorites)
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(f => f.Word)
            .WithMany()
            .HasForeignKey(f => f.WordId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    public void Configure(EntityTypeBuilder<Friendship> builder)
    {
        builder.ToTable("friendships");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.Status).HasConversion<int>();

        // Повторная заявка от того же человека невозможна. Обратное направление
        // этим индексом не ловится: (A,B) и (B,A) — разные строки, поэтому
        // встречная заявка проверяется в сервисе.
        builder.HasIndex(f => new { f.RequesterId, f.AddresseeId }).IsUnique();

        // Входящие и исходящие заявки — два самых частых запроса страницы друзей.
        builder.HasIndex(f => new { f.AddresseeId, f.Status });
        builder.HasIndex(f => new { f.RequesterId, f.Status });

        builder.Ignore(f => f.IsActive);

        builder.HasOne(f => f.Requester)
            .WithMany()
            .HasForeignKey(f => f.RequesterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(f => f.Addressee)
            .WithMany()
            .HasForeignKey(f => f.AddresseeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("password_reset_tokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(64);

        // Поиск идёт строго по хешу токена, поэтому индекс уникальный:
        // совпадение хешей означало бы, что два человека получили одну ссылку.
        builder.HasIndex(t => t.TokenHash).IsUnique();

        // Погашение действующих токенов пользователя при сбросе пароля.
        builder.HasIndex(t => new { t.UserId, t.ExpiresAt });

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
