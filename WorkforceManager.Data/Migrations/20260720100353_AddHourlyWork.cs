using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHourlyWork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HourlyRole",
                table: "Workers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HourlyWorkLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkerId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndHour24 = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkdaysCredited = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HourlyWorkLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HourlyWorkLogs_Workers_WorkerId",
                        column: x => x.WorkerId,
                        principalTable: "Workers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HourlyWorkLogs_Date",
                table: "HourlyWorkLogs",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_HourlyWorkLogs_WorkerId_Date",
                table: "HourlyWorkLogs",
                columns: new[] { "WorkerId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HourlyWorkLogs");

            migrationBuilder.DropColumn(
                name: "HourlyRole",
                table: "Workers");
        }
    }
}
