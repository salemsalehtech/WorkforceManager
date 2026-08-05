using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
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

        [Fact]
        public async Task Deleting_a_product_keeps_its_production_history()
        {
            await ConfigurePasswordAsync();
            await RecordProductionAsync();

            using (var scope = _db.CreateScope())
                await _db.GetService<ProductManagementService>(scope)
                    .DeleteProductAsync(TestDatabase.ProductBagId, OperationsPassword, "وقف إنتاجه خلاص");

            using var check = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(check);

            var product = await db.Products.AsNoTracking()
                .SingleAsync(p => p.Id == TestDatabase.ProductBagId);
            Assert.True(product.IsDeleted);
            Assert.Equal("شنطة", product.DeletedName);
            Assert.False(product.IsActive);

            // سجل الإنتاج لسه بيوصل لمنتجه عبر المرحلة
            var record = await db.DailyProductions
                .Include(r => r.ProductionStage).ThenInclude(s => s.Product)
                .AsNoTracking().SingleAsync();
            Assert.Equal("شنطة", record.ProductionStage.Product.Name);
        }

        [Fact]
        public async Task Deleting_a_stage_does_not_touch_recorded_wages()
        {
            await ConfigurePasswordAsync();
            await RecordProductionAsync(pieces: 100);

            using (var scope = _db.CreateScope())
                await _db.GetService<ProductManagementService>(scope)
                    .DeleteStageAsync(TestDatabase.BagStage1Id, OperationsPassword, "المرحلة اتلغت من الخط");

            // اليومية محفوظة كـ Snapshot على السجل، فحذف المرحلة مبيغيّرش أجر حد
            var record = Assert.Single(await _db.GetProductionAsync());
            Assert.Equal(10m, record.WorkdaysCompleted);
        }

        [Fact]
        public async Task Deleting_a_product_without_the_password_is_blocked()
        {
            await ConfigurePasswordAsync();

            using var scope = _db.CreateScope();
            var result = await _db.GetService<ProductManagementService>(scope)
                .DeleteProductAsync(TestDatabase.ProductBagId, WrongPassword, "تجربة");

            Assert.False(result.IsDeleted);

            var db = _db.GetService<AppDbContext>(scope);
            var product = await db.Products.AsNoTracking()
                .SingleAsync(p => p.Id == TestDatabase.ProductBagId);
            Assert.False(product.IsDeleted);
            Assert.True(product.IsActive);
        }

        [Fact]
        public async Task Deleted_worker_disappears_from_every_list_including_suspended()
        {
            // الباج اللي كان: الحذف بيعلّم العامل موقوف كمان، فكان بيقع في
            // فلتر "الموقوفين" ويرجع بزرار "إعادة تفعيل" — يعني الحذف
            // مالوش أي معنى
            await ConfigurePasswordAsync();

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkerManagementService>(scope)
                    .DeleteWorkerAsync(TestDatabase.WorkerAhmedId, OperationsPassword, "ساب الشغل");

            using var check = _db.CreateScope();
            var repo = _db.GetService<IWorkerRepository>(check);

            var all = await repo.GetAllWithSkillsAsync();       // قايمة "الكل" (فيها الموقوفين)
            var active = await repo.GetActiveWithSkillsAsync(); // قايمة النشطين

            Assert.DoesNotContain(all, w => w.Id == TestDatabase.WorkerAhmedId);
            Assert.DoesNotContain(active, w => w.Id == TestDatabase.WorkerAhmedId);
        }

        [Fact]
        public async Task Deleted_product_disappears_from_every_list_including_suspended()
        {
            await ConfigurePasswordAsync();

            using (var scope = _db.CreateScope())
                await _db.GetService<ProductManagementService>(scope)
                    .DeleteProductAsync(TestDatabase.ProductBagId, OperationsPassword, "وقف إنتاجه");

            using var check = _db.CreateScope();
            var repo = _db.GetService<IProductRepository>(check);

            Assert.DoesNotContain(await repo.GetAllWithStagesAsync(), p => p.Id == TestDatabase.ProductBagId);
            Assert.DoesNotContain(await repo.GetActiveWithStagesAsync(), p => p.Id == TestDatabase.ProductBagId);
        }

        [Fact]
        public async Task Deleted_stage_disappears_from_its_product_line()
        {
            await ConfigurePasswordAsync();

            using (var scope = _db.CreateScope())
                await _db.GetService<ProductManagementService>(scope)
                    .DeleteStageAsync(TestDatabase.BagStage2Id, OperationsPassword, "اتلغت من الخط");

            using var check = _db.CreateScope();
            var product = await _db.GetService<IProductRepository>(check)
                .GetWithStagesAsync(TestDatabase.ProductBagId);

            Assert.NotNull(product);
            Assert.DoesNotContain(product!.Stages, s => s.Id == TestDatabase.BagStage2Id);
            Assert.Equal(2, product.Stages.Count); // فضل مرحلتين من التلاتة
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
        public void Performance_maps_to_the_right_star_count()
        {
            Assert.Equal(5, SkillRatingService.StarsForRatio(1.50m)); // بيعمل الكوتة ونص
            Assert.Equal(4, SkillRatingService.StarsForRatio(1.20m));
            Assert.Equal(3, SkillRatingService.StarsForRatio(1.00m)); // الكوتة بالظبط
            Assert.Equal(2, SkillRatingService.StarsForRatio(0.75m));
            Assert.Equal(1, SkillRatingService.StarsForRatio(0.40m));
        }

        [Fact]
        public async Task Manager_stars_are_stored_with_who_and_when()
        {
            using (var scope = _db.CreateScope())
            {
                _db.GetService<CurrentUserContext>(scope).SignIn("admin", "مدير القسم");
                await _db.GetService<SkillRatingService>(scope)
                    .SetStarsAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 5);
            }

            using var check = _db.CreateScope();
            var skill = await _db.GetService<IWorkerSkillRepository>(check)
                .GetAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id);

            Assert.Equal(5, skill!.Stars);
            Assert.Equal("مدير القسم", skill.StarsUpdatedBy);
            Assert.NotNull(skill.StarsUpdatedAt);
        }

        [Fact]
        public async Task Stars_outside_one_to_five_are_refused()
        {
            using var scope = _db.CreateScope();
            var rating = _db.GetService<SkillRatingService>(scope);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                rating.SetStarsAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 0));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                rating.SetStarsAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 6));
        }

        [Fact]
        public void Measurement_needs_enough_days_to_mean_anything()
        {
            // يوم واحد شاذ ميصحش يبقى أساس اقتراح تعديل تقييم
            var oneDay = new[] { Record(TestDatabase.Today, 20, 10) };
            Assert.Null(SkillRatingService.MeasureFromRecords(oneDay));

            var threeDays = new[]
            {
                Record(TestDatabase.Today, 12, 10),
                Record(TestDatabase.Today.AddDays(-1), 10, 10),
                Record(TestDatabase.Today.AddDays(-2), 14, 10)
            };

            var measured = SkillRatingService.MeasureFromRecords(threeDays);
            Assert.NotNull(measured);
            Assert.Equal(1.20m, measured!.Value.Ratio); // (1.2 + 1.0 + 1.4) ÷ 3
            Assert.Equal(3, measured.Value.Days);
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

            var measured = SkillRatingService.MeasureFromRecords(twoRecordsOneDay);
            Assert.Equal(1.00m, measured!.Value.Ratio);
            Assert.Equal(3, measured.Value.Days);
        }

        [Fact]
        public async Task Measuring_never_touches_the_managers_stars()
        {
            // ده الضمان الأساسي في النظام: النظام بيقيس، والمدير بس هو
            // اللي بيقيّم. رقم بيتقلب من ورا ظهر المدير بيخليه ميثقش فيه
            using (var scope = _db.CreateScope())
                await _db.GetService<SkillRatingService>(scope)
                    .SetStarsAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 5);

            // 3 أيام إنتاج على نص الكوتة — أداء ضعيف جدًا
            for (var day = 0; day < 3; day++)
            {
                using var scope = _db.CreateScope();
                await _db.GetService<WorkdayCalculationService>(scope).RecordProductionAsync(
                    TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 5,
                    TestDatabase.Today.AddDays(-day));
            }

            using (var scope = _db.CreateScope())
                await _db.GetService<SkillRatingService>(scope).MeasureAllAsync(TestDatabase.Today);

            using var check = _db.CreateScope();
            var skill = await _db.GetService<IWorkerSkillRepository>(check)
                .GetAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id);

            Assert.Equal(5, skill!.Stars);          // النجوم زي ما هي
            Assert.Equal(0.50m, skill.MeasuredRatio); // والقياس اتحدّث جنبها
            Assert.Equal(3, skill.MeasuredDays);
        }

        [Fact]
        public async Task Monthly_review_flags_the_gap_between_stars_and_reality()
        {
            using (var scope = _db.CreateScope())
                await _db.GetService<SkillRatingService>(scope)
                    .SetStarsAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 5);

            // بيعمل نص الكوتة بس — يستاهل نجمة واحدة
            for (var day = 0; day < 3; day++)
            {
                using var scope = _db.CreateScope();
                await _db.GetService<WorkdayCalculationService>(scope).RecordProductionAsync(
                    TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 5,
                    TestDatabase.Today.AddDays(-day));
            }

            using var check = _db.CreateScope();
            var review = await _db.GetService<SkillRatingService>(check)
                .BuildReviewAsync(TestDatabase.Today);

            var suggestion = Assert.Single(review.Suggestions);
            Assert.Equal(TestDatabase.WorkerAhmedId, suggestion.WorkerId);
            Assert.Equal(5, suggestion.CurrentStars);
            Assert.Equal(1, suggestion.SuggestedStars);
            Assert.False(suggestion.IsUpgrade);
            Assert.Equal(1, review.DowngradeCount);
        }

        [Fact]
        public async Task Review_stays_quiet_when_stars_match_reality()
        {
            // المدير مش محتاج يبص على اللي مظبوط أصلاً
            using (var scope = _db.CreateScope())
                await _db.GetService<SkillRatingService>(scope)
                    .SetStarsAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 3);

            // بيعمل الكوتة بالظبط = 3 نجوم = نفس تقييمه
            for (var day = 0; day < 3; day++)
            {
                using var scope = _db.CreateScope();
                await _db.GetService<WorkdayCalculationService>(scope).RecordProductionAsync(
                    TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 10,
                    TestDatabase.Today.AddDays(-day));
            }

            using var check = _db.CreateScope();
            var review = await _db.GetService<SkillRatingService>(check)
                .BuildReviewAsync(TestDatabase.Today);

            Assert.False(review.HasSuggestions);
        }

        [Fact]
        public async Task A_never_rated_skill_is_offered_for_confirmation_even_when_it_matches()
        {
            // من غير SetStarsAsync: النجوم اللي على المهارة حطها الترحيل،
            // مش المدير. لو سكتنا عنها لأنها "مظبوطة"، مصنع لسه بادئ عمره
            // ما هيشوف تنبيه المراجعة أصلاً — كل تقييماته مبدئية ومتطابقة
            for (var day = 0; day < 3; day++)
            {
                using var scope = _db.CreateScope();
                await _db.GetService<WorkdayCalculationService>(scope).RecordProductionAsync(
                    TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 10,
                    TestDatabase.Today.AddDays(-day));
            }

            using var check = _db.CreateScope();
            var rating = _db.GetService<SkillRatingService>(check);
            var review = await rating.BuildReviewAsync(TestDatabase.Today);

            var suggestion = Assert.Single(review.Suggestions);
            Assert.True(suggestion.IsConfirmation);
            Assert.True(suggestion.IsUnrated);
            Assert.False(suggestion.IsUpgrade);
            Assert.False(suggestion.IsDowngrade);
            Assert.Equal(suggestion.CurrentStars, suggestion.SuggestedStars);
            Assert.Equal(1, review.ConfirmationCount);

            // وبعد ما المدير يأكّد، بتسكت للأبد — التأكيد بيختم
            // StarsUpdatedAt حتى لو الرقم نفسه مااتغيرش
            await rating.ApplySuggestionAsync(suggestion);

            Assert.False((await rating.BuildReviewAsync(TestDatabase.Today)).HasSuggestions);
        }

        [Fact]
        public async Task Applying_a_suggestion_sets_the_stars()
        {
            using (var scope = _db.CreateScope())
                await _db.GetService<SkillRatingService>(scope)
                    .SetStarsAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 1);

            // بيعمل الكوتة وزيادة 50% — يستاهل 5 نجوم
            for (var day = 0; day < 3; day++)
            {
                using var scope = _db.CreateScope();
                await _db.GetService<WorkdayCalculationService>(scope).RecordProductionAsync(
                    TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 15,
                    TestDatabase.Today.AddDays(-day));
            }

            using var scope2 = _db.CreateScope();
            var rating = _db.GetService<SkillRatingService>(scope2);
            var review = await rating.BuildReviewAsync(TestDatabase.Today);

            var suggestion = Assert.Single(review.Suggestions);
            Assert.True(suggestion.IsUpgrade);

            await rating.ApplySuggestionAsync(suggestion);

            var skill = await _db.GetService<IWorkerSkillRepository>(scope2)
                .GetAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id);
            Assert.Equal(5, skill!.Stars);
        }

        [Fact]
        public async Task Ignoring_a_suggestion_leaves_the_stars_untouched()
        {
            // "سيبه زي ما هو" لازم يبقى قرار حقيقي: التقييم مايتغيرش،
            // والاقتراح يرجع الشهر الجاي لو الفرق لسه موجود
            using (var scope = _db.CreateScope())
                await _db.GetService<SkillRatingService>(scope)
                    .SetStarsAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 5);

            for (var day = 0; day < 3; day++)
            {
                using var scope = _db.CreateScope();
                await _db.GetService<WorkdayCalculationService>(scope).RecordProductionAsync(
                    TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 5,
                    TestDatabase.Today.AddDays(-day));
            }

            using var check = _db.CreateScope();
            var rating = _db.GetService<SkillRatingService>(check);

            // بنبني المراجعة مرتين من غير ما نطبّق حاجة
            Assert.Single((await rating.BuildReviewAsync(TestDatabase.Today)).Suggestions);
            Assert.Single((await rating.BuildReviewAsync(TestDatabase.Today)).Suggestions);

            var skill = await _db.GetService<IWorkerSkillRepository>(check)
                .GetAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id);
            Assert.Equal(5, skill!.Stars);
        }

        [Fact]
        public async Task Review_ignores_workers_who_are_no_longer_active()
        {
            // مفيش داعي المدير يراجع تقييم حد مش شغّال أصلاً
            using (var scope = _db.CreateScope())
                await _db.GetService<SkillRatingService>(scope)
                    .SetStarsAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 5);

            for (var day = 0; day < 3; day++)
            {
                using var scope = _db.CreateScope();
                await _db.GetService<WorkdayCalculationService>(scope).RecordProductionAsync(
                    TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 5,
                    TestDatabase.Today.AddDays(-day));
            }

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkerManagementService>(scope)
                    .DeactivateWorkerAsync(TestDatabase.WorkerAhmedId);

            using var check = _db.CreateScope();
            var review = await _db.GetService<SkillRatingService>(check)
                .BuildReviewAsync(TestDatabase.Today);

            Assert.False(review.HasSuggestions);
        }

        [Fact]
        public async Task Workers_are_ranked_by_stars_best_first()
        {
            using var scope = _db.CreateScope();
            var rating = _db.GetService<SkillRatingService>(scope);

            await rating.SetStarsAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 2);
            await rating.SetStarsAsync(TestDatabase.WorkerSaidId, TestDatabase.BagStage1Id, 5);

            var ranked = await rating.GetRankedForStageAsync(TestDatabase.BagStage1Id);

            Assert.Equal(TestDatabase.WorkerSaidId, ranked[0].WorkerId);
            Assert.Equal("★★★★★", ranked[0].StarsText);
            Assert.Equal(TestDatabase.WorkerAhmedId, ranked[1].WorkerId);
        }

        [Fact]
        public void Product_stars_average_only_the_stages_the_worker_knows()
        {
            // المتخصص في 3 مراحل من 11 مش ضعيف — المراحل اللي مالوش فيها
            // مهارة مبتتحسبش صفر
            var skills = new[]
            {
                new WorkerSkill { Stars = 5 },
                new WorkerSkill { Stars = 4 }
            };

            Assert.Equal(4.5m, SkillRatingService.ProductStars(skills));
            Assert.Null(SkillRatingService.ProductStars(Array.Empty<WorkerSkill>()));
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
