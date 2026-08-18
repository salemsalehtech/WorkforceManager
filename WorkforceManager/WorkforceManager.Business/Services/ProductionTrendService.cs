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

        /// <summary>محسوبة لوحدها (بدون DB) عشان تتختبر مباشرة من غير سيناريو أسبوعين كاملين</summary>
        public static ProductionDeclineDto? Evaluate(
            int workerId, string workerName, IReadOnlyList<DailyProduction> records, DateTime today)
        {
            var pieceCountByDay = records
                .GroupBy(r => r.Date.Date)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.PieceCount));

            // مفيش إنتاج النهارده خالص = غياب، مش "تراجع" — شاشة الحضور بتمسك دي
            var todayPieces = pieceCountByDay.GetValueOrDefault(today, 0);
            if (todayPieces == 0) return null;

            var priorDays = pieceCountByDay
                .Where(kv => kv.Key < today)
                .OrderByDescending(kv => kv.Key)
                .Take(RequiredPriorDays)
                .Select(kv => (decimal)kv.Value)
                .ToList();

            // لسه مفيش تاريخ كفاية يتقاس عليه — بلاغ خطأ أسوأ من مفيش بلاغ
            if (priorDays.Count < RequiredPriorDays) return null;

            var average = priorDays.Average();
            if (average <= 0) return null;

            var percentOfAverage = Math.Round(todayPieces / average, 2);
            if (percentOfAverage >= DeclineThreshold) return null;

            return new ProductionDeclineDto
            {
                WorkerId = workerId,
                WorkerName = workerName,
                TodayPieces = todayPieces,
                TrailingAverage = Math.Round(average, 0),
                PercentOfAverage = percentOfAverage
            };
        }
    }
}
