using Microsoft.EntityFrameworkCore;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Models;
using WorkforceManager.Data.Seed;

namespace WorkforceManager.Data
{
    /// <summary>
    /// بيتشغل مرة واحدة بس، أول ما التطبيق يفتح ويلاقي قاعدة البيانات
    /// فاضية: بيزرع فيها بيانات العميل الحقيقية (17 منتج بمراحلها
    /// ويومياتها من Salem.xlsx، و46 عامل بأسمائهم من ملف اسماء الصنفرة،
    /// وربط مهاراتهم الفعلي بالمراحل من WorkerSkillsSeed).
    /// لو قاعدة البيانات فيها بيانات بالفعل، بيتخطى العملية تمامًا
    /// (منعًا لتكرار البيانات في كل تشغيل).
    /// </summary>
    public static class DatabaseSeeder
    {
        public static async Task SeedIfEmptyAsync(AppDbContext db)
        {
            var hasProducts = await db.Products.AnyAsync();
            var hasWorkers = await db.Workers.AnyAsync();

            if (!hasProducts)
            {
                var products = RealDataSeed.BuildProducts();
                await db.Products.AddRangeAsync(products);
            }

            if (!hasWorkers)
            {
                var workers = RealDataSeed.BuildWorkers();
                await db.Workers.AddRangeAsync(workers);
            }

            if (!hasProducts || !hasWorkers)
            {
                await db.SaveChangesAsync();
            }

            await SeedScrapReasonsAsync(db);

            // ربط المهارات بيعتمد على وجود المنتجات والعمال مع بعض —
            // بيتشغل بس أول مرة (تركيب جديد) عشان ميتعارضش مع تعديلات
            // المستخدم اليدوية اللاحقة على مهارات العمال من الشاشة
            if (!hasProducts && !hasWorkers)
            {
                await SeedWorkerSkillLinksAsync(db);
            }

            // الأدوار بالساعة للعمال الوصفيين — Upsert آمن (بيتخطى اللي متحدد
            // نوعه بالفعل) فينفع يتشغل على قاعدة موجودة من غير ما يلمس تعديلات المستخدم
            await SeedHourlyRolesAsync(db);
        }

        /// <summary>
        /// أسباب الهالك الافتراضية — بتتزرع مرة واحدة بس.
        ///
        /// الشرط على الجدول كله مش على كل سبب: المستخدم اللي شال سبب
        /// من الإعدادات مش عايزه يرجع تاني كل ما البرنامج يفتح.
        /// </summary>
        public static async Task SeedScrapReasonsAsync(AppDbContext db)
        {
            if (await db.ScrapReasons.AnyAsync()) return;

            var defaults = new[] { "عيب خامة", "غلط تشغيل", "عطل مكنة", "مقاس غلط", "أخرى" };

            await db.ScrapReasons.AddRangeAsync(
                defaults.Select((name, index) => new ScrapReason
                {
                    Name = name,
                    SortOrder = index + 1,
                    IsActive = true
                }));

            await db.SaveChangesAsync();
        }

        /// <summary>
        /// يحدد الدور بالساعة للعمال الوصفيين (رص/جودة/تدريب) بناءً على
        /// ملاحظاتهم النصية — للعمال اللي لسه مفيش لهم دور بالساعة محدد بس
        /// (مبيلمسش أي عامل المستخدم حدد نوعه يدويًا).
        /// </summary>
        public static async Task SeedHourlyRolesAsync(AppDbContext db)
        {
            // بناخد بس العمال اللي مالهمش دور بالساعة لسه، ومالهمش مهارات إنتاج
            // (عشان منحوّلش عامل إنتاج بالغلط لو ملاحظته فيها كلمة تدريب مثلًا)
            var candidates = await db.Workers
                .Where(w => w.HourlyRole == null && w.SkillsNotes != null && !w.Skills.Any())
                .ToListAsync();

            var changed = false;
            foreach (var worker in candidates)
            {
                var notes = worker.SkillsNotes!;
                HourlyRole? role = notes.Contains("رص") ? HourlyRole.Racking
                    : notes.Contains("جوده") || notes.Contains("جودة") ? HourlyRole.Quality
                    : notes.Contains("تدريب") ? HourlyRole.Training
                    : null;

                if (role is not null)
                {
                    worker.HourlyRole = role;
                    changed = true;
                }
            }

            if (changed)
                await db.SaveChangesAsync();
        }

        /// <summary>
        /// يربط مهارات العمال بمراحل الإنتاج من WorkerSkillsSeed —
        /// Upsert آمن بيتخطى أي رابط موجود بالفعل، فينفع يتشغل أكتر من
        /// مرة من غير تكرار (مفيد لتطبيقه على قاعدة بيانات موجودة فعلًا).
        /// </summary>
        public static async Task SeedWorkerSkillLinksAsync(AppDbContext db)
        {
            var links = WorkerSkillsSeed.BuildLinks();

            // الربط بالاسم. الكود في WorkerSkillsSeed معرّف داخلي للبذرة
            // بس (اسم عربي من خمس كلمات مكرر 38 مرة مش مقروء)، وRealDataSeed
            // هي اللي بتترجمه لاسم — الكود نفسه عمره ما بيتخزّن في
            // الداتابيز. أسماء العمال الـ46 المزروعين متأكد إنها فريدة.
            var nameByCode = RealDataSeed.NameByCode();

            // الاسم المكرر بيتشال من القايمة مش بيتاخد أول واحد فيه:
            // لو المستخدم أضاف عامل باسم عامل مزروع، التخمين هيربط
            // المهارات بالشخص الغلط — ومهارة ناقصة بتتصلح من الشاشة،
            // لكن مهارة على الشخص الغلط بتفضل غلط من غير ما حد ياخد باله.
            var workersByName = (await db.Workers.ToListAsync())
                .GroupBy(w => w.FullName)
                .Where(g => g.Count() == 1)
                .ToDictionary(g => g.Key, g => g.Single());

            var stagesByProduct = (await db.ProductionStages.Include(s => s.Product).ToListAsync())
                .ToLookup(s => s.Product.Name);

            var existingPairs = (await db.WorkerSkills
                    .Select(ws => new { ws.WorkerId, ws.ProductionStageId })
                    .ToListAsync())
                .Select(x => (x.WorkerId, x.ProductionStageId))
                .ToHashSet();

            var toAdd = new List<WorkerSkill>();
            foreach (var (code, workerLinks) in links)
            {
                if (!nameByCode.TryGetValue(code, out var workerName)) continue;
                if (!workersByName.TryGetValue(workerName, out var worker)) continue;

                foreach (var link in workerLinks)
                {
                    var productStages = stagesByProduct[link.ProductName];
                    var targetStages = link.StageName is null
                        ? productStages.Where(s => link.Exclude == null || !link.Exclude.Contains(s.StageName))
                        : productStages.Where(s => s.StageName == link.StageName);

                    foreach (var stage in targetStages)
                    {
                        if (!existingPairs.Add((worker.Id, stage.Id))) continue; // موجود بالفعل أو مكرر داخليًا

                        toAdd.Add(new WorkerSkill
                        {
                            WorkerId = worker.Id,
                            ProductionStageId = stage.Id,
                            Level = link.Level
                        });
                    }
                }
            }

            if (toAdd.Count > 0)
            {
                await db.WorkerSkills.AddRangeAsync(toAdd);
                await db.SaveChangesAsync();
            }
        }
    }
}
