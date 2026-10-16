using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishTrainer.Infrastructure.Migrations
{
    /// <summary>
    /// Аватар пользователя: имя файла в хранилище.
    /// </summary>
    /// <remarks>
    /// Столбец необязательный и добавляется одной операцией: у существующих
    /// пользователей аватара просто нет, и значение по умолчанию для него
    /// не нужно. Это тот случай, когда обязательный столбец добавить было бы
    /// нельзя, — здесь ограничение не возникает.
    /// </remarks>
    public partial class AddUserAvatar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "avatar_file_name",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "avatar_file_name",
                table: "users");
        }
    }
}
