using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// خطط الإنتاج المتأجّلة ("الذاكرة") — والاستثناء الوحيد في البرنامج
    /// لقاعدة "ترتيب الخط هو SortOrder".
    ///
    /// أخطر حاجة هنا مش إن الترتيب المخصص يشتغل، ده الغرض. الخطر إنه
    /// **يوصل لحتة مكانش المفروض يوصلها**: حساب فجوات الخط، التقارير،
    /// أو الجلسة العادية اللي بعده. القسم الأخير هو اللي بيغطي ده.
    /// </summary>
    public class ProductionMemoryTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Today => TestDatabase.Today;

        // شنطة: قص(4) → خياطة(5) → تشطيب(6) — التلات مراحل بترتيبها الحقيقي
        private static int[] RealBagOrder =>
            new[] { TestDatabase.BagStage1Id, TestDatabase.BagStage2Id, TestDatabase.BagStage3Id };

        private ProductionMemoryService Memories(IServiceScope scope) =>
            _db.GetService<ProductionMemoryService>(scope);

        // ======================= الخطة نفسها =======================

        [Fact]
        public async Task A_plan_keeps_the_order_the_user_chose_not_the_products_own()
        {
            using var scope = _db.CreateScope();

            // ترتيب مقلوب تمامًا عن ترتيب المنتج
            var reversed = new[] { TestDatabase.BagStage3Id, TestDatabase.BagStage2Id, TestDatabase.BagStage1Id };

            var id = await Memories(scope).CreateAsync(
                TestDatabase.ProductBagId, reversed, "أعمل الشنطة بالمقلوب", Today.AddDays(3));

            var plan = await Memories(scope).GetAsync(id);

            Assert.NotNull(plan);
            Assert.Equal(reversed, plan!.Stages.Select(s => s.ProductionStageId).ToArray());
            Assert.Equal(new[] { 1, 2, 3 }, plan.Stages.Select(s => s.Position).ToArray());
        }

        [Fact]
        public async Task A_plan_may_leave_stages_out()
        {
            // قرار مؤكد مع المستخدم: الخطة ممكن تتخطى مراحل عن قصد
            using var scope = _db.CreateScope();

            var id = await Memories(scope).CreateAsync(
                TestDatabase.ProductBagId,
                new[] { TestDatabase.BagStage1Id, TestDatabase.BagStage3Id },
                "من غير خياطة", Today);

            var plan = await Memories(scope).GetAsync(id);

            Assert.Equal(2, plan!.Stages.Count);
            Assert.True(plan.CanStart);
        }

        [Fact]
        public async Task A_plan_cannot_repeat_a_stage()
        {
            // مرحلة مرتين في نفس الخط = تسجيل مزدوج لليوميات والأجور
            using var scope = _db.CreateScope();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Memories(scope).CreateAsync(
                    TestDatabase.ProductBagId,
                    new[] { TestDatabase.BagStage1Id, TestDatabase.BagStage1Id },
                    "", Today));
        }

        [Fact]
        public async Task A_plan_cannot_borrow_a_stage_from_another_product()
        {
            using var scope = _db.CreateScope();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Memories(scope).CreateAsync(
                    TestDatabase.ProductBagId,
                    new[] { TestDatabase.BagStage1Id, TestDatabase.RingStage1Id },
                    "", Today));
        }

        [Fact]
        public async Task An_empty_plan_is_refused()
        {
            using var scope = _db.CreateScope();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Memories(scope).CreateAsync(TestDatabase.ProductBagId, Array.Empty<int>(), "", Today));
        }

        // ======================= التذكير =======================

        [Fact]
        public async Task An_overdue_reminder_still_fires()
        {
            // المصنع ممكن يقفل أسبوع — التذكير المتأخر لازم يفضل يفكّر،
            // مش يعدّي في صمت لأن يومه فات
            using var scope = _db.CreateScope();

            await Memories(scope).CreateAsync(
                TestDatabase.ProductBagId, RealBagOrder, "فات من أسبوع", Today.AddDays(-7));

            var due = await Memories(scope).GetDueAsync(Today);

            Assert.Single(due);
        }

        [Fact]
        public async Task A_future_reminder_does_not_fire_yet()
        {
            using var scope = _db.CreateScope();

            await Memories(scope).CreateAsync(
                TestDatabase.ProductBagId, RealBagOrder, "بكرة", Today.AddDays(1));

            Assert.Empty(await Memories(scope).GetDueAsync(Today));
        }

        [Fact]
        public async Task Todays_reminder_fires_on_the_day_itself()
        {
            using var scope = _db.CreateScope();

            await Memories(scope).CreateAsync(
                TestDatabase.ProductBagId, RealBagOrder, "النهارده", Today);

            Assert.Single(await Memories(scope).GetDueAsync(Today));
        }

        [Fact]
        public async Task Several_overdue_reminders_all_come_back_oldest_first()
        {
            using var scope = _db.CreateScope();

            await Memories(scope).CreateAsync(TestDatabase.ProductBagId, RealBagOrder, "أقدم", Today.AddDays(-5));
            await Memories(scope).CreateAsync(TestDatabase.ProductRingId,
                new[] { TestDatabase.RingStage1Id }, "أحدث", Today.AddDays(-1));
            await Memories(scope).CreateAsync(TestDatabase.ProductChainId,
                new[] { TestDatabase.ChainStage1Id }, "لسه بدري", Today.AddDays(4));

            var due = await Memories(scope).GetDueAsync(Today);

            Assert.Equal(2, due.Count);
            Assert.Equal("أقدم", due[0].Notes);
            Assert.Equal("أحدث", due[1].Notes);
        }

        [Fact]
        public async Task Starting_a_plan_moves_it_to_the_done_list_and_stops_the_reminder()
        {
            // بيحصل بمجرد فتح الشاشة، من غير ما يتسجّل أي إنتاج
            using var scope = _db.CreateScope();

            var id = await Memories(scope).CreateAsync(
                TestDatabase.ProductBagId, RealBagOrder, "", Today);

            await Memories(scope).MarkStartedAsync(id);

            Assert.Empty(await Memories(scope).GetDueAsync(Today));
            Assert.Empty(await Memories(scope).GetActiveAsync());
            Assert.Single(await Memories(scope).GetCompletedAsync());
        }

        [Fact]
        public async Task Postponing_keeps_everything_else_and_fires_again_on_the_new_day()
        {
            using var scope = _db.CreateScope();

            var id = await Memories(scope).CreateAsync(
                TestDatabase.ProductBagId, RealBagOrder, "ملاحظة مهمة", Today);

            await Memories(scope).PostponeAsync(id, Today.AddDays(3));

            Assert.Empty(await Memories(scope).GetDueAsync(Today));

            var later = await Memories(scope).GetDueAsync(Today.AddDays(3));
            var plan = Assert.Single(later);

            Assert.Equal("ملاحظة مهمة", plan.Notes);
            Assert.Equal(RealBagOrder, plan.Stages.Select(s => s.ProductionStageId).ToArray());
        }

        // ======================= خطة باتت =======================

        [Fact]
        public async Task A_plan_whose_product_was_deactivated_still_shows_but_cannot_start()
        {
            using var scope = _db.CreateScope();

            var id = await Memories(scope).CreateAsync(
                TestDatabase.ProductBagId, RealBagOrder, "", Today);

            var db = _db.GetService<AppDbContext>(scope);
            (await db.Products.FirstAsync(p => p.Id == TestDatabase.ProductBagId)).IsActive = false;
            await db.SaveChangesAsync();

            using var fresh = _db.CreateScope();
            var due = await Memories(fresh).GetDueAsync(Today);

            // بيظهر — مش بيتشال في صمت
            var plan = Assert.Single(due);
            Assert.False(plan.CanStart);
            Assert.Contains("موقوف", plan.BlockedReason!);

            // ومحاولة فتح جلسة منه بترفض بدل ما تفتح شاشة مكسورة
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Memories(fresh).GetStageOrderForSessionAsync(id));
        }

        [Fact]
        public async Task A_plan_whose_product_was_deleted_still_shows_but_cannot_start()
        {
            using var scope = _db.CreateScope();

            var id = await Memories(scope).CreateAsync(
                TestDatabase.ProductBagId, RealBagOrder, "", Today);

            var db = _db.GetService<AppDbContext>(scope);
            (await db.Products.FirstAsync(p => p.Id == TestDatabase.ProductBagId)).IsDeleted = true;
            await db.SaveChangesAsync();

            using var fresh = _db.CreateScope();
            var plan = Assert.Single(await Memories(fresh).GetDueAsync(Today));

            Assert.False(plan.CanStart);
            Assert.Contains("اتشال", plan.BlockedReason!);
        }

        [Fact]
        public async Task A_plan_whose_stage_was_deactivated_cannot_start()
        {
            using var scope = _db.CreateScope();

            var id = await Memories(scope).CreateAsync(
                TestDatabase.ProductBagId, RealBagOrder, "", Today);

            var db = _db.GetService<AppDbContext>(scope);
            (await db.Set<ProductionStage>().FirstAsync(s => s.Id == TestDatabase.BagStage2Id)).IsActive = false;
            await db.SaveChangesAsync();

            using var fresh = _db.CreateScope();
            var plan = Assert.Single(await Memories(fresh).GetDueAsync(Today));

            Assert.False(plan.CanStart);

            // والمرحلة الواقعة متعلّمة عشان الشاشة توضّح مكان المشكلة
            Assert.Contains(plan.Stages, s =>
                s.ProductionStageId == TestDatabase.BagStage2Id && !s.IsStillInLine);
        }
    }
}
