using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartmentAccountLogins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AppUserId",
                table: "OperationsCredentials",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkerId",
                table: "AppUsers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperationsCredentials_AppUserId",
                table: "OperationsCredentials",
                column: "AppUserId",
                unique: true,
                filter: "\"AppUserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_WorkerId",
                table: "AppUsers",
                column: "WorkerId",
                unique: true,
                filter: "\"WorkerId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_AppUsers_Workers_WorkerId",
                table: "AppUsers",
                column: "WorkerId",
                principalTable: "Workers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_OperationsCredentials_AppUsers_AppUserId",
                table: "OperationsCredentials",
                column: "AppUserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppUsers_Workers_WorkerId",
                table: "AppUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_OperationsCredentials_AppUsers_AppUserId",
                table: "OperationsCredentials");

            migrationBuilder.DropIndex(
                name: "IX_OperationsCredentials_AppUserId",
                table: "OperationsCredentials");

            migrationBuilder.DropIndex(
                name: "IX_AppUsers_WorkerId",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "AppUserId",
                table: "OperationsCredentials");

            migrationBuilder.DropColumn(
                name: "WorkerId",
                table: "AppUsers");
        }
    }
}
