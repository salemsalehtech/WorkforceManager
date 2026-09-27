using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// اكتمال بيانات اليوم قبل التوقيع — محور مختلف عن تغطية سجل العمليات
    /// (DailyOperationsSignOffService). البيانات الافتراضية في TestDatabase
    /// خالية تمامًا من إنتاج/حضور/ذاكرة، فكل اختبار هنا بيسجّل بس اللي
    /// محتاجه ويتأكد إن الباقي فضل في قايمة النواقص.
    /// </summary>
    public class DailySignOffChecklistServiceTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private async Task<Business.DTOs.DailySignOffChecklist> BuildAsync()
        {
            using var scope = _db.CreateScope();
            return await _db.GetService<DailySignOffChecklistService>(scope).BuildAsync(TestDatabase.Today);
        }

        [Fact]
        public async Task Product_with_no_production_today_is_flagged()
        {
            var checklist = await BuildAsync();

            // مفيش إنتاج مسجّل أصلًا في القاعدة الافتراضية — المنتجات الأربعة كلها ناقصة
            Assert.Contains("دبلة", checklist.ProductsWithNoEntries);
            Assert.Contains("سلسلة", checklist.ProductsWithNoEntries);
        }

        [Fact]
        public async Task Product_with_production_today_is_not_flagged()
        {
            using (var scope = _db.CreateScope())
            {
                var context = _db.GetService<AppDbContext>(scope);
                context.DailyProductions.Add(new DailyProduction
                {
                    WorkerId = TestDatabase.WorkerAhmedId,
                    ProductionStageId = TestDatabase.RingStage1Id,
                    Date = TestDatabase.Today,
                    PieceCount = 10,
                    PiecesPerWorkdayAtEntry = 50
                });
                context.SaveChanges();
            }

            var checklist = await BuildAsync();

            Assert.DoesNotContain("دبلة", checklist.ProductsWithNoEntries);
            Assert.Contains("سلسلة", checklist.ProductsWithNoEntries); // مالوش إنتاج، لازم يفضل ناقص
        }

        [Fact]
        public async Task Worker_with_no_attendance_today_is_flagged()
        {
            var checklist = await BuildAsync();

            Assert.Contains("سعيد", checklist.WorkersWithNoAttendance);
        }

        [Fact]
        public async Task Worker_with_attendance_today_is_not_flagged()
        {
            using (var scope = _db.CreateScope())
            {
                var context = _db.GetService<AppDbContext>(scope);
                context.Attendances.Add(new Attendance
                {
                    WorkerId = TestDatabase.WorkerSaidId,
                    Date = TestDatabase.Today,
                    Status = AttendanceStatus.Present
                });
                context.SaveChanges();
            }

            var checklist = await BuildAsync();

            Assert.DoesNotContain("سعيد", checklist.WorkersWithNoAttendance);
            Assert.Contains("أحمد", checklist.WorkersWithNoAttendance);
        }

        [Fact]
        public async Task Memory_due_today_with_no_entries_is_flagged()
        {
            using (var scope = _db.CreateScope())
            {
                var context = _db.GetService<AppDbContext>(scope);
                context.ProductionMemories.Add(new ProductionMemory
                {
                    ProductId = TestDatabase.ProductChainId,
                    RemindOn = TestDatabase.Today,
                    CompletedAt = null
                });
                context.SaveChanges();
            }

            var checklist = await BuildAsync();

            Assert.Contains("سلسلة", checklist.MemoryDueWithNoEntries);
        }

        [Fact]
        public async Task Memory_due_today_whose_product_already_has_entries_is_not_flagged()
        {
            using (var scope = _db.CreateScope())
            {
                var context = _db.GetService<AppDbContext>(scope);
                context.ProductionMemories.Add(new ProductionMemory
                {
                    ProductId = TestDatabase.ProductChainId,
                    RemindOn = TestDatabase.Today,
                    CompletedAt = null
                });
                context.DailyProductions.Add(new DailyProduction
                {
                    WorkerId = TestDatabase.WorkerAhmedId,
                    ProductionStageId = TestDatabase.ChainStage1Id,
                    Date = TestDatabase.Today,
                    PieceCount = 5,
                    PiecesPerWorkdayAtEntry = 50
                });
                context.SaveChanges();
            }

            var checklist = await BuildAsync();

            Assert.DoesNotContain("سلسلة", checklist.MemoryDueWithNoEntries);
        }

        [Fact]
        public async Task Fully_covered_day_raises_no_issues()
        {
            using (var scope = _db.CreateScope())
            {
                var context = _db.GetService<AppDbContext>(scope);
                foreach (var stageId in new[]
                         {
                             TestDatabase.RingStage1Id, TestDatabase.ChainStage1Id,
                             TestDatabase.BagStage1Id, TestDatabase.ThirdsStage1Id
                         })
                {
                    context.DailyProductions.Add(new DailyProduction
                    {
                        WorkerId = TestDatabase.WorkerAhmedId,
                        ProductionStageId = stageId,
                        Date = TestDatabase.Today,
                        PieceCount = 5,
                        PiecesPerWorkdayAtEntry = 50
                    });
                }
                foreach (var workerId in new[] { TestDatabase.WorkerAhmedId, TestDatabase.WorkerSaidId, TestDatabase.WorkerMonaHourlyId })
                {
                    context.Attendances.Add(new Attendance { WorkerId = workerId, Date = TestDatabase.Today, Status = AttendanceStatus.Present });
                }
                context.SaveChanges();
            }

            var checklist = await BuildAsync();

            Assert.False(checklist.HasAnyIssues);
        }
    }
}
