using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishTrainer.Infrastructure.Migrations
{
    /// <summary>
    /// Личные колоды: владелец колоды.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Столбец необязательный, и это не формальность: у всех существующих колод
    /// владельца нет, потому что они системные. Обязательный столбец потребовал
    /// бы придумать им владельца, а «системная колода принадлежит кому-то» —
    /// неправда.
    /// </para>
    /// <para>
    /// Каскад по владельцу: удаление аккаунта уносит его личные колоды. Слова
    /// при этом остаются — они лежат в общем справочнике и могут использоваться
    /// другими колодами, включая системные.
    /// </para>
    /// </remarks>
    public partial class AddOwnDecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "owner_id",
                table: "decks",
                type: "integer",
                nullable: true);

            // Список колод собирается как «системные плюс мои», поэтому выборка
            // идёт по владельцу. Без индекса это чтение всей таблицы колод
            // на каждое открытие страницы тренировки.
            migrationBuilder.CreateIndex(
                name: "IX_decks_owner_id",
                table: "decks",
                column: "owner_id");

            migrationBuilder.AddForeignKey(
                name: "FK_decks_users_owner_id",
                table: "decks",
                column: "owner_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_decks_users_owner_id",
                table: "decks");

            migrationBuilder.DropIndex(
                name: "IX_decks_owner_id",
                table: "decks");

            migrationBuilder.DropColumn(
                name: "owner_id",
                table: "decks");
        }
    }
}
