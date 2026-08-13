using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRackingStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RackingWorkerId",
                table: "Products",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRackingStage",
                table: "ProductionStages",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Products_RackingWorkerId",
                table: "Products",
                column: "RackingWorkerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Workers_RackingWorkerId",
                table: "Products",
                column: "RackingWorkerId",
                principalTable: "Workers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_Workers_RackingWorkerId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_RackingWorkerId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "RackingWorkerId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "IsRackingStage",
                table: "ProductionStages");
        }
    }
}
