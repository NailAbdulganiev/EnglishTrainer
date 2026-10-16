using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishTrainer.Infrastructure.Migrations
{
    /// <summary>
    /// Дневная цель в дневной статистике: день идёт в серию только при её выполнении.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Цель хранится в самой дневной записи, а не берётся из настроек при подсчёте
    /// серии. Иначе повышение плана задним числом обнуляло бы уже набранную серию:
    /// человек поднял цель с 20 до 30 и потерял двадцать дней подряд.
    /// </para>
    /// <para>
    /// Существующие строки получают ноль — «цель не зафиксирована». Такой день
    /// засчитывается при любом ответе, то есть ровно так, как считался до этой
    /// миграции, и серия ни у кого не обрывается на ровном месте.
    /// </para>
    /// </remarks>
    public partial class AddDailyGoalToStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "daily_goal",
                table: "daily_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "daily_goal",
                table: "daily_stats");
        }
    }
}
