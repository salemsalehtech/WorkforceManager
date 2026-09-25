using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// IWorkerRepository.CountNeedingAttentionAsync — العدّ الرخيص لبادچ جرس
    /// الإشعارات. نفس قاعدة WorkerRow.NeedsAttention (HasNoWage || HasNoSkills)
    /// بس COUNT مفهرس، فالاختبار بيتأكد إن العدّ متوافق معاها تمامًا.
    /// </summary>
    public class WorkerNeedsAttentionCountTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task CountsOnlyActiveNonDepartmentWorkersMissingWageOrSkills()
        {
            using (var scope = _db.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // النشط بمهارات وسعر يومية — مش محتاج انتباه (خط الأساس)
                context.Workers.Add(new Worker { FullName = "كامل", IsActive = true, DailyWageEgp = 200m });

                // النشط من غير سعر يومية — محتاج انتباه
                var noWage = new Worker { FullName = "من غير سعر", IsActive = true, DailyWageEgp = 0m };
                context.Workers.Add(noWage);

                // النشط بالقطعة من غير أي مهارة — محتاج انتباه
                var noSkills = new Worker { FullName = "من غير مهارات", IsActive = true, DailyWageEgp = 100m };
                context.Workers.Add(noSkills);

                // العامل بالساعة من غير مهارات — مش محتاج انتباه (مهاراتش مطلوبة بالتصميم)
                context.Workers.Add(new Worker
                {
                    FullName = "بالساعة", IsActive = true, DailyWageEgp = 150m, HourlyRole = HourlyRole.Racking
                });

                // موقوف من غير سعر يومية — مش بيتحسب أصلًا (مش نشط)
                context.Workers.Add(new Worker { FullName = "موقوف", IsActive = false, DailyWageEgp = 0m });

                // حساب إداري من غير سعر يومية — مستبعد زي شاشة العمال بالظبط
                context.Workers.Add(new Worker
                {
                    FullName = "مدير قسم", IsActive = true, DailyWageEgp = 0m, HourlyRole = HourlyRole.DepartmentManager
                });

                await context.SaveChangesAsync();

                // كامل عنده مهارة، الباقي من غير — عشان "من غير سعر" ميقعش في فلتر "من غير مهارات" كمان بالغلط
                context.WorkerSkills.Add(new WorkerSkill
                {
                    WorkerId = context.Workers.Single(w => w.FullName == "كامل").Id,
                    ProductionStageId = TestDatabase.RingStage1Id,
                    Level = SkillLevel.Proficient
                });
                context.WorkerSkills.Add(new WorkerSkill
                {
                    WorkerId = noWage.Id,
                    ProductionStageId = TestDatabase.RingStage1Id,
                    Level = SkillLevel.Proficient
                });
                await context.SaveChangesAsync();
            }

            using (var scope = _db.CreateScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<IWorkerRepository>();

                // البذرة الأصلية (أحمد/سعيد/منى) كلها سليمة — العدّ بس الاتنين الجداد المحتاجين انتباه
                Assert.Equal(2, await repo.CountNeedingAttentionAsync());
            }
        }
    }
}
