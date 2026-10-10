using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EnglishTrainer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFavoritesAndFriends : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ник добавляется в три шага, потому что таблица пользователей уже не пуста.
            // Сначала колонка появляется необязательной, затем заполняется по данным,
            // и только потом становится обязательной и уникальной. Одним действием
            // это сделало бы существующие строки невалидными.
            migrationBuilder.AddColumn<string>(
                name: "username",
                table: "users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            // Ник переносится из локальной части email по тем же правилам, что и в коде
            // (UsernamePolicy): только латиница, цифры, дефис и подчёркивание, длина 3–32.
            // Совпадающие значения разводятся числовым хвостом — иначе уникальный индекс
            // не создастся, и миграция упадёт на живых данных.
            migrationBuilder.Sql("""
                WITH prepared AS (
                    SELECT id,
                           CASE
                               WHEN length(cleaned) < 3 THEN 'user' || id
                               ELSE left(cleaned, 32)
                           END AS base_name
                    FROM (
                        SELECT id,
                               regexp_replace(lower(split_part(email, '@', 1)), '[^a-z0-9_-]', '', 'g') AS cleaned
                        FROM users
                        WHERE username IS NULL
                    ) AS raw
                ),
                numbered AS (
                    SELECT id,
                           base_name,
                           row_number() OVER (PARTITION BY base_name ORDER BY id) AS seq
                    FROM prepared
                )
                UPDATE users AS target
                SET username = CASE
                        WHEN numbered.seq = 1 THEN numbered.base_name
                        ELSE left(numbered.base_name, 32 - length(numbered.seq::text)) || numbered.seq::text
                    END
                FROM numbered
                WHERE target.id = numbered.id;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "username",
                table: "users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "favorite_words",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    word_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_favorite_words", x => x.id);
                    table.ForeignKey(
                        name: "FK_favorite_words_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_favorite_words_words_word_id",
                        column: x => x.word_id,
                        principalTable: "words",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "friendships",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    requester_id = table.Column<int>(type: "integer", nullable: false),
                    addressee_id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    responded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_friendships", x => x.id);
                    table.ForeignKey(
                        name: "FK_friendships_users_addressee_id",
                        column: x => x.addressee_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_friendships_users_requester_id",
                        column: x => x.requester_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_username",
                table: "users",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_favorite_words_user_id_word_id",
                table: "favorite_words",
                columns: new[] { "user_id", "word_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_favorite_words_word_id",
                table: "favorite_words",
                column: "word_id");

            migrationBuilder.CreateIndex(
                name: "IX_friendships_addressee_id_status",
                table: "friendships",
                columns: new[] { "addressee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_friendships_requester_id_addressee_id",
                table: "friendships",
                columns: new[] { "requester_id", "addressee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_friendships_requester_id_status",
                table: "friendships",
                columns: new[] { "requester_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "favorite_words");

            migrationBuilder.DropTable(
                name: "friendships");

            migrationBuilder.DropIndex(
                name: "IX_users_username",
                table: "users");

            migrationBuilder.DropColumn(
                name: "username",
                table: "users");
        }
    }
}
