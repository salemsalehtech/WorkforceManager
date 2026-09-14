using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;

namespace WorkforceManager.UI.Sandbox
{
    /// <summary>
    /// بيانات وهمية واضحة إنها تدريب — مش بيانات مصنع حقيقية زرعناها بالغلط،
    /// ومش أسماء اختبارات تقنية زي WorkerAhmedId (شوف
    /// WorkforceManager.Tests\TestDatabase.cs) اللي مصممة تتقرا في كود
    /// اختبار مش على شاشة مستخدم حقيقي.
    ///
    /// عامل "يوسف" متعمّد من غير أي مهارة على "منتج تجريبي — خاتم فضة" —
    /// عشان فلو تدريب "إضافة مهارة" يبقى إضافة حقيقية، مش إعادة تقييم
    /// مهارة موجودة أصلًا.
    /// </summary>
    internal static class SandboxDemoSeeder
    {
        public const int RingProductId = 1;
        public const int RingShapingStageId = 1;
        public const int RingPolishingStageId = 2;

        public const int ChainProductId = 2;
        public const int ChainWeldingStageId = 3;

        public const int YoussefWorkerId = 1;
        public const int SaraWorkerId = 2;

        public const int DemoUserId = 1;

        public static async Task SeedAsync(AppDbContext db)
        {
            db.Products.AddRange(
                new Product { Id = RingProductId, Name = "منتج تجريبي — خاتم فضة", IsActive = true },
                new Product { Id = ChainProductId, Name = "منتج تجريبي — سلسلة", IsActive = true });

            db.ProductionStages.AddRange(
                new ProductionStage
                {
                    Id = RingShapingStageId, ProductId = RingProductId, StageName = "تشكيل",
                    SortOrder = 1, PiecesPerWorkday = 10, IsActive = true
                },
                new ProductionStage
                {
                    Id = RingPolishingStageId, ProductId = RingProductId, StageName = "تلميع",
                    SortOrder = 2, PiecesPerWorkday = 10, IsActive = true
                },
                new ProductionStage
                {
                    Id = ChainWeldingStageId, ProductId = ChainProductId, StageName = "لحام",
                    SortOrder = 1, PiecesPerWorkday = 10, IsActive = true
                });

            db.Workers.AddRange(
                new Worker
                {
                    Id = YoussefWorkerId, FullName = "يوسف (عامل تجريبي)", IsActive = true, DailyWageEgp = 200m
                },
                new Worker
                {
                    Id = SaraWorkerId, FullName = "سارة (عاملة تجريبية)", IsActive = true, DailyWageEgp = 200m
                });

            // سارة عندها مهارة على السلسلة (عشان بروفايلها مش فاضي خالص)،
            // يوسف من غير أي مهارة على الخاتم عن قصد — شوف الكومنت فوق
            db.WorkerSkills.Add(new WorkerSkill
            {
                WorkerId = SaraWorkerId, ProductionStageId = ChainWeldingStageId,
                Level = SkillLevel.Proficient, Stars = 3
            });

            db.AppUsers.Add(new AppUser
            {
                Id = DemoUserId, Username = "sandbox", PasswordHash = "x", PasswordSalt = "x",
                DisplayName = "وضع التجربة"
            });

            await db.SaveChangesAsync();
        }
    }
}
