using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;

namespace EnglishTrainer.Infrastructure.Persistence;

/// <summary>
/// Контекст EF Core. Конфигурация сущностей вынесена в отдельные классы
/// <see cref="IEntityTypeConfiguration{T}"/> — контекст не должен превращаться
/// в свалку из сотен строк маппинга.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<Word> Words => Set<Word>();
    public DbSet<WordTranslation> WordTranslations => Set<WordTranslation>();
    public DbSet<WordExample> WordExamples => Set<WordExample>();
    public DbSet<Deck> Decks => Set<Deck>();
    public DbSet<DeckWord> DeckWords => Set<DeckWord>();
    public DbSet<CardState> CardStates => Set<CardState>();
    public DbSet<ReviewLog> ReviewLogs => Set<ReviewLog>();
    public DbSet<StudySession> StudySessions => Set<StudySession>();
    public DbSet<StudySessionItem> StudySessionItems => Set<StudySessionItem>();
    public DbSet<DailyStat> DailyStats => Set<DailyStat>();
    public DbSet<FavoriteWord> FavoriteWords => Set<FavoriteWord>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Единое именование в базе задаётся правилом, а не перечислением столбцов:
        // правило автоматически распространится на сущности, которые появятся позже.
        configurationBuilder.Conventions.Add(_ => new SnakeCaseNamingConvention());
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Конфигурации подхватываются из текущей сборки — не нужно перечислять их вручную.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
