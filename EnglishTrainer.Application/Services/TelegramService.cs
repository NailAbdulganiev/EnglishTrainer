using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EnglishTrainer.Application.Dtos;
using EnglishTrainer.Application.Mapping;
using EnglishTrainer.Application.Telegram;
using EnglishTrainer.Domain.Abstractions;
using EnglishTrainer.Domain.Entities;
using EnglishTrainer.Domain.Exceptions;
using EnglishTrainer.Domain.Repositories;
using Microsoft.Extensions.Options;

namespace EnglishTrainer.Application.Services;

/// <summary>
/// Привязка Telegram к аккаунту и вход в мини-апп.
/// </summary>
/// <remarks>
/// <para>
/// Два направления, и оба нужны. Веб-версия знает пароль, но не знает, какому
/// Telegram-аккаунту он соответствует; мини-апп знает аккаунт Telegram, но
/// пароля не спрашивает. Код привязки — мостик: его выдаёт веб-версия,
/// принимает бот, и только человек, владеющий обоими, может его перенести.
/// </para>
/// <para>
/// После привязки вход в мини-апп идёт по подписи Telegram
/// (<c>initData</c>), которую проверяет <see cref="ITelegramInitDataValidator"/>.
/// Пароль в этом сценарии не участвует вовсе, поэтому проверка подписи —
/// не дополнительный барьер, а единственный.
/// </para>
/// </remarks>
public sealed class TelegramService(
    IUserRepository users,
    ITelegramLinkCodeRepository linkCodes,
    ITelegramInitDataValidator initDataValidator,
    ITokenIssuer tokenIssuer,
    IOptions<TelegramOptions> options,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    /// <summary>
    /// Сколько живёт код привязки.
    /// </summary>
    /// <remarks>
    /// Пятнадцать минут, а не сутки, как у ссылки сброса пароля. Разница
    /// в том, что код переносят вручную и сразу: человек нажал «привязать»,
    /// перешёл в Telegram, отправил код. Если за четверть часа он этого
    /// не сделал, скорее всего передумал, и код лучше погасить.
    /// </remarks>
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Длина кода в байтах до кодирования. 24 байта — это 32 символа base64url.</summary>
    private const int CodeBytes = 24;

    /// <summary>
    /// Выдаёт новый код привязки, гася прежние.
    /// </summary>
    /// <remarks>
    /// Действующим остаётся ровно один код: иначе «отменить привязку» было бы
    /// невозможно — старый код продолжал бы работать после выдачи нового.
    /// </remarks>
    public async Task<TelegramLinkDto> CreateLinkCodeAsync(int userId, CancellationToken ct = default)
    {
        var user = await users.GetWithSettingsAsync(userId, ct)
            ?? throw NotFoundException.For("Пользователь", userId);

        var now = clock.UtcNow;
        var code = GenerateCode();

        foreach (var active in await linkCodes.GetActiveAsync(userId, now, ct))
        {
            active.UsedAt = now;
        }

        var expiresAt = now.Add(CodeLifetime);

        await linkCodes.AddAsync(new TelegramLinkCode
        {
            UserId = userId,
            // В базе — только хеш: код уходит в ссылку t.me, и утечка таблицы
            // не должна позволять привязать чужой Telegram.
            CodeHash = Hash(code),
            ExpiresAt = expiresAt
        }, ct);

        await unitOfWork.SaveChangesAsync(ct);

        return new TelegramLinkDto(
            code,
            options.Value.DeepLink(code),
            expiresAt,
            user.TelegramId.HasValue);
    }

    /// <summary>
    /// Привязывает Telegram к аккаунту по коду.
    /// </summary>
    /// <remarks>
    /// Повторная привязка того же аккаунта разрешена и ничего не ломает:
    /// человек мог просто потерять старое сообщение и запросить новый код.
    /// А вот привязать один Telegram к двум аккаунтам нельзя — тогда непонятно,
    /// чей прогресс показывать в мини-аппе.
    /// </remarks>
    public async Task<TelegramLinkResultDto> LinkAsync(
        string? code, long telegramId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ValidationException("Не указан код привязки.");
        }

        var now = clock.UtcNow;

        var entity = await linkCodes.GetByHashAsync(Hash(code.Trim()), ct);

        if (entity is null || !entity.IsUsable(now))
        {
            throw new ValidationException(
                "Код привязки недействителен или устарел. Получите новый в профиле на сайте.");
        }

        var user = await users.GetWithSettingsAsync(entity.UserId, ct)
            ?? throw NotFoundException.For("Пользователь", entity.UserId);

        var owner = await users.GetByTelegramIdAsync(telegramId, ct);

        if (owner is not null && owner.Id != user.Id)
        {
            throw new ConflictException(
                "Этот Telegram уже привязан к другому аккаунту. Сначала отвяжите его в профиле того аккаунта.");
        }

        entity.UsedAt = now;
        user.TelegramId = telegramId;
        user.TelegramLinkedAt = now;

        // Расписание напоминаний появляется только теперь: до привязки отправлять
        // их было некуда, поэтому NextReminderAtUtc у пользователя пуст.
        user.ScheduleReminder(now);

        await unitOfWork.SaveChangesAsync(ct);

        return new TelegramLinkResultDto(user.Username, user.DisplayName);
    }

    /// <summary>Отвязывает Telegram. Напоминания после этого не приходят.</summary>
    public async Task<TelegramStatusDto> UnlinkAsync(int userId, CancellationToken ct = default)
    {
        var user = await users.GetWithSettingsAsync(userId, ct)
            ?? throw NotFoundException.For("Пользователь", userId);

        user.TelegramId = null;
        user.TelegramLinkedAt = null;
        user.ScheduleReminder(clock.UtcNow);

        await unitOfWork.SaveChangesAsync(ct);

        return new TelegramStatusDto(false, null);
    }

    /// <summary>Состояние привязки для страницы профиля.</summary>
    public async Task<TelegramStatusDto> GetStatusAsync(int userId, CancellationToken ct = default)
    {
        var user = await users.GetByIdAsync(userId, ct)
            ?? throw NotFoundException.For("Пользователь", userId);

        return new TelegramStatusDto(user.TelegramId.HasValue, user.TelegramLinkedAt);
    }

    /// <summary>Привязан ли этот аккаунт Telegram. Нужно боту, чтобы выбрать приветствие.</summary>
    public async Task<bool> IsLinkedAsync(long telegramId, CancellationToken ct = default) =>
        await users.GetByTelegramIdAsync(telegramId, ct) is not null;

    /// <summary>
    /// Время напоминания в виде «ЧЧ:ММ» — для сообщения бота.
    /// </summary>
    /// <remarks>
    /// Возвращается строкой, а не <see cref="TimeOnly"/>: это подпись для человека,
    /// и формат — часть текста сообщения, а не данных.
    /// </remarks>
    public async Task<string> GetReminderTimeAsync(long telegramId, CancellationToken ct = default)
    {
        var user = await users.GetByTelegramIdAsync(telegramId, ct)
            ?? throw new ForbiddenException("Этот Telegram не привязан к аккаунту.");

        var time = user.Settings?.ReminderTime ?? new TimeOnly(19, 0);

        // Культура задана явно: в некоторых локалях разделитель времени не двоеточие,
        // и «19.00» в сообщении выглядело бы опечаткой.
        return time.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Вход в мини-апп по данным, подписанным Telegram.
    /// </summary>
    /// <remarks>
    /// Отдельного «создать аккаунт» здесь нет намеренно. Человек, открывший
    /// мини-апп впервые, ещё не связан ни с каким прогрессом: молча завести
    /// ему новый аккаунт означало бы потерять всё, что он уже выучил в вебе.
    /// Поэтому незнакомый Telegram получает понятный отказ с инструкцией.
    /// </remarks>
    public async Task<AuthResponseDto> SignInAsync(string? initData, CancellationToken ct = default)
    {
        var data = initDataValidator.Validate(initData)
            ?? throw new UnauthorizedException(
                "Данные Telegram недействительны. Закройте и откройте приложение заново.");

        var user = await users.GetByTelegramIdAsync(data.UserId, ct)
            ?? throw new ForbiddenException(
                "Этот Telegram ещё не привязан к аккаунту. Откройте профиль на сайте, " +
                "нажмите «Привязать Telegram» — или отправьте боту команду /start.");

        var token = tokenIssuer.Issue(user.Id, user.PublicId, user.Email);

        return new AuthResponseDto(token.Value, token.ExpiresAtUtc, user.ToDto());
    }

    /// <summary>
    /// Привязывает Telegram по коду прямо из мини-аппа и сразу выдаёт токен.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Тот же код, что переносят через бота, но без бота: мини-апп уже знает,
    /// какой это аккаунт Telegram, и знает код. Отправлять человека в чат
    /// с командой <c>/link</c> ради того же результата — лишний шаг.
    /// </para>
    /// <para>
    /// Подпись проверяется здесь с той же строгостью: иначе возможность
    /// привязать чужой Telegram к чужому аккаунту появлялась бы ровно
    /// в этом обходном пути.
    /// </para>
    /// </remarks>
    public async Task<AuthResponseDto> LinkFromInitDataAsync(
        string? initData, string? code, CancellationToken ct = default)
    {
        var data = initDataValidator.Validate(initData)
            ?? throw new UnauthorizedException(
                "Данные Telegram недействительны. Закройте и откройте приложение заново.");

        await LinkAsync(code, data.UserId, ct);

        var user = await users.GetByTelegramIdAsync(data.UserId, ct)
            ?? throw new NotFoundException("Привязка не сохранилась. Попробуйте ещё раз.");

        var token = tokenIssuer.Issue(user.Id, user.PublicId, user.Email);

        return new AuthResponseDto(token.Value, token.ExpiresAtUtc, user.ToDto());
    }

    /// <summary>Включает или выключает ежедневные напоминания. Команды бота «/stop» и «/on».</summary>
    public async Task<bool> SetRemindersAsync(
        long telegramId, bool enabled, CancellationToken ct = default)
    {
        var user = await users.GetByTelegramIdAsync(telegramId, ct)
            ?? throw new ForbiddenException("Этот Telegram не привязан к аккаунту.");

        if (user.Settings is not null)
        {
            user.Settings.DailyReminderEnabled = enabled;
        }

        user.ScheduleReminder(clock.UtcNow);

        await unitOfWork.SaveChangesAsync(ct);

        return enabled;
    }

    /// <summary>
    /// Код привязки. Случайные байты, а не слово и не число.
    /// </summary>
    /// <remarks>
    /// Алфавит base64url выбран потому, что он же допустим в параметре
    /// <c>start</c> у ссылки t.me: другие символы Telegram просто не передаст.
    /// </remarks>
    private static string GenerateCode() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(CodeBytes))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    /// <summary>SHA-256 в hex. Сам код нигде не сохраняется.</summary>
    private static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}
