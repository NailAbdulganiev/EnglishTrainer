using EnglishTrainer.Infrastructure.Repositories;
using EnglishTrainer.IntegrationTests.Database;

namespace EnglishTrainer.IntegrationTests.Repositories;

/// <summary>
/// Пользователи: поиск, привязка Telegram и выборка «кому пора напомнить».
/// </summary>
/// <remarks>
/// Выборка напоминаний — то место, где ошибка видна не сразу и не в логах,
/// а в том, что человеку приходит сообщение в три часа ночи или не приходит
/// вовсе. Условий у неё четыре (привязка, включённость, наступивший срок,
/// ограничение выборки), и проверяются они по отдельности: пропущенное условие
/// не ломает ни один сценарий, кроме самого важного.
/// </remarks>
public sealed class UserRepositoryTests
{
    [Fact]
    public async Task GetByTelegramIdAsync_LoadsSettings()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new UserRepository(scope.Context);

        var user = await TestData.AddUserAsync(scope.Context, telegramId: 123456789L);

        var found = await repository.GetByTelegramIdAsync(123456789L);

        Assert.NotNull(found);
        Assert.Equal(user.Id, found!.Id);

        // Настройки нужны вызывающему коду всегда: и для напоминаний, и для
        // сводки дня. Без Include они были бы null, и сервис молча показывал
        // бы нули вместо целей пользователя.
        Assert.NotNull(found.Settings);
    }

    [Fact]
    public async Task GetByTelegramIdAsync_ForUnknownId_ReturnsNull()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new UserRepository(scope.Context);

        Assert.Null(await repository.GetByTelegramIdAsync(999_999_999L));
    }

    [Fact]
    public async Task GetDueRemindersAsync_ReturnsOnlyUsersWhoAreDue()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new UserRepository(scope.Context);
        var now = DateTime.UtcNow;

        var due = await TestData.AddUserAsync(
            scope.Context, telegramId: 1001L, remindersEnabled: true, nextReminderAtUtc: now.AddMinutes(-5));

        // Привязки нет — отправлять некуда.
        await TestData.AddUserAsync(
            scope.Context, telegramId: null, remindersEnabled: true, nextReminderAtUtc: now.AddMinutes(-5));

        // Напоминания выключены.
        await TestData.AddUserAsync(
            scope.Context, telegramId: 1002L, remindersEnabled: false, nextReminderAtUtc: now.AddMinutes(-5));

        // Срок ещё не наступил.
        await TestData.AddUserAsync(
            scope.Context, telegramId: 1003L, remindersEnabled: true, nextReminderAtUtc: now.AddHours(2));

        var found = await repository.GetDueRemindersAsync(now, limit: 50);

        Assert.Contains(found, user => user.Id == due.Id);
        Assert.DoesNotContain(found, user => user.TelegramId is 1002L or 1003L or null);
    }

    [Fact]
    public async Task GetDueRemindersAsync_OrdersByDueMoment()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new UserRepository(scope.Context);
        var now = DateTime.UtcNow;

        var later = await TestData.AddUserAsync(
            scope.Context, telegramId: 2001L, nextReminderAtUtc: now.AddMinutes(-1));

        var earlier = await TestData.AddUserAsync(
            scope.Context, telegramId: 2002L, nextReminderAtUtc: now.AddHours(-3));

        var found = await repository.GetDueRemindersAsync(now, limit: 50);

        var ordered = found.Select(user => user.Id).ToList();

        // Самое просроченное — первым: если напоминаний накопилось больше, чем
        // можно отправить за один тик, старые не должны ждать вечно.
        Assert.True(
            ordered.IndexOf(earlier.Id) < ordered.IndexOf(later.Id),
            "Более просроченное напоминание должно идти раньше.");
    }

    [Fact]
    public async Task GetDueRemindersAsync_RespectsLimit()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new UserRepository(scope.Context);
        var now = DateTime.UtcNow;

        for (var index = 0; index < 5; index++)
        {
            await TestData.AddUserAsync(
                scope.Context, telegramId: 3000L + index, nextReminderAtUtc: now.AddMinutes(-1));
        }

        Assert.Equal(2, (await repository.GetDueRemindersAsync(now, limit: 2)).Count);

        // Нулевой предел — это «ничего», а не «без ограничения»: планировщик
        // передаёт сюда настройку, и опечатка в ней не должна устроить залп.
        Assert.Empty(await repository.GetDueRemindersAsync(now, limit: 0));
    }

    [Fact]
    public async Task GetDueRemindersAsync_LoadsSettingsForScheduling()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new UserRepository(scope.Context);

        await TestData.AddUserAsync(
            scope.Context, telegramId: 4001L, nextReminderAtUtc: DateTime.UtcNow.AddMinutes(-1));

        var found = await repository.GetDueRemindersAsync(DateTime.UtcNow, limit: 10);

        // После отправки планировщик сдвигает расписание — значит настройки
        // должны быть загружены, иначе сдвигать будет нечего.
        var user = found.Single(candidate => candidate.TelegramId == 4001L);
        Assert.NotNull(user.Settings);
    }

    [Fact]
    public async Task SearchByUsernameAsync_MatchesFromStartOnly()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new UserRepository(scope.Context);

        var token = $"zq{Guid.NewGuid():N}"[..8];
        var target = await TestData.AddUserAsync(scope.Context);

        target.Username = token;
        await scope.Context.SaveChangesAsync();

        var byPrefix = await repository.SearchByUsernameAsync(token[..4], limit: 10);

        Assert.Contains(byPrefix, user => user.Id == target.Id);

        // Совпадение ищется с начала строки: так запрос опирается на индекс
        // и не деградирует с ростом числа людей.
        Assert.DoesNotContain(
            await repository.SearchByUsernameAsync(token[^4..], limit: 10),
            user => user.Id == target.Id);
    }

    [Fact]
    public async Task SearchByUsernameAsync_WithEmptyQuery_ReturnsNothing()
    {
        await using var scope = await TestDatabase.BeginAsync();
        var repository = new UserRepository(scope.Context);

        await TestData.AddUserAsync(scope.Context);

        // Пустой запрос вернул бы весь список пользователей: это и бесполезно,
        // и раскрывает лишнее.
        Assert.Empty(await repository.SearchByUsernameAsync("", limit: 10));
        Assert.Empty(await repository.SearchByUsernameAsync("кто-то", limit: 0));
    }

    [Fact]
    public async Task UniqueIndex_RejectsTwoUsersWithSameTelegram()
    {
        await using var scope = await TestDatabase.BeginAsync();

        await TestData.AddUserAsync(scope.Context, telegramId: 5001L);

        var user = new Domain.Entities.User
        {
            Email = $"dup-{Guid.NewGuid():N}@example.com",
            Username = $"dup{Guid.NewGuid():N}"[..12],
            DisplayName = "Дубль",
            PasswordHash = "не-используется",
            TelegramId = 5001L
        };

        scope.Context.Users.Add(user);

        // Один Telegram — один аккаунт: иначе непонятно, чей прогресс показывать
        // в мини-аппе. Гарантия на уровне базы, а не проверкой в коде.
        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(
            () => scope.Context.SaveChangesAsync());
    }
}
