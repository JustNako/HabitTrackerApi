using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HabitTrackerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddHabitsRepeat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RepeatDaysOfMonth",
                table: "Habits",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepeatDaysOfWeek",
                table: "Habits",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RepeatPeriod",
                table: "Habits",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RepeatTimesPerPeriod",
                table: "Habits",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RepeatType",
                table: "Habits",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RepeatDaysOfMonth",
                table: "Habits");

            migrationBuilder.DropColumn(
                name: "RepeatDaysOfWeek",
                table: "Habits");

            migrationBuilder.DropColumn(
                name: "RepeatPeriod",
                table: "Habits");

            migrationBuilder.DropColumn(
                name: "RepeatTimesPerPeriod",
                table: "Habits");

            migrationBuilder.DropColumn(
                name: "RepeatType",
                table: "Habits");
        }
    }
}
