using System.Data.Common;
using EnglishTrainer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace EnglishTrainer.IntegrationTests.Database;

/// <summary>
/// Живая база для интеграционных тестов.
/// </summary>
/// <remarks>
/// <para>
/// Зачем это вообще нужно. Сквозной сценарий проверяет API целиком, но он
/// требует запущенного сервера и не может дотянуться до частных случаев:
/// сортировки просроченных карточек, границ выборки, поведения уникального
/// индекса при гонке. А EF-запросы — это код, который компилируется всегда,
/// а работает не всегда: неверно составленный LINQ падает не при сборке,
/// а при первом выполнении, у пользователя.
/// </para>
/// <para>
/// Строка подключения берётся из того же <c>ConnectionStrings__Postgres</c>,
/// что и у приложения, но имя базы заменяется на <c>_tests</c>: интеграционные
/// тесты вставляют и удаляют строки, и делать это в базе с настоящим прогрессом
/// нельзя. База создаётся, если её нет, — иначе тесты не запустились бы
/// на чистой машине без лишних телодвижений.
/// </para>
/// </remarks>
internal static class TestDatabase
{
    /// <summary>Переменная окружения, из которой читается строка подключения.</summary>
    public const string VariableName = "ConnectionStrings__Postgres";

    /// <summary>Суффикс имени тестовой базы.</summary>
    private const string DatabaseSuffix = "_tests";

    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static bool _ready;

    private static string? _connectionString;

    /// <summary>Настроена ли база. Без неё интеграционные тесты не имеют смысла.</summary>
    public static bool IsConfigured => Read() is not null;

    /// <summary>Строка подключения к тестовой базе.</summary>
    /// <exception cref="InvalidOperationException">Переменная окружения не задана.</exception>
    public static string ConnectionString =>
        _connectionString ??= Read() ?? throw new InvalidOperationException(NotConfiguredMessage());

    /// <summary>Готовит базу: создаёт при необходимости и применяет миграции.</summary>
    public static async Task EnsureReadyAsync()
    {
        if (_ready)
        {
            return;
        }

        await Gate.WaitAsync();

        try
        {
            if (_ready)
            {
                return;
            }

            await CreateDatabaseIfMissingAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(ConnectionString)
                .Options;

            await using var context = new AppDbContext(options);

            // Миграции применяются отдельным контекстом, вне тестовых транзакций:
            // DDL внутри транзакции, которая затем откатывается, оставил бы
            // базу без схемы.
            await context.Database.MigrateAsync();

            _ready = true;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Открывает область видимости теста: соединение и транзакцию, которая
    /// откатывается при освобождении.
    /// </summary>
    /// <remarks>
    /// Откат вместо уборки данных руками. Убирать за собой вручную — значит
    /// помнить о каждой таблице и о порядке удаления из-за внешних ключей;
    /// одна забытая строка — и следующий тест падает по причине, которой
    /// в его коде нет. Транзакция возвращает базу в исходное состояние целиком
    /// и одинаково для всех тестов.
    /// </remarks>
    public static async Task<TestScope> BeginAsync()
    {
        await EnsureReadyAsync();

        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        var transaction = await connection.BeginTransactionAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection)
            .Options;

        var context = new AppDbContext(options);

        // Контекст создан поверх уже открытого соединения, поэтому транзакцию
        // нужно подключить явно: иначе EF начнёт собственную, и откат внешней
        // ничего не отменит.
        await context.Database.UseTransactionAsync(transaction);

        return new TestScope(context, connection, transaction);
    }
    /// <summary>Текст, который увидит человек, забывший настроить базу.</summary>
    public static string NotConfiguredMessage() =>
        $"Не задана строка подключения для интеграционных тестов ({VariableName}). " +
        "Поднимите PostgreSQL (docker compose up -d) и задайте переменную, например: " +
        "ConnectionStrings__Postgres=\"Host=localhost;Port=5433;Database=english_trainer;Username=postgres;Password=postgres\". " +
        "Тесты работают с базой <имя>_tests и создают её сами.";

    private static string? Read()
    {
        var configured = Environment.GetEnvironmentVariable(VariableName);

        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        var builder = new NpgsqlConnectionStringBuilder(configured)
        {
            Database = TestDatabaseName(configured)
        };

        return builder.ConnectionString;
    }

    /// <summary>
    /// Имя тестовой базы: имя рабочей плюс суффикс.
    /// </summary>
    /// <remarks>
    /// Имя проверяется, а не подставляется как есть. Оно попадает в SQL-команду
    /// создания базы, где параметров не бывает: подстановка произвольной строки
    /// из настройки — это возможность выполнить чужой SQL, пусть и на своей
    /// машине. Проверка «только строчные буквы, цифры и подчёркивание» её снимает.
    /// </remarks>
    private static string TestDatabaseName(string connectionString)
    {
        var source = new NpgsqlConnectionStringBuilder(connectionString).Database;

        if (string.IsNullOrWhiteSpace(source) || !IsSafeName(source))
        {
            throw new InvalidOperationException(
                $"Имя базы '{source}' содержит символы, недопустимые в имени тестовой базы.");
        }

        return source + DatabaseSuffix;
    }

    private static bool IsSafeName(string value) =>
        value.All(symbol => char.IsAsciiLetterLower(symbol) || char.IsAsciiDigit(symbol) || symbol == '_');

    private static async Task CreateDatabaseIfMissingAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString);
        var database = builder.Database;

        // Подключение идёт к служебной базе postgres: создать базу изнутри
        // создаваемой нельзя, её ещё нет.
        builder.Database = "postgres";

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        await using var check = connection.CreateCommand();
        check.CommandText = "select 1 from pg_database where datname = $1";
        check.Parameters.Add(new NpgsqlParameter { Value = database });

        if (await check.ExecuteScalarAsync() is not null)
        {
            return;
        }

        await using var create = connection.CreateCommand();

        // Имя базы проверено выше и состоит только из безопасных символов,
        // поэтому кавычки здесь — экранирование идентификатора, а не защита
        // от подстановки: параметризовать идентификатор SQL не позволяет.
        create.CommandText = $"create database \"{database}\"";

        await create.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// Область видимости одного теста: контекст, соединение и транзакция,
/// которая откатывается при освобождении.
/// </summary>
internal sealed class TestScope(
    AppDbContext context,
    DbConnection connection,
    DbTransaction transaction) : IAsyncDisposable
{
    public AppDbContext Context { get; } = context;

    public async ValueTask DisposeAsync()
    {
        await transaction.RollbackAsync();
        await transaction.DisposeAsync();
        await Context.DisposeAsync();
        await connection.DisposeAsync();
    }
}
