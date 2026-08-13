using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// الحذف بيسيب أثر ولا لأ.
    ///
    /// القاعدة: **الصف بيتمسح من الجدول خالص طول ما مفيش تاريخ أجور
    /// بيشاور عليه.** الحالة الشائعة (منتج اتضاف بالغلط، عامل اتسجّل
    /// مرتين، سجل إنتاج اتصحّح) مالهاش أي داعي تفضل قاعدة في الجداول
    /// للأبد. وفي المقابل، عامل اشتغل شهور **لازم** يفضل: كشوف أجوره
    /// بتقرا اسمه، ومسحه بيحوّلها لأرقام من غير أصحاب.
    ///
    /// اللي بيفضل في الحالتين هو حدث سجل العمليات — وده مقصود.
    /// </summary>
    public class DeletionScopeTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private const string Reason = "اتسجّل بالغلط";

        private async Task RecordProductionAsync(int workerId, int stageId)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<WorkdayCalculationService>(scope).RecordProductionAsync(
                workerId, stageId, 10, TestDatabase.Today, confirmOverride: true);
        }

        private async Task<int> CountAsync<T>(Func<AppDbContext, IQueryable<T>> set) where T : class
        {
            using var scope = _db.CreateScope();
            return await set(_db.GetService<AppDbContext>(scope)).IgnoreQueryFilters().CountAsync();
        }

        // ======================= الحالة الشائعة: يتمسح خالص =======================

        [Fact]
        public async Task A_worker_with_no_history_is_removed_from_the_table_entirely()
        {
            using (var scope = _db.CreateScope())
            {
                var result = await _db.GetService<WorkerManagementService>(scope)
                    .DeleteWorkerAsync(TestDatabase.WorkerSaidId, "", Reason);

                Assert.True(result.IsDeleted);
                Assert.True(result.WasPermanent);
            }

            using var check = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(check);

            Assert.Null(await db.Workers.IgnoreQueryFilters()
                .FirstOrDefaultAsync(w => w.Id == TestDatabase.WorkerSaidId));
        }

        [Fact]
        public async Task Removing_a_worker_takes_their_skills_with_them()
        {
            var before = await CountAsync(db => db.WorkerSkills);

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkerManagementService>(scope)
                    .DeleteWorkerAsync(TestDatabase.WorkerSaidId, "", Reason);

            // مهارة على عامل مامعدش ليه وجود مالهاش أي معنى
            Assert.True(await CountAsync(db => db.WorkerSkills) < before);
        }

        [Fact]
        public async Task A_production_record_is_always_removed_because_nothing_points_at_it()
        {
            await RecordProductionAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id);

            int recordId;
            using (var scope = _db.CreateScope())
                recordId = await _db.GetService<AppDbContext>(scope)
                    .DailyProductions.Select(r => r.Id).FirstAsync();

            using (var scope = _db.CreateScope())
            {
                var result = await _db.GetService<WorkdayCalculationService>(scope)
                    .DeleteProductionAsync(recordId, "", Reason);

                Assert.True(result.IsDeleted);
                Assert.True(result.WasPermanent);
            }

            // مش متعلّم محذوف — مش موجود أصلاً
            Assert.Equal(0, await CountAsync(db => db.DailyProductions));
        }

        [Fact]
        public async Task Deleting_a_whole_day_leaves_no_rows_behind()
        {
            await RecordProductionAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id);
            await RecordProductionAsync(TestDatabase.WorkerSaidId, TestDatabase.BagStage2Id);

            using (var scope = _db.CreateScope())
                Assert.True((await _db.GetService<WorkdayCalculationService>(scope)
                    .DeleteProductionDayAsync(TestDatabase.Today, "", Reason)).IsDeleted);

            Assert.Equal(0, await CountAsync(db => db.DailyProductions));
        }

        [Fact]
        public async Task A_product_nobody_produced_is_removed_with_its_stages()
        {
            using (var scope = _db.CreateScope())
            {
                var result = await _db.GetService<ProductManagementService>(scope)
                    .DeleteProductAsync(TestDatabase.ProductChainId, "", Reason);

                Assert.True(result.WasPermanent);
            }

            using var check = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(check);

            Assert.Null(await db.Products.IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.Id == TestDatabase.ProductChainId));
            Assert.Empty(await db.ProductionStages.IgnoreQueryFilters()
                .Where(s => s.ProductId == TestDatabase.ProductChainId).ToListAsync());
        }

        // ======================= الحالة اللي لازم تفضل =======================

        [Fact]
        public async Task A_worker_who_actually_produced_is_kept_and_only_flagged()
        {
            await RecordProductionAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id);

            using (var scope = _db.CreateScope())
            {
                var result = await _db.GetService<WorkerManagementService>(scope)
                    .DeleteWorkerAsync(TestDatabase.WorkerAhmedId, "", Reason);

                Assert.True(result.IsDeleted);
                Assert.False(result.WasPermanent);
            }

            using var check = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(check);

            // كشف أجوره القديم لازم يفضل يقرا اسمه
            var worker = await db.Workers.IgnoreQueryFilters()
                .FirstAsync(w => w.Id == TestDatabase.WorkerAhmedId);

            Assert.True(worker.IsDeleted);
            Assert.False(worker.IsActive);
            Assert.Equal(Reason, worker.DeletionReason);
            Assert.Equal("أحمد", worker.DeletedName);
        }

        [Fact]
        public async Task A_worker_with_only_a_penalty_is_kept_too_because_that_is_money()
        {
            using (var scope = _db.CreateScope())
                await _db.GetService<PenaltyService>(scope).RecordPenaltyAsync(
                    TestDatabase.WorkerSaidId, TestDatabase.Today, "تأخير",
                    PenaltyDeduction.HalfDay);

            using var del = _db.CreateScope();
            var result = await _db.GetService<WorkerManagementService>(del)
                .DeleteWorkerAsync(TestDatabase.WorkerSaidId, "", Reason);

            Assert.True(result.IsDeleted);
            Assert.False(result.WasPermanent);
        }

        [Fact]
        public async Task A_product_that_was_produced_is_kept()
        {
            await RecordProductionAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id);

            using var scope = _db.CreateScope();
            var result = await _db.GetService<ProductManagementService>(scope)
                .DeleteProductAsync(TestDatabase.ProductBagId, "", Reason);

            Assert.True(result.IsDeleted);
            Assert.False(result.WasPermanent);
        }

        [Fact]
        public async Task A_soft_deleted_production_row_still_protects_its_worker()
        {
            // السجل المتشال ناعم لسه ماسك المفتاح الأجنبي. لو الفحص
            // استخدم الفلتر العام، كان هيقول "العامل فاضي" ويحاول
            // يمسحه — وقاعدة البيانات ترفض برسالة المستخدم مش هيفهمها.
            await RecordProductionAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id);

            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                var row = await db.DailyProductions.FirstAsync();
                row.IsDeleted = true;
                await db.SaveChangesAsync();
            }

            using var check = _db.CreateScope();
            Assert.False(await _db.GetService<DeletionScopeService>(check)
                .CanRemoveWorkerAsync(TestDatabase.WorkerAhmedId));
        }

        // ======================= الأثر المقصود =======================

        [Fact]
        public async Task The_activity_log_records_the_deletion_either_way()
        {
            using (var scope = _db.CreateScope())
                await _db.GetService<WorkerManagementService>(scope)
                    .DeleteWorkerAsync(TestDatabase.WorkerSaidId, "", Reason);

            using var check = _db.CreateScope();
            var events = await _db.GetService<AppDbContext>(check)
                .ActivityEvents.Where(e => e.EventType == ActivityEventType.WorkerDeleted)
                .ToListAsync();

            var logged = Assert.Single(events);
            Assert.Equal(Reason, logged.Reason);
            Assert.Equal("سعيد", logged.EntityName); // الاسم لقطة، فاضل مقروء بعد المسح
        }
        // ======================= تنظيف اللي اتشال قبل كده =======================

        [Fact]
        public async Task The_cleaner_removes_rows_that_were_only_flagged_before()
        {
            // بنحاكي الوضع القديم: الصف متعلّم محذوف وقاعد في الجدول
            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                var worker = await db.Workers.FirstAsync(w => w.Id == TestDatabase.WorkerSaidId);
                worker.IsDeleted = true;
                await db.SaveChangesAsync();
            }

            using (var scope = _db.CreateScope())
                await DeletedRowsCleaner.PurgeAsync(_db.GetService<AppDbContext>(scope));

            using var check = _db.CreateScope();
            Assert.Null(await _db.GetService<AppDbContext>(check).Workers.IgnoreQueryFilters()
                .FirstOrDefaultAsync(w => w.Id == TestDatabase.WorkerSaidId));
        }

        [Fact]
        public async Task The_cleaner_never_touches_a_worker_who_has_wage_history()
        {
            await RecordProductionAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id);

            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                var worker = await db.Workers.FirstAsync(w => w.Id == TestDatabase.WorkerAhmedId);
                worker.IsDeleted = true;
                await db.SaveChangesAsync();
            }

            using (var scope = _db.CreateScope())
                await DeletedRowsCleaner.PurgeAsync(_db.GetService<AppDbContext>(scope));

            using var check = _db.CreateScope();
            Assert.NotNull(await _db.GetService<AppDbContext>(check).Workers.IgnoreQueryFilters()
                .FirstOrDefaultAsync(w => w.Id == TestDatabase.WorkerAhmedId));
        }

        [Fact]
        public async Task Running_the_cleaner_on_a_clean_database_changes_nothing()
        {
            using var scope = _db.CreateScope();
            Assert.Equal(0, await DeletedRowsCleaner.PurgeAsync(_db.GetService<AppDbContext>(scope)));
        }

        // ======================= الإنتاج الفعلي مستقل عن قطع العمال =======================

        private async Task RecordActualOutputAsync(int stageId)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<ProductionStageOutputService>(scope).RecordOutputAsync(stageId, TestDatabase.Today, 50);
            await _db.GetService<AppDbContext>(scope).SaveChangesAsync();
        }

        [Fact]
        public async Task A_stage_with_no_history_at_all_can_be_removed()
        {
            using var scope = _db.CreateScope();
            Assert.True(await _db.GetService<DeletionScopeService>(scope)
                .CanRemoveStageAsync(TestDatabase.RingStage1Id));
        }

        [Fact]
        public async Task A_stage_with_only_an_actual_output_record_cannot_be_removed()
        {
            // قطعة العامل والإنتاج الفعلي منفصلين تمامًا — مرحلة ممكن يكون
            // معهاش أي سجل إنتاج عمال بس ليها رقم إنتاج فعلي محفوظ (بعد
            // تصحيح مسح سجلات العمال مثلًا)، والـ FK Restrict كان هيرمي
            // خطأ قاعدة بيانات خام من غير الفحص ده
            await RecordActualOutputAsync(TestDatabase.RingStage1Id);

            using var scope = _db.CreateScope();
            Assert.False(await _db.GetService<DeletionScopeService>(scope)
                .CanRemoveStageAsync(TestDatabase.RingStage1Id));
        }

        [Fact]
        public async Task A_product_with_only_an_actual_output_record_on_one_stage_cannot_be_removed()
        {
            await RecordActualOutputAsync(TestDatabase.RingStage1Id);

            using var scope = _db.CreateScope();
            Assert.False(await _db.GetService<DeletionScopeService>(scope)
                .CanRemoveProductAsync(TestDatabase.ProductRingId));
        }
    }
}
