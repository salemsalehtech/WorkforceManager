using Microsoft.EntityFrameworkCore;

namespace WorkforceManager.Data
{
    /// <summary>
    /// بينضّف الصفوف اللي المستخدم شالها وفضلت متعلّمة في الجداول.
    ///
    /// الحذف بقى بيمسح الصف خالص طول ما مفيش تاريخ أجور بيشاور عليه
    /// (<c>DeletionScopeService</c>)، بس ده بيطبّق على اللي جاي. الصفوف
    /// اللي اتشالت قبل كده لسه قاعدة، وكل استعلام بيعدّي عليها وهي
    /// مستبعدة بفلتر. الكلاس ده بيلحق عليها بنفس القاعدة بالظبط.
    ///
    /// بيتنادى عند فتح البرنامج بعد النسخة الاحتياطية، زي تنظيف سجل
    /// العمليات. آمن يتنادى أي عدد مرات: بيشتغل على المتشال بس، ولو
    /// مفيش حاجة بيرجع صفر من غير ما يكتب.
    ///
    /// **مفيش تفاعل متسلسل عن قصد**: القرار بيتاخد على الحالة زي ما هي
    /// دلوقتي، الأول لكل الصفوف، وبعدين المسح. يعني سجل إنتاج متشال
    /// مبيخليش العامل اللي عليه يبقى "فاضي" في نفس اللفة — العامل ده
    /// بيتنضّف في تشغيل بعدين لو فضل فاضي فعلاً.
    /// </summary>
    public static class DeletedRowsCleaner
    {
        public static async Task<int> PurgeAsync(AppDbContext db)
        {
            // اللي محدش بيشاور عليهم دلوقتي — بيتحسبوا قبل أي مسح
            var busyWorkers = await BusyWorkerIdsAsync(db);
            var busyStages = await db.DailyProductions.IgnoreQueryFilters()
                .Select(dp => dp.ProductionStageId).Distinct().ToListAsync();

            var removed = 0;

            // سجلات الإنتاج المتشالة: مفيش أي مفتاح أجنبي بيشاور عليها
            removed += await db.DailyProductions.IgnoreQueryFilters()
                .Where(dp => dp.IsDeleted)
                .ExecuteDeleteAsync();

            removed += await db.Workers
                .Where(w => w.IsDeleted && !busyWorkers.Contains(w.Id))
                .ExecuteDeleteAsync();

            removed += await db.ProductionStages
                .Where(s => s.IsDeleted && !busyStages.Contains(s.Id))
                .ExecuteDeleteAsync();

            // المنتج بيتشال بس لو مفيش ولا مرحلة من مراحله عليها إنتاج
            var busyProducts = await db.ProductionStages
                .Where(s => busyStages.Contains(s.Id))
                .Select(s => s.ProductId)
                .Distinct()
                .ToListAsync();

            removed += await db.Products
                .Where(p => p.IsDeleted && !busyProducts.Contains(p.Id))
                .ExecuteDeleteAsync();

            return removed;
        }

        /// <summary>
        /// العمال اللي عليهم أي حاجة بتدخل في أجرهم — إنتاج، شغل بالساعة،
        /// جزاء، سلفة أو حافز. دول مايتمسحوش مهما كانوا متشالين: كشوفهم
        /// القديمة بتقرا أسماءهم منهم.
        /// </summary>
        private static async Task<List<int>> BusyWorkerIdsAsync(AppDbContext db)
        {
            var ids = new HashSet<int>();

            ids.UnionWith(await db.DailyProductions.IgnoreQueryFilters()
                .Select(dp => dp.WorkerId).Distinct().ToListAsync());
            ids.UnionWith(await db.HourlyWorkLogs.Select(h => h.WorkerId).Distinct().ToListAsync());
            ids.UnionWith(await db.Penalties.Select(p => p.WorkerId).Distinct().ToListAsync());
            ids.UnionWith(await db.WageAdjustments.Select(a => a.WorkerId).Distinct().ToListAsync());

            return ids.ToList();
        }
    }
}
