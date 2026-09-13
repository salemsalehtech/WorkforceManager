using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCoreSharedSystems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AutoSampleDays",
                table: "WorkerSkills",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAutoCalculatedAt",
                table: "WorkerSkills",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LastManualValue",
                table: "WorkerSkills",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RatingSource",
                table: "WorkerSkills",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "RatingValue",
                table: "WorkerSkills",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Workers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "Workers",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedName",
                table: "Workers",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletionReason",
                table: "Workers",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Workers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Products",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "Products",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedName",
                table: "Products",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletionReason",
                table: "Products",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Products",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "ProductionStages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "ProductionStages",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedName",
                table: "ProductionStages",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletionReason",
                table: "ProductionStages",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "ProductionStages",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "DailyProductions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "DailyProductions",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedName",
                table: "DailyProductions",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletionReason",
                table: "DailyProductions",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "DailyProductions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ActivityEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventType = table.Column<int>(type: "INTEGER", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    EntityId = table.Column<int>(type: "INTEGER", nullable: false),
                    EntityName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Details = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OperationsCredentials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PasswordSalt = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    FailedAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationsCredentials", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_EntityType_EntityId",
                table: "ActivityEvents",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_EventType",
                table: "ActivityEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_OccurredAt",
                table: "ActivityEvents",
                column: "OccurredAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityEvents");

            migrationBuilder.DropTable(
                name: "OperationsCredentials");

            migrationBuilder.DropColumn(
                name: "AutoSampleDays",
                table: "WorkerSkills");

            migrationBuilder.DropColumn(
                name: "LastAutoCalculatedAt",
                table: "WorkerSkills");

            migrationBuilder.DropColumn(
                name: "LastManualValue",
                table: "WorkerSkills");

            migrationBuilder.DropColumn(
                name: "RatingSource",
                table: "WorkerSkills");

            migrationBuilder.DropColumn(
                name: "RatingValue",
                table: "WorkerSkills");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Workers");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "Workers");

            migrationBuilder.DropColumn(
                name: "DeletedName",
                table: "Workers");

            migrationBuilder.DropColumn(
                name: "DeletionReason",
                table: "Workers");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Workers");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "DeletedName",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "DeletionReason",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "ProductionStages");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "ProductionStages");

            migrationBuilder.DropColumn(
                name: "DeletedName",
                table: "ProductionStages");

            migrationBuilder.DropColumn(
                name: "DeletionReason",
                table: "ProductionStages");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "ProductionStages");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "DailyProductions");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "DailyProductions");

            migrationBuilder.DropColumn(
                name: "DeletedName",
                table: "DailyProductions");

            migrationBuilder.DropColumn(
                name: "DeletionReason",
                table: "DailyProductions");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "DailyProductions");
        }
    }
}
