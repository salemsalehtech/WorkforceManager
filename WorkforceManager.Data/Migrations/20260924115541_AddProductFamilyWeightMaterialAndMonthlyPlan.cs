using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductFamilyWeightMaterialAndMonthlyPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FamilyId",
                table: "Products",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Material",
                table: "Products",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PieceWeightGrams",
                table: "Products",
                type: "decimal(10,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MonthlyPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Month = table.Column<int>(type: "INTEGER", nullable: false),
                    PlannedQuantity = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyPlans", x => x.Id);
                    table.CheckConstraint("CK_MonthlyPlan_Month_Valid", "Month BETWEEN 1 AND 12");
                    table.CheckConstraint("CK_MonthlyPlan_PlannedQuantity_NonNegative", "PlannedQuantity >= 0");
                    table.ForeignKey(
                        name: "FK_MonthlyPlans_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductFamilies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductFamilies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_FamilyId",
                table: "Products",
                column: "FamilyId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Product_PieceWeightGrams_Positive",
                table: "Products",
                sql: "PieceWeightGrams IS NULL OR PieceWeightGrams > 0");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyPlans_ProductId_Year_Month",
                table: "MonthlyPlans",
                columns: new[] { "ProductId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductFamilies_Name",
                table: "ProductFamilies",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_ProductFamilies_FamilyId",
                table: "Products",
                column: "FamilyId",
                principalTable: "ProductFamilies",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_ProductFamilies_FamilyId",
                table: "Products");

            migrationBuilder.DropTable(
                name: "MonthlyPlans");

            migrationBuilder.DropTable(
                name: "ProductFamilies");

            migrationBuilder.DropIndex(
                name: "IX_Products_FamilyId",
                table: "Products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Product_PieceWeightGrams_Positive",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "FamilyId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Material",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PieceWeightGrams",
                table: "Products");
        }
    }
}
