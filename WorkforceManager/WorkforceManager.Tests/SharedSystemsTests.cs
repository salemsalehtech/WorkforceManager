using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// اختبارات الأنظمة المشتركة الأربعة والضمانات اللي بتحرسها:
    /// مفيش عملية حساسة بتعدّي البوابة، مفيش حذف فعلي لشغل، مفيش حدث
    /// بيضيع، ومصدر التقييم دايمًا واضح.
    /// </summary>
    public class SharedSystemsTests : IDisposable
    {
        private const string OperationsPassword = "op-secret";
        private const string WrongPassword = "not-it";

        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        /// <summary>بيحطّ كلمة سر العمليات — أغلب الاختبارات محتاجة البوابة مفعّلة</summary>
        private async Task ConfigurePasswordAsync()
        {
            using var scope = _db.CreateScope();
            await _db.GetService<OperationsPasswordService>(scope)
                .SetPasswordAsync(null, OperationsPassword);
        }

        /// <summary>بيسجّل إنتاج ويرجّع معرّف السجل</summary>
        private async Task<int> RecordProductionAsync(int pieces = 100)
        {
            var range = new FlowRangeDto
            {
                FromStageId = TestDatabase.BagStage1Id,
                ToStageId = TestDatabase.BagStage1Id,
                PieceCount = pieces
            };
            var shares = new[]
            {
                new FlowShareDto
                {
                    ProductionStageId = TestDatabase.BagStage1Id,
                    WorkerId = TestDatabase.WorkerAhmedId,
                    PieceCount = pieces
                }
            };

            using (var scope = _db.CreateScope())
                await _db.GetService<ProductionFlowService>(scope).RecordFlowAsync(
                    TestDatabase.ProductBagId, TestDatabase.Today, new[] { range }, shares,
                    confirmOverride: true);

            return (await _db.GetProductionAsync()).Single().Id;
        }

        // ======================= نظام 1: بوابة كلمة السر =======================

        [Fact]
        public async Task Sensitive_action_is_blocked_without_the_right_password()
        {
            await ConfigurePasswordAsync();
            var recordId = await RecordProductionAsync();

            using var scope = _db.CreateScope();
            var result = await _db.GetService<WorkdayCalculationService>(scope)
                .DeleteProductionAsync(recordId, WrongPassword, "تجربة");

            Assert.False(result.IsDeleted);
            Assert.Contains("غلط", result.Message);
        }

        [Fact]
        public async Task Failed_password_leaves_no_partial_change()
        {
            await ConfigurePasswordAsync();
            var recordId = await RecordProductionAsync();

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope)
                    .DeleteProductionAsync(recordId, WrongPassword, "تجربة");

            // لا السجل اتشال ولا حدث اتكتب — الرفض بيحصل قبل أي كتابة
            var record = Assert.Single(await _db.GetProductionAsync());
            Assert.False(record.IsDeleted);

            using var check = _db.CreateScope();
            Assert.Empty(await _db.GetService<ActivityLogService>(check).GetRecentAsync());
        }

        [Fact]
        public async Task Correct_password_lets_the_action_through()
        {
            await ConfigurePasswordAsync();
            var recordId = await RecordProductionAsync();

            using var scope = _db.CreateScope();
            var result = await _db.GetService<WorkdayCalculationService>(scope)
                .DeleteProductionAsync(recordId, OperationsPassword, "اتسجل بالغلط");

            Assert.True(result.IsDeleted);
        }

        [Fact]
        public async Task Gate_locks_out_after_repeated_wrong_attempts()
        {
            await ConfigurePasswordAsync();

            using var scope = _db.CreateScope();
            var gate = _db.GetService<OperationsPasswordService>(scope);

            for (var i = 0; i < OperationsPasswordService.MaxFailedAttempts; i++)
                await gate.VerifyAsync(SensitiveAction.DeleteWorker, WrongPassword);

            // القفل بيمنع حتى كلمة السر الصح — ده اللي بيوقف التخمين
            var afterLockout = await gate.VerifyAsync(SensitiveAction.DeleteWorker, OperationsPassword);

            Assert.False(afterLockout.IsAllowed);
            Assert.Contains("متقفلة", afterLockout.Message);
        }

        [Fact]
        public async Task Gate_stays_open_when_no_password_is_configured()
        {
            // البرنامج موجود على أجهزة شغّالة من قبل الميزة — قفلها فجأة
            // كان هيوقف المصنع
            var recordId = await RecordProductionAsync();

            using var scope = _db.CreateScope();
            var result = await _db.GetService<WorkdayCalculationService>(scope)
                .DeleteProductionAsync(recordId, "", "اتسجل بالغلط");

            Assert.True(result.IsDeleted);
            Assert.True(result.PasswordNotConfigured);
        }

        [Fact]
        public async Task Setting_the_password_turns_the_gate_on()
        {
            // ده تدفق شاشة الإعدادات بالظبط: قبل التسجيل البوابة مفتوحة،
            // وبعده بتمنع. من غير الخانة دي كل الحماية اللي اتبنت مقفولة
            // من غير مفتاح.
            using (var scope = _db.CreateScope())
                Assert.False(await _db.GetService<OperationsPasswordService>(scope).IsConfiguredAsync());

            await ConfigurePasswordAsync();

            using var check = _db.CreateScope();
            var gate = _db.GetService<OperationsPasswordService>(check);

            Assert.True(await gate.IsConfiguredAsync());
            Assert.False((await gate.VerifyAsync(SensitiveAction.DeleteWorker, WrongPassword)).IsAllowed);
        }

        [Fact]
        public async Task Changing_the_password_requires_the_current_one()
        {
            await ConfigurePasswordAsync();

            using var scope = _db.CreateScope();
            var gate = _db.GetService<OperationsPasswordService>(scope);

            // من غير الشرط ده، أي حد يقعد على الجهاز يغيّرها ويعدّي البوابة
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                gate.SetPasswordAsync(WrongPassword, "new-secret"));

            // والقديمة لسه شغالة — المحاولة الفاشلة ماغيّرتش حاجة
            Assert.True((await gate.VerifyAsync(SensitiveAction.DeleteWorker, OperationsPassword)).IsAllowed);
        }

        [Fact]
        public async Task Password_shorter_than_the_minimum_is_refused()
        {
            using var scope = _db.CreateScope();
            var gate = _db.GetService<OperationsPasswordService>(scope);

            await Assert.ThrowsAsync<InvalidOperationException>(() => gate.SetPasswordAsync(null, "ab"));
            Assert.False(await gate.IsConfiguredAsync());
        }

        // ======================= نظام 2: الحذف الناعم =======================

        [Fact]
        public async Task Deleting_a_record_keeps_the_row_and_records_who_when_why()
        {
            await ConfigurePasswordAsync();
            var recordId = await RecordProductionAsync();

            using (var scope = _db.CreateScope())
            {
                _db.GetService<CurrentUserContext>(scope).SignIn("admin", "مدير القسم");
                await _db.GetService<WorkdayCalculationService>(scope)
                    .DeleteProductionAsync(recordId, OperationsPassword, "اتسجل مرتين بالغلط");
            }

            using var check = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(check);

            // الصف موجود في الداتابيز — مش اتشال فعليًا
            var raw = await db.DailyProductions.IgnoreQueryFilters()
                .AsNoTracking().SingleAsync(r => r.Id == recordId);

            Assert.True(raw.IsDeleted);
            Assert.Equal("مدير القسم", raw.DeletedBy);
            Assert.Equal("اتسجل مرتين بالغلط", raw.DeletionReason);
            Assert.NotNull(raw.DeletedAt);
        }

        [Fact]
        public async Task Deleted_record_disappears_from_normal_queries()
        {
            await ConfigurePasswordAsync();
            var recordId = await RecordProductionAsync();

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope)
                    .DeleteProductionAsync(recordId, OperationsPassword, "غلط");

            // الفلتر العام بيشيله من كل استعلام من غير ما حد يكتب شرط
            Assert.Empty(await _db.GetProductionAsync());
        }

        [Fact]
        public async Task Deleting_a_worker_keeps_their_production_history_readable()
        {
            // ده الضمان الأهم في النظام كله: حذف عامل ميمسحش تاريخ أجوره
            await ConfigurePasswordAsync();
            await RecordProductionAsync();

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkerManagementService>(scope)
                    .DeleteWorkerAsync(TestDatabase.WorkerAhmedId, OperationsPassword, "ساب الشغل");

            using var check = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(check);

            // سجل الإنتاج لسه بيتقري ومعاه اسم صاحبه
            var record = await db.DailyProductions
                .Include(r => r.Worker)
                .AsNoTracking()
                .SingleAsync();

            Assert.NotNull(record.Worker);
            Assert.Equal("أحمد", record.Worker.FullName);

            // واللقطة محفوظة على العامل عشان العرض يقول "اتشال من النظام"
            var worker = await db.Workers.AsNoTracking()
                .SingleAsync(w => w.Id == TestDatabase.WorkerAhmedId);
            Assert.True(worker.IsDeleted);
            Assert.Equal("أحمد", worker.DeletedName);
            Assert.False(worker.IsActive); // اختفى من القوايم النشطة كمان
        }

        [Fact]
        public async Task Deletion_without_a_reason_is_refused()
        {
            await ConfigurePasswordAsync();
            var recordId = await RecordProductionAsync();

            using var scope = _db.CreateScope();
            var result = await _db.GetService<WorkdayCalculationService>(scope)
                .DeleteProductionAsync(recordId, OperationsPassword, "   ");

            Assert.False(result.IsDeleted);
            Assert.Contains("سبب", result.Message);
        }

        // ======================= نظام 3: سجل العمليات =======================

        [Fact]
        public async Task Deletion_writes_one_event_with_actor_reason_and_time()
        {
            await ConfigurePasswordAsync();
            var recordId = await RecordProductionAsync();

            using (var scope = _db.CreateScope())
            {
                _db.GetService<CurrentUserContext>(scope).SignIn("admin", "مدير القسم");
                await _db.GetService<WorkdayCalculationService>(scope)
                    .DeleteProductionAsync(recordId, OperationsPassword, "مكرر");
            }

            using var check = _db.CreateScope();
            var events = await _db.GetService<ActivityLogService>(check).GetRecentAsync();

            var logged = Assert.Single(events);
            Assert.Equal(ActivityEventType.ProductionRecordDeleted, logged.EventType);
            Assert.Equal("مدير القسم", logged.Actor);
            Assert.Equal("مكرر", logged.Reason);
            Assert.Equal(nameof(DailyProduction), logged.EntityType);
        }

        [Fact]
        public async Task Deletion_and_its_event_are_saved_together()
        {
            await ConfigurePasswordAsync();
            var recordId = await RecordProductionAsync();

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope)
                    .DeleteProductionAsync(recordId, OperationsPassword, "غلط");

            using var check = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(check);

            // الاتنين موجودين — الحذف والحدث في معاملة واحدة
            var deleted = await db.DailyProductions.IgnoreQueryFilters()
                .AsNoTracking().SingleAsync();
            Assert.True(deleted.IsDeleted);
            Assert.Equal(1, await db.ActivityEvents.CountAsync());
        }

        // ======================= نظام 4: تقييم المهارة =======================

        [Fact]
        public void Rating_maps_to_the_right_level()
        {
            Assert.Equal(SkillLevel.Expert, SkillRatingService.LevelFor(1.20m));
            Assert.Equal(SkillLevel.Proficient, SkillRatingService.LevelFor(1.00m));
            Assert.Equal(SkillLevel.Beginner, SkillRatingService.LevelFor(0.50m));
        }

        [Fact]
        public async Task Manual_rating_is_stored_with_manual_source()
        {
            using (var scope = _db.CreateScope())
                await _db.GetService<SkillRatingService>(scope)
                    .SetManualRatingAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 1.25m);

            using var check = _db.CreateScope();
            var ranked = await _db.GetService<SkillRatingService>(check)
                .GetRankedForStageAsync(TestDatabase.BagStage1Id);

            var ahmed = ranked.Single(r => r.WorkerId == TestDatabase.WorkerAhmedId);
            Assert.Equal(1.25m, ahmed.RatingValue);
            Assert.Equal(SkillRatingSource.Manual, ahmed.Source);
            Assert.Equal(SkillLevel.Expert, ahmed.Level);
            Assert.Equal("تقدير يدوي", ahmed.SourceText);
        }

        [Fact]
        public void Auto_rating_needs_enough_days_before_it_replaces_a_human_estimate()
        {
            // يوم واحد شاذ ميصحش يقلب تقييم عامل
            var oneDay = new[] { Record(TestDatabase.Today, 20, 10) };
            Assert.Null(SkillRatingService.ComputeFromRecords(oneDay));

            var threeDays = new[]
            {
                Record(TestDatabase.Today, 12, 10),
                Record(TestDatabase.Today.AddDays(-1), 10, 10),
                Record(TestDatabase.Today.AddDays(-2), 14, 10)
            };

            var computed = SkillRatingService.ComputeFromRecords(threeDays);
            Assert.NotNull(computed);
            Assert.Equal(1.20m, computed!.Value.Rating); // (1.2 + 1.0 + 1.4) ÷ 3
            Assert.Equal(3, computed.Value.Days);
        }

        [Fact]
        public void Same_day_records_count_as_one_day()
        {
            // عامل اتسجل له سجلين نص كوتة عمل كوتة كاملة في اليوم ده،
            // مش نص كوتة مرتين
            var twoRecordsOneDay = new[]
            {
                Record(TestDatabase.Today, 5, 10),
                Record(TestDatabase.Today, 5, 10),
                Record(TestDatabase.Today.AddDays(-1), 10, 10),
                Record(TestDatabase.Today.AddDays(-2), 10, 10)
            };

            var computed = SkillRatingService.ComputeFromRecords(twoRecordsOneDay);
            Assert.Equal(1.00m, computed!.Value.Rating);
            Assert.Equal(3, computed.Value.Days);
        }

        [Fact]
        public async Task Auto_rating_overrides_manual_but_keeps_it_visible()
        {
            using (var scope = _db.CreateScope())
                await _db.GetService<SkillRatingService>(scope)
                    .SetManualRatingAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 1.30m);

            // 3 أيام إنتاج على نص الكوتة
            for (var day = 0; day < 3; day++)
            {
                var date = TestDatabase.Today.AddDays(-day);
                using var scope = _db.CreateScope();
                await _db.GetService<WorkdayCalculationService>(scope).RecordProductionAsync(
                    TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 5, date);
            }

            using (var scope = _db.CreateScope())
                await _db.GetService<SkillRatingService>(scope)
                    .RecalculateForWorkerAsync(TestDatabase.WorkerAhmedId, TestDatabase.Today);

            using var check = _db.CreateScope();
            var ahmed = (await _db.GetService<SkillRatingService>(check)
                    .GetRankedForStageAsync(TestDatabase.BagStage1Id))
                .Single(r => r.WorkerId == TestDatabase.WorkerAhmedId);

            Assert.Equal(SkillRatingSource.Auto, ahmed.Source);
            Assert.Equal(0.50m, ahmed.RatingValue);
            Assert.Equal(3, ahmed.SampleDays);

            // التقدير البشري مضاعش — الواجهة بتقدر تعرض الاتنين
            Assert.Equal(1.30m, ahmed.LastManualValue);
            Assert.True(ahmed.OverridesManualValue);
            Assert.Contains("3 يوم", ahmed.SourceText);
        }

        [Fact]
        public async Task Workers_are_ranked_best_to_worst_for_a_stage()
        {
            using var scope = _db.CreateScope();
            var rating = _db.GetService<SkillRatingService>(scope);

            await rating.SetManualRatingAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 0.80m);
            await rating.SetManualRatingAsync(TestDatabase.WorkerSaidId, TestDatabase.BagStage1Id, 1.40m);

            var ranked = await rating.GetRankedForStageAsync(TestDatabase.BagStage1Id);

            Assert.Equal(TestDatabase.WorkerSaidId, ranked[0].WorkerId);
            Assert.Equal(TestDatabase.WorkerAhmedId, ranked[1].WorkerId);
        }

        [Fact]
        public void Product_rating_averages_only_the_stages_the_worker_knows()
        {
            // المتخصص في 3 مراحل من 11 مش ضعيف — المراحل اللي مالوش فيها
            // مهارة مبتتحسبش صفر
            var skills = new[]
            {
                new WorkerSkill { RatingValue = 1.20m },
                new WorkerSkill { RatingValue = 1.00m }
            };

            Assert.Equal(1.10m, SkillRatingService.ProductRating(skills));
            Assert.Null(SkillRatingService.ProductRating(Array.Empty<WorkerSkill>()));
        }

        // ======================= التدفق المشترك =======================

        [Fact]
        public async Task One_deletion_runs_gate_then_soft_delete_then_event()
        {
            await ConfigurePasswordAsync();
            var recordId = await RecordProductionAsync();

            using (var scope = _db.CreateScope())
            {
                _db.GetService<CurrentUserContext>(scope).SignIn("admin", "مدير القسم");
                var result = await _db.GetService<WorkdayCalculationService>(scope)
                    .DeleteProductionAsync(recordId, OperationsPassword, "تصحيح إدخال");

                Assert.True(result.IsDeleted);            // 1) البوابة عدّت
            }

            using var check = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(check);

            var raw = await db.DailyProductions.IgnoreQueryFilters().AsNoTracking().SingleAsync();
            Assert.True(raw.IsDeleted);                   // 2) حذف ناعم مش فعلي
            Assert.Equal("تصحيح إدخال", raw.DeletionReason);

            var logged = await db.ActivityEvents.AsNoTracking().SingleAsync();
            Assert.Equal("مدير القسم", logged.Actor);     // 3) الحدث اتسجل
            Assert.Equal("تصحيح إدخال", logged.Reason);
        }

        /// <summary>سجل إنتاج في الذاكرة للاختبارات النقية</summary>
        private static DailyProduction Record(DateTime date, int pieces, int quota) => new()
        {
            ProductionStageId = TestDatabase.BagStage1Id,
            Date = date,
            PieceCount = pieces,
            PiecesPerWorkdayAtEntry = quota
        };
    }
}
