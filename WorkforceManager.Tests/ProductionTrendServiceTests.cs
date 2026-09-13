using WorkforceManager.Business.Services;
using WorkforceManager.Core.Models;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// ProductionTrendService.Evaluate — دالة نقية، مفيش قاعدة بيانات
    /// هنا. المحور: متوسط آخر 7 أيام شغل **فعلية** (مش تقويمية) بـ
    /// **يوميات** مش قطع خام، وتنبيه بس لو النهارده أقل من 80% منه،
    /// وغياب تام (صفر) مش "تراجع".
    /// </summary>
    public class ProductionTrendServiceTests
    {
        private static readonly DateTime Today = new(2026, 8, 19);

        /// <summary>اليومية المفترضة 5000 قطعة عشان الأرقام تفضل سهلة القراءة (5000 قطعة = يومية واحدة بالظبط)</summary>
        private const int Quota = 5000;

        private static List<DailyProduction> DaysBefore(params int[] pieces)
        {
            // pieces[0] = أقرب يوم قبل النهارده (أمس)، وهكذا للخلف
            var records = new List<DailyProduction>();
            for (var i = 0; i < pieces.Length; i++)
                records.Add(new DailyProduction
                {
                    Date = Today.AddDays(-(i + 1)),
                    PieceCount = pieces[i],
                    PiecesPerWorkdayAtEntry = Quota
                });
            return records;
        }

        private static DailyProduction TodayRecord(int pieceCount) =>
            new() { Date = Today, PieceCount = pieceCount, PiecesPerWorkdayAtEntry = Quota };

        [Fact]
        public void ANoticeableDrop_IsFlagged()
        {
            var records = DaysBefore(5000, 5000, 5000, 5000, 5000, 5000, 5000);
            records.Add(TodayRecord(3500)); // 0.7 يومية = 70%

            var result = ProductionTrendService.Evaluate(1, "أحمد", records, Today);

            Assert.NotNull(result);
            Assert.Equal(0.7m, result!.TodayWorkdays);
            Assert.Equal(1.0m, result.TrailingAverageWorkdays);
            Assert.Equal(0.70m, result.PercentOfAverage);
        }

        [Fact]
        public void ASmallDrop_UnderTheThreshold_IsNotFlagged()
        {
            var records = DaysBefore(5000, 5000, 5000, 5000, 5000, 5000, 5000);
            records.Add(TodayRecord(4200)); // 84%

            Assert.Null(ProductionTrendService.Evaluate(1, "أحمد", records, Today));
        }

        [Fact]
        public void FewerThanSevenPriorWorkDays_IsNotFlagged_EvenWithATinyOutputToday()
        {
            var records = DaysBefore(5000, 5000, 5000); // 3 أيام سابقة بس
            records.Add(TodayRecord(100));

            Assert.Null(ProductionTrendService.Evaluate(1, "أحمد", records, Today));
        }

        [Fact]
        public void NoProductionToday_IsAbsence_NotADecline()
        {
            var records = DaysBefore(5000, 5000, 5000, 5000, 5000, 5000, 5000);
            // مفيش سجل خالص للنهارده

            Assert.Null(ProductionTrendService.Evaluate(1, "أحمد", records, Today));
        }

        [Fact]
        public void GapDaysWithNoRecord_AreSkipped_NotCountedAsZero()
        {
            // 10 أيام فات، 3 منهم من غير تسجيل (عطلة/غياب) — لازم يرجع
            // بالظبط لآخر 7 أيام *فعلية*، مش يحسب الفجوات صفر ولا يوقف عندها
            var records = new List<DailyProduction>
            {
                new() { Date = Today.AddDays(-1), PieceCount = 5000, PiecesPerWorkdayAtEntry = Quota },
                new() { Date = Today.AddDays(-2), PieceCount = 5000, PiecesPerWorkdayAtEntry = Quota },
                // فجوة يوم -3 و -4 (مفيش تسجيل)
                new() { Date = Today.AddDays(-5), PieceCount = 5000, PiecesPerWorkdayAtEntry = Quota },
                new() { Date = Today.AddDays(-6), PieceCount = 5000, PiecesPerWorkdayAtEntry = Quota },
                new() { Date = Today.AddDays(-7), PieceCount = 5000, PiecesPerWorkdayAtEntry = Quota },
                new() { Date = Today.AddDays(-8), PieceCount = 5000, PiecesPerWorkdayAtEntry = Quota },
                new() { Date = Today.AddDays(-9), PieceCount = 5000, PiecesPerWorkdayAtEntry = Quota },
                TodayRecord(3500)
            };

            var result = ProductionTrendService.Evaluate(1, "أحمد", records, Today);

            Assert.NotNull(result);
            Assert.Equal(1.0m, result!.TrailingAverageWorkdays);
        }

        // ======================= EvaluateAverage (لجدول متوسط إنتاج العمال) =======================

        [Fact]
        public void EvaluateAverage_WithEnoughHistoryAndNormalToday_HasNoAlert()
        {
            var records = DaysBefore(5000, 5000, 5000, 5000, 5000, 5000, 5000);
            records.Add(TodayRecord(4900)); // 98%

            var result = ProductionTrendService.EvaluateAverage(1, "أحمد", records, Today);

            Assert.True(result.HasEnoughHistory);
            Assert.Equal(1.0m, result.TrailingAverageWorkdays);
            Assert.False(result.IsBelowToday);
        }

        [Fact]
        public void EvaluateAverage_WithEnoughHistoryAndLowToday_IsBelowToday()
        {
            var records = DaysBefore(5000, 5000, 5000, 5000, 5000, 5000, 5000);
            records.Add(TodayRecord(3500)); // 70%

            var result = ProductionTrendService.EvaluateAverage(1, "أحمد", records, Today);

            Assert.True(result.HasEnoughHistory);
            Assert.True(result.IsBelowToday);
        }

        [Fact]
        public void EvaluateAverage_WithEnoughHistoryButNoProductionToday_StillReportsTheAverage()
        {
            // غياب النهارده — لسه يظهر بمتوسطه، بس بلا حالة/تنبيه (بعكس
            // Evaluate اللي بترجع null تمامًا في الحالة دي)
            var records = DaysBefore(5000, 5000, 5000, 5000, 5000, 5000, 5000);

            var result = ProductionTrendService.EvaluateAverage(1, "أحمد", records, Today);

            Assert.True(result.HasEnoughHistory);
            Assert.Equal(1.0m, result.TrailingAverageWorkdays);
            Assert.Null(result.TodayWorkdays);
            Assert.Null(result.PercentOfAverage);
            Assert.False(result.IsBelowToday); // مفيش نسبة أصلًا يتحسب عليها تنبيه
            Assert.Equal("لسه من غير تسجيل النهارده", result.TodayText);
        }

        [Fact]
        public void EvaluateAverage_WithFewerThanSevenPriorDays_HasNoEnoughHistory()
        {
            var records = DaysBefore(5000, 5000, 5000);
            records.Add(TodayRecord(100));

            var result = ProductionTrendService.EvaluateAverage(1, "أحمد", records, Today);

            Assert.False(result.HasEnoughHistory);
            Assert.Null(result.TrailingAverageWorkdays);
        }

        [Fact]
        public void EvaluateAverage_RecentDays_FlagsDaysBelowTheAverage()
        {
            // آخر يومين قلّوا (0.7 و 0.6 يومية)، وباقي الأسبوع عادي (1.0) —
            // المتوسط نفسه بيتأثر بالأيام القليلة (6.3/7 = 0.9)، والنسبة
            // بتتحسب على المتوسط الناتج ده مش على رقم ثابت مفترض
            var records = DaysBefore(3500, 3000, 5000, 5000, 5000, 5000, 5000);
            records.Add(TodayRecord(4900));

            var result = ProductionTrendService.EvaluateAverage(1, "أحمد", records, Today);

            Assert.Equal(0.9m, result.TrailingAverageWorkdays);

            var yesterday = result.RecentDays.Single(d => d.Date == Today.AddDays(-1));
            Assert.Equal(0.7m, yesterday.Workdays);
            Assert.True(yesterday.IsBelowNormal); // 0.7 / 0.9 ≈ 0.78

            var dayBeforeThat = result.RecentDays.Single(d => d.Date == Today.AddDays(-2));
            Assert.Equal(0.6m, dayBeforeThat.Workdays);
            Assert.True(dayBeforeThat.IsBelowNormal); // 0.6 / 0.9 ≈ 0.67

            var normalDay = result.RecentDays.Single(d => d.Date == Today.AddDays(-3));
            Assert.False(normalDay.IsBelowNormal); // 1.0 / 0.9 ≈ 1.11
        }
    }
}
