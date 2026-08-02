using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveProductionBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // لازم نفضّي جدول الدفعات بنفسنا قبل ما يتشال.
            //
            // SQLite بتعمل DELETE ضمني قبل DROP TABLE وبتفحص المفاتيح
            // الأجنبية عليه. وفيه مرجعين على الجدول ده:
            //   • DailyProductions.ProductionBatchId  (SET NULL)
            //   • ProductionBatches.SplitFromBatchId  (RESTRICT) ← مرجع ذاتي
            //
            // الأخير هو اللي بيكسر الترحيل: الدفعة اللي اتقسمت بتشاور على
            // أصلها، وحذف الأصل بيقع على RESTRICT حتى لو الاتنين بيتحذفوا
            // في نفس اللحظة. تصفير المرجعين وتفضية الجدول بيخلي DROP TABLE
            // يعدّي — وسجلات الإنتاج نفسها (وأجور العمال المبنية عليها)
            // مبتتلمسش، العمود بس هو اللي بيروح.
            migrationBuilder.Sql("UPDATE DailyProductions SET ProductionBatchId = NULL;");
            migrationBuilder.Sql("UPDATE ProductionBatches SET SplitFromBatchId = NULL;");
            migrationBuilder.Sql("DELETE FROM ProductionBatches;");

            migrationBuilder.DropIndex(
                name: "IX_DailyProductions_ProductionBatchId",
                table: "DailyProductions");

            migrationBuilder.DropColumn(
                name: "ProductionBatchId",
                table: "DailyProductions");

            migrationBuilder.DropTable(
                name: "ProductionBatches");

            migrationBuilder.RenameColumn(
                name: "CarriedPieces",
                table: "ProductionDayClosures",
                newName: "ParkedPieces");

            migrationBuilder.RenameColumn(
                name: "CarriedBatchCount",
                table: "ProductionDayClosures",
                newName: "CompletedPieces");

            // العمودين اتغيّر معناهم مش اسمهم بس: "عدد الدفعات المرحّلة" بقى
            // "قطع خلصت الخط"، و"القطع المرحّلة" بقت "القطع المستنية بين
            // المراحل". القيم القديمة لو فضلت هتقرا كأرقام إنتاج حقيقية وهي
            // مش كده — التصفير أصدق من رقم مضلّل، والتقرير بيحسب من سجلات
            // الإنتاج أصلاً لأي يوم مش مخزّنة لقطته
            migrationBuilder.Sql("UPDATE ProductionDayClosures SET CompletedPieces = 0, ParkedPieces = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ParkedPieces",
                table: "ProductionDayClosures",
                newName: "CarriedPieces");

            migrationBuilder.RenameColumn(
                name: "CompletedPieces",
                table: "ProductionDayClosures",
                newName: "CarriedBatchCount");

            migrationBuilder.AddColumn<int>(
                name: "ProductionBatchId",
                table: "DailyProductions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductionBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LastCompletedStageId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    SplitFromBatchId = table.Column<int>(type: "INTEGER", nullable: true),
                    CompletedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsOpeningBalance = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionBatches_ProductionBatches_SplitFromBatchId",
                        column: x => x.SplitFromBatchId,
                        principalTable: "ProductionBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionBatches_ProductionStages_LastCompletedStageId",
                        column: x => x.LastCompletedStageId,
                        principalTable: "ProductionStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionBatches_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyProductions_ProductionBatchId",
                table: "DailyProductions",
                column: "ProductionBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_CompletedDate",
                table: "ProductionBatches",
                column: "CompletedDate");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_LastCompletedStageId",
                table: "ProductionBatches",
                column: "LastCompletedStageId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_ProductId_Status",
                table: "ProductionBatches",
                columns: new[] { "ProductId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_SplitFromBatchId",
                table: "ProductionBatches",
                column: "SplitFromBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_StartedDate",
                table: "ProductionBatches",
                column: "StartedDate");

            migrationBuilder.AddForeignKey(
                name: "FK_DailyProductions_ProductionBatches_ProductionBatchId",
                table: "DailyProductions",
                column: "ProductionBatchId",
                principalTable: "ProductionBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
