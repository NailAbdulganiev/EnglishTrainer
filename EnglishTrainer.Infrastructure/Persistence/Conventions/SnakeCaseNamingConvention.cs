using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace EnglishTrainer.Infrastructure.Persistence.Conventions;

/// <summary>
/// Приводит имена столбцов в базе к snake_case.
/// </summary>
/// <remarks>
/// <para>
/// Имена таблиц заданы явно (<c>deck_words</c>, <c>card_states</c>), а имена столбцов
/// по умолчанию остаются такими, как названы свойства C#: <c>UserId</c>, <c>DueAt</c>.
/// Получается база, в которой таблицы названы одним стилем, а столбцы — другим.
/// </para>
/// <para>
/// Причина, по которой это стоит исправить сразу: в PostgreSQL незакавыченный
/// идентификатор приводится к нижнему регистру, поэтому любой запрос вручную
/// превращается в набор кавычек — <c>select "UserId" from card_states</c>.
/// Через полгода таких запросов будут десятки, и каждый легко ошибиться.
/// </para>
/// <para>
/// Реализовано конвенцией, а не двумя сотнями <c>HasColumnName</c>: правило одно
/// и применяется ко всем сущностям, включая те, что появятся позже. Забыть его
/// в новой конфигурации невозможно.
/// </para>
/// <para>
/// Это финализирующая конвенция: она срабатывает после всех явных настроек,
/// поэтому <c>HasColumnType</c>, <c>HasMaxLength</c> и фильтры индексов не затрагиваются.
/// </para>
/// </remarks>
public sealed class SnakeCaseNamingConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (tableName is null)
                continue;

            var storeObject = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());

            foreach (var property in entityType.GetProperties())
            {
                var columnName = property.GetColumnName(storeObject);
                if (columnName is null)
                    continue;

                var snakeCase = ToSnakeCase(columnName);

                if (!string.Equals(snakeCase, columnName, StringComparison.Ordinal))
                    property.SetColumnName(snakeCase);
            }
        }
    }

    /// <summary>
    /// Переводит имя в snake_case: <c>CardStateId</c> → <c>card_state_id</c>,
    /// <c>IpaUk</c> → <c>ipa_uk</c>.
    /// </summary>
    /// <remarks>
    /// Подчёркивание ставится перед заглавной буквой, за которой следует строчная,
    /// и перед любой заглавной, кроме первой. Без второго условия аббревиатуры
    /// разваливались бы на отдельные буквы: <c>IpaURL</c> → <c>ipa_u_r_l</c>.
    /// </remarks>
    private static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);

        for (var index = 0; index < name.Length; index++)
        {
            var symbol = name[index];

            if (char.IsUpper(symbol))
            {
                var previousIsLower = index > 0 && !char.IsUpper(name[index - 1]);
                var nextIsLower = index + 1 < name.Length && char.IsLower(name[index + 1]);

                if (index > 0 && (previousIsLower || nextIsLower))
                    builder.Append('_');

                builder.Append(char.ToLowerInvariant(symbol));
            }
            else
            {
                builder.Append(symbol);
            }
        }

        return builder.ToString();
    }
}
