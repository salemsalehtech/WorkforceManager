using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// المنسّق اللي "بحث سريع" الشامل قايم عليه — بيتأكد إن كل فئة من
    /// السبعة المرتبطة بقاعدة البيانات فعلاً بترجّع نتايج حقيقية، إن
    /// الترتيب منطقي (تطابق أدق = Score أعلى)، والتقييد لكل فئة شغّال
    /// (سجل العمليات، الفئة الأكبر حجمًا، ميغرقش الباقي).
    /// </summary>
    public class GlobalSearchServiceTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Today => TestDatabase.Today;

        private GlobalSearchService Search(IServiceScope scope) =>
            _db.GetService<GlobalSearchService>(scope);

        [Fact]
        public async Task Finds_a_worker_by_name()
        {
            using var scope = _db.CreateScope();

            var results = await Search(scope).SearchAsync("أحمد");

            var worker = Assert.Single(results, r => r.Category == SearchCategory.Worker);
            Assert.Equal(TestDatabase.WorkerAhmedId, worker.WorkerId);
        }

        [Fact]
        public async Task Finds_a_product_by_name()
        {
            using var scope = _db.CreateScope();

            var results = await Search(scope).SearchAsync("دبلة");

            var product = Assert.Single(results, r => r.Category == SearchCategory.Product);
            Assert.Equal(TestDatabase.ProductRingId, product.ProductId);
        }

        [Fact]
        public async Task Finds_a_stage_and_carries_its_parent_product_as_context()
        {
            using var scope = _db.CreateScope();

            var results = await Search(scope).SearchAsync("تشكيل");

            var stage = Assert.Single(results, r => r.Category == SearchCategory.ProductionStage);
            Assert.Equal(TestDatabase.RingStage1Id, stage.ProductionStageId);
            Assert.Equal(TestDatabase.ProductRingId, stage.ProductId);
            Assert.Equal("دبلة", stage.SecondaryText); // اسم المنتج، مش استخدم في المطابقة نفسها
        }

        [Fact]
        public async Task Searching_a_stage_name_does_not_also_flood_results_with_every_other_stage_of_the_same_product()
        {
            // اسم المنتج مش حقل مطابقة للمرحلة (شوف GlobalSearchService.MatchStages) —
            // البحث باسم مرحلة واحدة (تشكيل) المفروض يرجّع المرحلة دي بس، مش
            // مرحلتين (تشكيل + تلميع) لمجرد إنهم في نفس المنتج
            using var scope = _db.CreateScope();

            var results = await Search(scope).SearchAsync("تشكيل");

            Assert.Single(results, r => r.Category == SearchCategory.ProductionStage);
        }

        [Fact]
        public async Task Finds_an_initial_balance_across_products_not_just_the_one_it_was_created_for()
        {
            using var scope = _db.CreateScope();
            var balances = _db.GetService<InitialBalanceService>(scope);

            var created = await balances.CreateAsync(new CreateInitialBalanceRequest
            {
                ProductId = TestDatabase.ProductBagId,
                Name = "رصيد قص متأخر",
                Notes = "لسه ما اتسحبش",
                Quantity = 50,
                OriginalDate = Today
            });

            var results = await Search(scope).SearchAsync("قص متأخر");

            var balance = Assert.Single(results, r => r.Category == SearchCategory.InitialBalance);
            Assert.Equal(created.Id, balance.InitialBalanceId);
            Assert.Equal(TestDatabase.ProductBagId, balance.ProductId);
        }

        [Fact]
        public async Task Finds_a_memory_plan_alongside_the_product_it_belongs_to()
        {
            using var scope = _db.CreateScope();
            var memories = _db.GetService<ProductionMemoryService>(scope);

            await memories.CreateAsync(
                TestDatabase.ProductRingId,
                new[] { TestDatabase.RingStage1Id },
                "خطة تشكيل بس من غير تلميع", Today.AddDays(3));

            var results = await Search(scope).SearchAsync("دبلة");

            // نفس الاستعلام لازم يرجّع الاتنين: المنتج نفسه، وخطة الذاكرة بتاعته
            Assert.Contains(results, r => r.Category == SearchCategory.Product);
            Assert.Contains(results, r => r.Category == SearchCategory.MemoryPlan);
        }

        [Fact]
        public async Task Finds_an_activity_log_entry_by_its_snapshot_name()
        {
            using var scope = _db.CreateScope();
            var log = _db.GetService<ActivityLogService>(scope);

            var logged = await log.LogAsync(
                ActivityEventType.ScrapRecorded, "Product", TestDatabase.ProductBagId,
                entityName: "قطعة تالفة أثناء الخياطة", details: "تسجيل اختباري");

            var results = await Search(scope).SearchAsync("قطعة تالفة");

            var entry = Assert.Single(results, r => r.Category == SearchCategory.ActivityLogEntry);
            Assert.Equal(logged.Id, entry.ActivityEventId);
            Assert.NotNull(entry.ActivityEventOccurredAt);
        }

        [Fact]
        public async Task Finds_a_built_in_report_template_even_without_the_hamza()
        {
            using var scope = _db.CreateScope();

            // "الاسبوع" من غير همزة — لازم يوصل لـ"كشف الأسبوع" عن طريق
            // نفس تطبيع ArabicSearch اللي SearchMatcher قايم عليه
            var results = await Search(scope).SearchAsync("كشف الاسبوع");

            var template = Assert.Single(results, r => r.Category == SearchCategory.ReportTemplate);
            Assert.Equal("كشف الأسبوع", template.ReportTemplateName);
        }

        [Fact]
        public async Task Finds_a_department_account_worker()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);

            db.Workers.Add(new Worker
            {
                FullName = "مدير الفرع الشرقي", IsActive = true,
                HourlyRole = HourlyRole.DepartmentManager, DailyWageEgp = 300m
            });
            await db.SaveChangesAsync();

            var results = await Search(scope).SearchAsync("مدير الفرع");

            var account = Assert.Single(results, r => r.Category == SearchCategory.DepartmentAccount);
            Assert.Equal("مدير الفرع الشرقي", account.PrimaryText);

            // حساب إداري ميظهرش تحت فئة العمال العادية — استعلامه الأصلي مستبعده أصلًا
            Assert.DoesNotContain(results, r => r.Category == SearchCategory.Worker && r.PrimaryText == "مدير الفرع الشرقي");
        }

        [Fact]
        public async Task Empty_or_whitespace_query_returns_no_results()
        {
            using var scope = _db.CreateScope();
            var service = Search(scope);

            Assert.Empty(await service.SearchAsync(""));
            Assert.Empty(await service.SearchAsync("   "));
            Assert.Empty(await service.SearchAsync(null));
        }

        [Fact]
        public async Task Results_are_sorted_by_score_descending_across_every_category()
        {
            using var scope = _db.CreateScope();

            // "دبله" (بدل "دبلة") بيدّي تطابق دقيق للمنتج بعد التطبيع، وممكن
            // يطابق حاجات تانية بشكل أضعف — الترتيب النهائي لازم يفضل تنازلي
            var results = await Search(scope).SearchAsync("دبله");

            var scores = results.Select(r => r.Score).ToList();
            Assert.Equal(scores.OrderByDescending(s => s), scores);
        }

        [Fact]
        public async Task Activity_log_results_are_capped_so_one_category_cannot_flood_the_list()
        {
            using var scope = _db.CreateScope();
            var log = _db.GetService<ActivityLogService>(scope);

            for (var i = 0; i < GlobalSearchService.MaxResultsPerCategory + 5; i++)
            {
                await log.LogAsync(
                    ActivityEventType.ScrapRecorded, "Product", TestDatabase.ProductBagId,
                    entityName: $"حدث اختباري رقم {i}");
            }

            var results = await Search(scope).SearchAsync("حدث اختباري");

            var activityResults = results.Where(r => r.Category == SearchCategory.ActivityLogEntry).ToList();
            Assert.Equal(GlobalSearchService.MaxResultsPerCategory, activityResults.Count);
        }
    }
}
