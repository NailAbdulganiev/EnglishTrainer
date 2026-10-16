using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishTrainer.Infrastructure.Migrations
{
    /// <summary>
    /// Дневная цель по умолчанию: тридцать ответов → десять.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Цель — это то, сколько нужно сделать, чтобы день засчитался в серию.
    /// Тридцать ответов при десяти новых словах в день недостижимы в первый же
    /// день: человек честно проходил сессию и видел «серия 0». Десять ответов —
    /// ровно одна обычная сессия, столько же, сколько даётся новых слов.
    /// </para>
    /// <para>
    /// Меняется только значение по умолчанию, то есть настройка тех, кто ещё
    /// не зарегистрирован. У уже созданных аккаунтов цель своя, и переписывать
    /// её миграцией нельзя: человек мог выставить её осознанно.
    /// </para>
    /// </remarks>
    public partial class LowerDefaultDailyGoal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "daily_goal",
                table: "user_settings",
                type: "integer",
                nullable: false,
                defaultValue: 10,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 30);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "daily_goal",
                table: "user_settings",
                type: "integer",
                nullable: false,
                defaultValue: 30,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 10);
        }
    }
}
