using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EnglishTrainer.Infrastructure.Persistence;

/// <summary>
/// Фабрика контекста для инструментов EF Core (<c>dotnet ef</c>).
/// </summary>
/// <remarks>
/// Строка подключения берётся из переменной окружения, потому что во время
/// создания миграции приложение ещё не запущено и его конфигурация недоступна.
/// Значение по умолчанию рассчитано на локальную базу из <c>docker-compose.yml</c>.
/// </remarks>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=english_trainer;Username=postgres;Password=postgres";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? DefaultConnectionString;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        return new AppDbContext(options);
    }
}
