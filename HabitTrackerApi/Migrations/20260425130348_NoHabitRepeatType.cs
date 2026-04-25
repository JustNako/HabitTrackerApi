using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HabitTrackerApi.Migrations
{
    /// <inheritdoc />
    public partial class NoHabitRepeatType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DaysOfMonth",
                table: "Habits");

            migrationBuilder.DropColumn(
                name: "DaysOfWeek",
                table: "Habits");

            migrationBuilder.DropColumn(
                name: "PeriodType",
                table: "Habits");

            migrationBuilder.DropColumn(
                name: "TargetDaysPerPeriod",
                table: "Habits");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Habits");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DaysOfMonth",
                table: "Habits",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DaysOfWeek",
                table: "Habits",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PeriodType",
                table: "Habits",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetDaysPerPeriod",
                table: "Habits",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Habits",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
