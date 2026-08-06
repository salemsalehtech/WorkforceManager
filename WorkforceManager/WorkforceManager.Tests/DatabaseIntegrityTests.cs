using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// صحة قاعدة البيانات نفسها: حدود التواريخ، والقيود اللي بتحمي
    /// الجدول من الكود.
    ///
    /// الاختبارات دي مش بتختبر ميزة، بتختبر إن القاعدة نفسها سليمة —
    /// النوع اللي لو باظ مبيبانش في شاشة، بيبان بعد سنة في كشف أجور غلط.
    /// </summary>
    public class DatabaseIntegrityTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Day => TestDatabase.Today;

        private async Task RecordAsync(DateTime date, int pieces = 10)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<WorkdayCalculationService>(scope).RecordProductionAsync(
                TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, pieces, date,
                confirmOverride: true);
        }

        // ======================= حدود المدى =======================
        // الاستعلامات بتقارن على العمود مباشرة (dp.Date >= from.Date)
        // عشان فهرس التاريخ يشتغل. الاختبارات دي بتثبّت إن الحدود
        // شاملة — لو حد رجّع .Date على العمود أو غيّر المقارنة، هتقع.

        [Fact]
        public async Task A_record_on_the_first_day_of_the_range_is_included()
        {
            await RecordAsync(Day);

            using var scope = _db.CreateScope();
            var rows = await _db.GetService<Core.Interfaces.IDailyProductionRepository>(scope)
                .GetByRangeAsync(Day, Day.AddDays(5));

            Assert.Single(rows);
        }

        [Fact]
        public async Task A_record_on_the_last_day_of_the_range_is_included()
        {
            await RecordAsync(Day.AddDays(5));

            using var scope = _db.CreateScope();
            var rows = await _db.GetService<Core.Interfaces.IDailyProductionRepository>(scope)
                .GetByRangeAsync(Day, Day.AddDays(5));

            Assert.Single(rows);
        }

        [Fact]
        public async Task A_record_outside_the_range_is_left_out_on_both_sides()
        {
            await RecordAsync(Day.AddDays(-1));
            await RecordAsync(Day.AddDays(6));

            using var scope = _db.CreateScope();
            var rows = await _db.GetService<Core.Interfaces.IDailyProductionRepository>(scope)
                .GetByRangeAsync(Day, Day.AddDays(5));

            Assert.Empty(rows);
        }

        [Fact]
        public async Task Asking_for_a_day_ignores_the_time_on_the_argument()
        {
            // الشاشات بتبعت DateTime.Now أحيانًا مش DateTime.Today
            await RecordAsync(Day);

            using var scope = _db.CreateScope();
            var rows = await _db.GetService<Core.Interfaces.IDailyProductionRepository>(scope)
                .GetByDateAsync(Day.AddHours(17).AddMinutes(43));

            Assert.Single(rows);
        }

        [Fact]
        public async Task Every_date_column_is_stored_at_midnight()
        {
            // ده الشرط اللي المقارنة المباشرة قايمة عليه. لو مسار كتابة
            // جديد نسي .Date، الاستعلامات هتفضل تشتغل بس هتسيب سجلات
            // برّا المدى من غير ما حد ياخد باله — الاختبار ده بيمسكها.
            await RecordAsync(Day);

            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);

            Assert.All(await db.DailyProductions.ToListAsync(),
                r => Assert.Equal(r.Date.Date, r.Date));
            Assert.All(await db.Attendances.ToListAsync(),
                a => Assert.Equal(a.Date.Date, a.Date));
        }

        // ======================= قيود الجدول =======================
        // الخدمات بتمنع القيم دي أصلاً. القيد هنا بيحمي الجدول من مسار
        // جديد أو أداة خارجية — الكتابة المباشرة لازم تترفض.

        [Fact]
        public async Task The_table_refuses_a_star_rating_outside_one_to_five()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);

            var skill = await db.WorkerSkills.FirstAsync();
            skill.Stars = 9;

            await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        [Fact]
        public async Task The_table_refuses_a_zero_quota_on_a_stage()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);

            // اليومية دي هي المقسوم عليه في حساب الأجر
            var stage = await db.ProductionStages.FirstAsync();
            stage.PiecesPerWorkday = 0;

            await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        [Fact]
        public async Task The_table_refuses_a_production_row_with_a_zero_quota_snapshot()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);

            db.DailyProductions.Add(new DailyProduction
            {
                WorkerId = TestDatabase.WorkerAhmedId,
                ProductionStageId = TestDatabase.BagStage1Id,
                Date = Day,
                PieceCount = 10,
                PiecesPerWorkdayAtEntry = 0
            });

            await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        [Fact]
        public async Task The_table_refuses_a_negative_daily_wage()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);

            var worker = await db.Workers.FirstAsync();
            worker.DailyWageEgp = -50m;

            await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        [Fact]
        public async Task The_table_refuses_a_zero_advance()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);

            db.WageAdjustments.Add(new WageAdjustment
            {
                WorkerId = TestDatabase.WorkerAhmedId,
                Date = Day,
                Type = WageAdjustmentType.Advance,
                AmountEgp = 0m
            });

            await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        // ======================= الأعمدة اللي اتشالت =======================

        [Fact]
        public async Task The_dead_columns_are_gone_and_stay_gone()
        {
            // اتشالوا لأن مفيش مسار بيكتب فيهم قيمة حقيقية ومفيش شاشة
            // بتقراهم. الاختبار ده بيمنع رجوعهم بالغلط مع كيان جديد.
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);

            var columns = db.Model.FindEntityType(typeof(Attendance))!
                .GetProperties().Select(p => p.Name).ToList();

            Assert.DoesNotContain("CheckInTime", columns);
            Assert.DoesNotContain("CheckOutTime", columns);
            Assert.DoesNotContain("Notes", columns);

            foreach (var type in new[]
                     {
                         typeof(DailyProduction), typeof(Penalty),
                         typeof(HourlyWorkLog), typeof(ProductionDayClosure)
                     })
            {
                Assert.DoesNotContain("Notes",
                    db.Model.FindEntityType(type)!.GetProperties().Select(p => p.Name));
            }

            await Task.CompletedTask;
        }

        [Fact]
        public void The_measured_ratio_is_a_numeric_column_not_text()
        {
            // نص معناه مقارنة أبجدية: "10.5" أقل من "9.0". العمود ده
            // بيتقارن في الذاكرة دلوقتي، بس النوع لازم يفضل رقمي عشان
            // أول استعلام يرتّب بيه ميطلعش غلط بصمت.
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);

            var column = db.Model.FindEntityType(typeof(WorkerSkill))!
                .GetProperty(nameof(WorkerSkill.MeasuredRatio));

            Assert.Equal("decimal(5,2)", column.GetColumnType());
        }
    }
}
