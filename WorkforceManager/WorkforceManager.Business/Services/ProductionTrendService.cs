using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// "جودة الإنتاج" — متوسط تراكمي لآخر أيام شغل العامل الفعلية (مش
    /// أيام تقويمية؛ يوم غياب أو من غير تسجيل مبيدخلش في المتوسط ولا
    /// بيوقف السلسلة)، وتنبيه لو إنتاج النهارده قلّ عنه بشكل ملحوظ.
    ///
    /// عامل من غير إنتاج النهارده خالص (0 قطعة) مش بيتنبّه عليه هنا —
    /// ده غياب، وشاشة الحضور أصلًا بتمسكه؛ التنبيه ده خاص بعامل حاضر
    /// وشغال بس إنتاجه قلّ عن المعتاد.
    /// </summary>
    public class ProductionTrendService
    {
        /// <summary>مدى البحث عن آخر أيام شغل فعلية — كفاية عمليًا لضمان 7 أيام شغل حتى مع أسبوع فيه غياب</summary>
        private const int LookbackDays = 21;

        /// <summary>أقل عدد أيام شغل سابقة لازم يكون موجود عشان المتوسط يبقى له معنى</summary>
        private const int RequiredPriorDays = 7;

        /// <summary>النسبة اللي تحتها إنتاج النهارده يتحسب "تراجع"</summary>
        private const decimal DeclineThreshold = 0.80m;

        private readonly IDailyProductionRepository _productionRepo;

        public ProductionTrendService(IDailyProductionRepository productionRepo)
        {
            _productionRepo = productionRepo;
        }

        /// <summary>كل عامل إنتاجه النهارده قلّ بشكل ملحوظ عن متوسط آخر 7 أيام شغل فعلية قبله</summary>
        public async Task<List<ProductionDeclineDto>> GetDecliningWorkersAsync(DateTime asOf)
        {
            var today = asOf.Date;
            var from = today.AddDays(-LookbackDays);
            var records = await _productionRepo.GetByRangeAsync(from, today);

            var result = new List<ProductionDeclineDto>();
            foreach (var group in records.ToLookup(r => r.WorkerId))
            {
                var dto = Evaluate(group.Key, group.First().Worker.FullName, group.ToList(), today);
                if (dto is not null) result.Add(dto);
            }

            return result.OrderBy(d => d.PercentOfAverage).ToList();
        }

        /// <summary>
        /// متوسط إنتاج كل عامل عنده تاريخ كافي (7 أيام شغل فعلية على
        /// الأقل)، مرتبين تنازليًا بالمتوسط — لجدول "متوسط إنتاج العمال"
        /// الكامل، بعكس GetDecliningWorkersAsync اللي بترجّع المتراجعين بس.
        /// </summary>
        public async Task<List<WorkerProductionAverageDto>> GetAllWorkerAveragesAsync(DateTime asOf)
        {
            var today = asOf.Date;
            var from = today.AddDays(-LookbackDays);
            var records = await _productionRepo.GetByRangeAsync(from, today);

            var result = records
                .ToLookup(r => r.WorkerId)
                .Select(group => EvaluateAverage(group.Key, group.First().Worker.FullName, group.ToList(), today))
                .Where(dto => dto.HasEnoughHistory)
                .OrderByDescending(dto => dto.TrailingAverage)
                .ToList();

            return result;
        }

        /// <summary>
        /// متوسط عامل واحد من سجلاته — دالة نقية (بدون DB) بتحسب المتوسط
        /// والنسبة لليوم المُعطى، من غير أي شرط "لازم يبقى فيه إنتاج
        /// النهارده" (بعكس Evaluate اللي بتشترط ده). المصدر الوحيد لحساب
        /// المتوسط — Evaluate بتستخدمها بدل ما تكرر نفس المنطق.
        /// </summary>
        public static WorkerProductionAverageDto EvaluateAverage(
            int workerId, string workerName, IReadOnlyList<DailyProduction> records, DateTime today)
        {
            var pieceCountByDay = records
                .GroupBy(r => r.Date.Date)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.PieceCount));

            var priorDays = pieceCountByDay
                .Where(kv => kv.Key < today)
                .OrderByDescending(kv => kv.Key)
                .Take(RequiredPriorDays)
                .Select(kv => (decimal)kv.Value)
                .ToList();

            decimal? average = priorDays.Count >= RequiredPriorDays && priorDays.Average() > 0
                ? Math.Round(priorDays.Average(), 0)
                : null;

            int? todayPieces = pieceCountByDay.TryGetValue(today, out var pieces) ? pieces : null;

            decimal? percentOfAverage = average is not null && todayPieces is not null
                ? Math.Round(todayPieces.Value / average.Value, 2)
                : null;

            return new WorkerProductionAverageDto
            {
                WorkerId = workerId,
                WorkerName = workerName,
                TrailingAverage = average,
                TodayPieces = todayPieces,
                PercentOfAverage = percentOfAverage
            };
        }

        /// <summary>محسوبة لوحدها (بدون DB) عشان تتختبر مباشرة من غير سيناريو أسبوعين كاملين</summary>
        public static ProductionDeclineDto? Evaluate(
            int workerId, string workerName, IReadOnlyList<DailyProduction> records, DateTime today)
        {
            var avg = EvaluateAverage(workerId, workerName, records, today);

            // مفيش إنتاج النهارده خالص = غياب، مش "تراجع" — شاشة الحضور بتمسك دي
            if (avg.TodayPieces is null or 0) return null;

            // لسه مفيش تاريخ كفاية يتقاس عليه — بلاغ خطأ أسوأ من مفيش بلاغ
            if (avg.TrailingAverage is null || avg.PercentOfAverage is null) return null;

            if (avg.PercentOfAverage >= DeclineThreshold) return null;

            return new ProductionDeclineDto
            {
                WorkerId = workerId,
                WorkerName = workerName,
                TodayPieces = avg.TodayPieces.Value,
                TrailingAverage = avg.TrailingAverage.Value,
                PercentOfAverage = avg.PercentOfAverage.Value
            };
        }
    }
}
