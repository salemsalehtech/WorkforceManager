using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductFamilySortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "ProductFamilies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // العائلات الموجودة عند العميل: ترتيبها المبدئي = الترتيب الأبجدي اللي كان معروض قبل كده،
            // فمفيش حاجة بتتنقّل من مكانها لحد ما المستخدم يرتّب بنفسه
            migrationBuilder.Sql(
                "UPDATE ProductFamilies SET SortOrder = " +
                "(SELECT COUNT(*) FROM ProductFamilies AS other WHERE other.Name < ProductFamilies.Name);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "ProductFamilies");
        }
    }
}
