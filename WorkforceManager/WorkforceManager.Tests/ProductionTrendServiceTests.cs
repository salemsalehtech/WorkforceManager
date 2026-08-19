using WorkforceManager.Business.Services;
using WorkforceManager.Core.Models;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// ProductionTrendService.Evaluate — دالة نقية، مفيش قاعدة بيانات
    /// هنا. المحور: متوسط آخر 7 أيام شغل **فعلية** (مش تقويمية)، وتنبيه
    /// بس لو النهارده أقل من 80% منه، وغياب تام (صفر) مش "تراجع".
    /// </summary>
    public class ProductionTrendServiceTests
    {
        private static readonly DateTime Today = new(2026, 8, 19);

        private static List<DailyProduction> DaysBefore(params int[] pieces)
        {
            // pieces[0] = أقرب يوم قبل النهارده (أمس)، وهكذا للخلف
            var records = new List<DailyProduction>();
            for (var i = 0; i < pieces.Length; i++)
                records.Add(new DailyProduction { Date = Today.AddDays(-(i + 1)), PieceCount = pieces[i] });
            return records;
        }

        [Fact]
        public void ANoticeableDrop_IsFlagged()
        {
            var records = DaysBefore(5000, 5000, 5000, 5000, 5000, 5000, 5000);
            records.Add(new DailyProduction { Date = Today, PieceCount = 3500 }); // 70%

            var result = ProductionTrendService.Evaluate(1, "أحمد", records, Today);

            Assert.NotNull(result);
            Assert.Equal(3500, result!.TodayPieces);
            Assert.Equal(5000m, result.TrailingAverage);
            Assert.Equal(0.70m, result.PercentOfAverage);
        }

        [Fact]
        public void ASmallDrop_UnderTheThreshold_IsNotFlagged()
        {
            var records = DaysBefore(5000, 5000, 5000, 5000, 5000, 5000, 5000);
            records.Add(new DailyProduction { Date = Today, PieceCount = 4200 }); // 84%

            Assert.Null(ProductionTrendService.Evaluate(1, "أحمد", records, Today));
        }

        [Fact]
        public void FewerThanSevenPriorWorkDays_IsNotFlagged_EvenWithATinyOutputToday()
        {
            var records = DaysBefore(5000, 5000, 5000); // 3 أيام سابقة بس
            records.Add(new DailyProduction { Date = Today, PieceCount = 100 });

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
                new() { Date = Today.AddDays(-1), PieceCount = 5000 },
                new() { Date = Today.AddDays(-2), PieceCount = 5000 },
                // فجوة يوم -3 و -4 (مفيش تسجيل)
                new() { Date = Today.AddDays(-5), PieceCount = 5000 },
                new() { Date = Today.AddDays(-6), PieceCount = 5000 },
                new() { Date = Today.AddDays(-7), PieceCount = 5000 },
                new() { Date = Today.AddDays(-8), PieceCount = 5000 },
                new() { Date = Today.AddDays(-9), PieceCount = 5000 },
                new() { Date = Today, PieceCount = 3500 }
            };

            var result = ProductionTrendService.Evaluate(1, "أحمد", records, Today);

            Assert.NotNull(result);
            Assert.Equal(5000m, result!.TrailingAverage);
        }
    }
}
