using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EnglishTrainer.Infrastructure.Migrations
{
    /// <summary>
    /// Привязка Telegram: одноразовые коды и момент следующего напоминания.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Таблица кодов повторяет устройство <c>password_reset_tokens</c>, и это
    /// не совпадение: задача та же — выдать наружу одноразовый секрет, не храня
    /// его самого. В базе лежит только SHA-256, поэтому утечка таблицы не даёт
    /// привязать чужой Telegram к своему аккаунту.
    /// </para>
    /// <para>
    /// Столбец <c>next_reminder_at_utc</c> необязательный: у пользователя без
    /// привязанного Telegram напоминаний нет, и расписание ему не назначается.
    /// Поэтому существующие строки миграция не ломает — им просто нечего
    /// заполнять.
    /// </para>
    /// </remarks>
    public partial class AddTelegramLinkCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "next_reminder_at_utc",
                table: "user_settings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "telegram_link_codes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_link_codes", x => x.id);
                    table.ForeignKey(
                        name: "FK_telegram_link_codes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Планировщик напоминаний просыпается каждую минуту и ищет строки
            // по этому столбцу. Без индекса выборка «кому пора» читала бы
            // таблицу настроек целиком — а она растёт вместе с числом людей.
            migrationBuilder.CreateIndex(
                name: "IX_user_settings_next_reminder_at_utc",
                table: "user_settings",
                column: "next_reminder_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_link_codes_code_hash",
                table: "telegram_link_codes",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_telegram_link_codes_user_id_expires_at",
                table: "telegram_link_codes",
                columns: new[] { "user_id", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "telegram_link_codes");

            migrationBuilder.DropIndex(
                name: "IX_user_settings_next_reminder_at_utc",
                table: "user_settings");

            migrationBuilder.DropColumn(
                name: "next_reminder_at_utc",
                table: "user_settings");
        }
    }
}
