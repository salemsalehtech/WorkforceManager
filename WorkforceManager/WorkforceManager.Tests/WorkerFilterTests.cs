using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// تصفية العمال — القاعدة اللي بتقرر مين يبان في شاشة العمال.
    ///
    /// الفكرة اللي الاختبارات دي بتحرسها: **الفلاتر بتتجمع بـ AND**.
    /// كل فلتر مفعّل بيضيّق النتيجة ومبيلغيش اللي قبله، عشان "عمال
    /// مرحلة الدبلة + 4 نجوم فأكتر + حاضرين النهارده" يبقى سؤال واحد.
    ///
    /// القاعدة نقية تمامًا (مفيش داتابيز ولا واجهة) — عشان كده الاختبارات
    /// هنا مالهاش TestDatabase.
    /// </summary>
    public class WorkerFilterTests
    {
        private const int RingStage = 1;
        private const int ChainStage = 3;
        private const int RingProduct = 1;
        private const int ChainProduct = 2;

        private static WorkerFilterSubject Worker(
            int id = 1,
            bool isActive = true,
            bool isHourly = false,
            int[]? stages = null,
            int[]? products = null,
            decimal stars = 0m,
            AttendanceStatus? today = null) => new()
            {
                WorkerId = id,
                IsActive = isActive,
                IsHourly = isHourly,
                StageIds = (stages ?? Array.Empty<int>()).ToHashSet(),
                ProductIds = (products ?? Array.Empty<int>()).ToHashSet(),
                AverageStars = stars,
                TodayStatus = today
            };

        // ======================= الشريحة الأساسية =======================

        [Fact]
        public void Default_scope_shows_active_workers_only()
        {
            var criteria = new WorkerFilterCriteria();

            Assert.True(WorkerFilterRules.Matches(Worker(isActive: true), criteria));
            Assert.False(WorkerFilterRules.Matches(Worker(isActive: false), criteria));
        }

        [Fact]
        public void Production_scope_excludes_hourly_workers()
        {
            var criteria = new WorkerFilterCriteria { Scope = WorkerPayScope.ByProduction };

            Assert.True(WorkerFilterRules.Matches(Worker(isHourly: false), criteria));
            Assert.False(WorkerFilterRules.Matches(Worker(isHourly: true), criteria));
        }

        [Fact]
        public void Inactive_scope_shows_only_stopped_workers()
        {
            var criteria = new WorkerFilterCriteria { Scope = WorkerPayScope.Inactive };

            Assert.True(WorkerFilterRules.Matches(Worker(isActive: false), criteria));
            Assert.False(WorkerFilterRules.Matches(Worker(isActive: true), criteria));
        }

        // ======================= كل فلتر لوحده =======================

        [Fact]
        public void Stage_filter_keeps_only_qualified_workers()
        {
            var criteria = new WorkerFilterCriteria { StageId = RingStage };

            Assert.True(WorkerFilterRules.Matches(Worker(stages: new[] { RingStage }), criteria));
            Assert.False(WorkerFilterRules.Matches(Worker(stages: new[] { ChainStage }), criteria));
            Assert.False(WorkerFilterRules.Matches(Worker(), criteria)); // مالوش مهارات
        }

        [Fact]
        public void Product_filter_keeps_workers_with_any_skill_on_it()
        {
            var criteria = new WorkerFilterCriteria { ProductId = RingProduct };

            Assert.True(WorkerFilterRules.Matches(Worker(products: new[] { RingProduct }), criteria));
            Assert.False(WorkerFilterRules.Matches(Worker(products: new[] { ChainProduct }), criteria));
        }

        [Fact]
        public void Stars_filter_is_a_minimum_not_an_exact_match()
        {
            var criteria = new WorkerFilterCriteria { MinStars = 4 };

            Assert.True(WorkerFilterRules.Matches(Worker(stars: 5m), criteria));
            Assert.True(WorkerFilterRules.Matches(Worker(stars: 4m), criteria));
            Assert.False(WorkerFilterRules.Matches(Worker(stars: 3.9m), criteria));
        }

        [Fact]
        public void Worker_with_no_skills_is_out_of_any_stars_filter()
        {
            // متوسط صفر معناه "مفيش مهارات"، مش "تقييمه صفر". العامل ده
            // خارج السؤال أصلاً — وإلا كان هيبان في فلتر "نجمتين فأكتر"
            var criteria = new WorkerFilterCriteria { MinStars = 2 };

            Assert.False(WorkerFilterRules.Matches(Worker(stars: 0m), criteria));
        }

        [Fact]
        public void Attendance_filter_matches_the_exact_status()
        {
            var criteria = new WorkerFilterCriteria { TodayStatus = AttendanceStatus.Present };

            Assert.True(WorkerFilterRules.Matches(Worker(today: AttendanceStatus.Present), criteria));
            Assert.False(WorkerFilterRules.Matches(
                Worker(today: AttendanceStatus.AbsentWithoutPermission), criteria));
        }

        [Fact]
        public void Unrecorded_attendance_is_not_the_same_as_any_status()
        {
            // عامل محدش سجّله مبيتحسبش حاضر ولا غايب
            Assert.False(WorkerFilterRules.Matches(
                Worker(today: null),
                new WorkerFilterCriteria { TodayStatus = AttendanceStatus.Present }));

            Assert.False(WorkerFilterRules.Matches(
                Worker(today: null),
                new WorkerFilterCriteria { TodayStatus = AttendanceStatus.AbsentWithoutPermission }));
        }

        // ======================= التجميع بـ AND =======================

        [Fact]
        public void Filters_combine_with_and_not_or()
        {
            var criteria = new WorkerFilterCriteria
            {
                StageId = RingStage,
                MinStars = 4,
                TodayStatus = AttendanceStatus.Present
            };

            // مطابق للتلاتة
            Assert.True(WorkerFilterRules.Matches(
                Worker(stages: new[] { RingStage }, stars: 4.5m, today: AttendanceStatus.Present),
                criteria));

            // مطابق لاتنين بس — لو كانت OR كان هيعدّي
            Assert.False(WorkerFilterRules.Matches(
                Worker(stages: new[] { RingStage }, stars: 4.5m, today: null),
                criteria));

            Assert.False(WorkerFilterRules.Matches(
                Worker(stages: new[] { ChainStage }, stars: 4.5m, today: AttendanceStatus.Present),
                criteria));

            Assert.False(WorkerFilterRules.Matches(
                Worker(stages: new[] { RingStage }, stars: 2m, today: AttendanceStatus.Present),
                criteria));
        }

        [Fact]
        public void An_unset_filter_does_not_narrow_anything()
        {
            // null معناه "الفلتر مش مفعّل" مش "دوّر على قيمة فاضية" —
            // لو الفرق ده اتكسر، فتح الشاشة كان هيدي قايمة فاضية
            var worker = Worker(stages: new[] { RingStage }, stars: 3m, today: null);

            Assert.True(WorkerFilterRules.Matches(worker, new WorkerFilterCriteria()));
        }

        [Fact]
        public void Extra_filters_still_respect_the_scope()
        {
            // فلتر المرحلة مش بيرجّع عامل موقوف في شريحة "النشطين"
            var criteria = new WorkerFilterCriteria { StageId = RingStage };

            Assert.False(WorkerFilterRules.Matches(
                Worker(isActive: false, stages: new[] { RingStage }), criteria));
        }

        // ======================= التطبيق على قايمة =======================

        [Fact]
        public void Apply_filters_a_whole_list()
        {
            var workers = new[]
            {
                Worker(id: 1, stages: new[] { RingStage }, stars: 5m),
                Worker(id: 2, stages: new[] { RingStage }, stars: 2m),
                Worker(id: 3, stages: new[] { ChainStage }, stars: 5m),
                Worker(id: 4, isActive: false, stages: new[] { RingStage }, stars: 5m)
            };

            var result = WorkerFilterRules.Apply(
                workers, w => w,
                new WorkerFilterCriteria { StageId = RingStage, MinStars = 4 }).ToList();

            Assert.Single(result);
            Assert.Equal(1, result[0].WorkerId);
        }
    }
}
