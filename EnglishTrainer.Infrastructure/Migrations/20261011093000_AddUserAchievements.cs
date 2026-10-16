using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EnglishTrainer.Infrastructure.Migrations
{
    /// <summary>
    /// Полученные достижения.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Хранится только факт получения и его дата. Прогресс здесь не лежит:
    /// он считается из состояний карточек, дневной статистики и журнала
    /// ответов. Два источника одного числа расходятся, и человек увидел бы
    /// «100 из 100» рядом с закрытой плашкой.
    /// </para>
    /// <para>
    /// Уникальный индекс по паре «пользователь + код» — это не оптимизация,
    /// а гарантия: выдача идёт после каждого открытия страницы, и два
    /// одновременных запроса без него выдали бы награду дважды.
    /// </para>
    /// </remarks>
    public partial class AddUserAchievements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_achievements",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    unlocked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_achievements", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_achievements_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_achievements_user_id_code",
                table: "user_achievements",
                columns: new[] { "user_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_achievements");
        }
    }
}
