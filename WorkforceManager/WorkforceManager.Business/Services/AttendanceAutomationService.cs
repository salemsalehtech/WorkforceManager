using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// أتمتة الحضور — القواعد اللي بتشتغل لوحدها من غير ما المستخدم يعمل حاجة:
    ///
    /// 1. **مين اشتغل النهارده** (<see cref="GetWorkersWithLoggedWorkAsync"/>):
    ///    التعريف الوحيد في البرنامج كله. شاشة الحضور بتستخدمه عشان تعلّم
    ///    "حاضر" تلقائيًا، وخدمة الحفظ بتستخدمه في التحقق. ممنوع أي مكان
    ///    تاني يعيد اشتقاقه من سجلات الإنتاج بنفسه.
    ///
    /// 2. **جزاء الغياب التلقائي** (<see cref="ReconcileAbsencePenaltiesAsync"/>):
    ///    غياب بدون إذن = جزاء نص يومية بيتولّد لوحده، وبيتشال لوحده لو
    ///    الحالة اتغيّرت. بيمر على <see cref="PenaltyService"/> (مصدر
    ///    الحقيقة للجزاءات) مش بيكتب في الجدول مباشرة.
    ///
    /// مبدأ ثابت: البرنامج بيلمس بس الجزاءات اللي هو ولّدها
    /// (<see cref="PenaltySource.AutoAbsence"/>). الجزاء اللي كتبه
    /// المستخدم بإيده مقدّس — مبيتعدلش ومبيتشالش مهما حصل.
    /// </summary>
    public class AttendanceAutomationService
    {
        private readonly IDailyProductionRepository _productionRepo;
        private readonly IHourlyWorkLogRepository _hourlyRepo;
        private readonly IPenaltyRepository _penaltyRepo;
        private readonly PenaltyService _penaltyService;

        public AttendanceAutomationService(
            IDailyProductionRepository productionRepo,
            IHourlyWorkLogRepository hourlyRepo,
            IPenaltyRepository penaltyRepo,
            PenaltyService penaltyService)
        {
            _productionRepo = productionRepo;
            _hourlyRepo = hourlyRepo;
            _penaltyRepo = penaltyRepo;
            _penaltyService = penaltyService;
        }

        /// <summary>
        /// العمال اللي ليهم شغل مسجّل في اليوم — التعريف الوحيد لـ"اشتغل النهارده".
        ///
        /// بيشمل النوعين: العامل بالقطعة بيبان من سجلات الإنتاج
        /// (<see cref="DailyProduction"/> على المراحل)، والعامل بالساعة
        /// بيبان من سجل ساعاته (<see cref="HourlyWorkLog"/>) — لأنه مالوش
        /// إنتاج على مراحل أصلاً، فلو اعتمدنا على الإنتاج بس كان هيتحسب
        /// إنه ماشتغلش.
        /// </summary>
        public async Task<HashSet<int>> GetWorkersWithLoggedWorkAsync(DateTime date)
        {
            var producers = (await _productionRepo.GetByDateAsync(date)).Select(p => p.WorkerId);
            var hourlyWorkers = (await _hourlyRepo.GetByDateAsync(date)).Select(h => h.WorkerId);

            return producers.Concat(hourlyWorkers).ToHashSet();
        }

        /// <summary>
        /// يوفّق جزاءات الغياب التلقائية مع حالات الحضور المحفوظة في اليوم.
        ///
        /// بيتنادى بعد ما الحضور يتحفظ (جوه نفس المعاملة) وبيعمل حاجتين:
        /// • أي عامل حالته "غياب بدون إذن" ومالوش جزاء تلقائي في اليوم ده → يتولّد له واحد.
        /// • أي جزاء تلقائي لعامل حالته بقت حاجة تانية (أو اتشال سجل حضوره) → يتشال.
        ///
        /// العملية **idempotent**: تشغيلها مية مرة على نفس الحالة بيدي نفس
        /// النتيجة بالظبط — مفيش تكرار ومفيش حذف وإعادة إنشاء من غير داعي.
        /// </summary>
        /// <param name="statusesByWorker">حالة الحضور المحفوظة لكل عامل اتحفظ في الدفعة دي</param>
        /// <param name="workerIdsInScope">
        /// العمال اللي المصالحة بتشملهم. لازم يبقى العمال اللي الدفعة
        /// لمستهم بس — عشان ميتشالش جزاء تلقائي لعامل تاني مالوش علاقة
        /// بالحفظة دي (ممكن يكون متسجّل من شاشة تانية أو يوم تاني).
        /// </param>
        /// <returns>عدد الجزاءات اللي اتولّدت وعدد اللي اتشال</returns>
        public async Task<(int Created, int Removed)> ReconcileAbsencePenaltiesAsync(
            DateTime date,
            IReadOnlyDictionary<int, AttendanceStatus> statusesByWorker,
            IReadOnlyCollection<int> workerIdsInScope)
        {
            // الجزاءات التلقائية الموجودة في اليوم ده للعمال اللي بنشتغل عليهم
            var existingAutoPenalties = (await _penaltyRepo.GetByRangeAsync(date, date))
                .Where(p => p.Source == PenaltySource.AutoAbsence)
                .Where(p => workerIdsInScope.Contains(p.WorkerId))
                .ToList();

            var autoPenaltyByWorker = existingAutoPenalties
                .GroupBy(p => p.WorkerId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var created = 0;
            var removed = 0;

            foreach (var workerId in workerIdsInScope)
            {
                var isUnexcusedAbsence =
                    statusesByWorker.TryGetValue(workerId, out var status) &&
                    status == AttendanceStatus.AbsentWithoutPermission;

                autoPenaltyByWorker.TryGetValue(workerId, out var workerAutoPenalties);
                var hasAutoPenalty = workerAutoPenalties is { Count: > 0 };

                if (isUnexcusedAbsence && !hasAutoPenalty)
                {
                    // B4: غياب بدون إذن من غير جزاء → ولّد واحد
                    await _penaltyService.RecordPenaltyAsync(
                        workerId, date,
                        AbsenceDeductionRule.AutoAbsenceReason,
                        AbsenceDeductionRule.AbsenceDeductionAmount,
                        notes: null,
                        source: PenaltySource.AutoAbsence,
                        saveChanges: false); // الحفظ مرة واحدة في آخر المصالحة
                    created++;
                }
                else if (!isUnexcusedAbsence && hasAutoPenalty)
                {
                    // B5: الحالة اتغيّرت → شيل الجزاء التلقائي (اليدوي مبيتلمسش)
                    foreach (var penalty in workerAutoPenalties!)
                    {
                        _penaltyRepo.Remove(penalty);
                        removed++;
                    }
                }
                else if (isUnexcusedAbsence && workerAutoPenalties!.Count > 1)
                {
                    // تنظيف دفاعي: لو بيانات قديمة فيها أكتر من جزاء تلقائي
                    // لنفس اليوم، نسيب واحد ونشيل الزيادة (B6)
                    foreach (var duplicate in workerAutoPenalties.Skip(1))
                    {
                        _penaltyRepo.Remove(duplicate);
                        removed++;
                    }
                }
            }

            if (created > 0 || removed > 0)
                await _penaltyRepo.SaveChangesAsync();

            return (created, removed);
        }
    }
}
