using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HabitTrackerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Habits",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Habits",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "HabitLogs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "HabitLogs",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Habits");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Habits");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "HabitLogs");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "HabitLogs");
        }
    }
}
