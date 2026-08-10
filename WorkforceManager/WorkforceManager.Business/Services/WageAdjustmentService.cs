using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// مسؤولة عن تسجيل وإدارة تعديلات الأجر بالجنيه (السلف والحوافز):
    /// السلفة مبلغ أخذه العامل مقدمًا يُخصم من أجره، والحافز مبلغ يُضاف له.
    /// مستقلة عن الإنتاج والحضور — أثرها الحسابي بيتطبق في كشف الأجور
    /// (PayrollService) وتقرير العامل: الأجر = أجر اليوميات + الحوافز − السلف.
    /// </summary>
    public class WageAdjustmentService
    {
        private readonly IWageAdjustmentRepository _adjustmentRepo;
        private readonly OperationsPasswordService _gate;
        private readonly ActivityLogService _log;

        public WageAdjustmentService(
            IWageAdjustmentRepository adjustmentRepo,
            OperationsPasswordService gate,
            ActivityLogService log)
        {
            _adjustmentRepo = adjustmentRepo;
            _gate = gate;
            _log = log;
        }

        /// <summary>
        /// يسجل سلفة أو حافز جديد على عامل في تاريخ معين بمبلغ بالجنيه.
        ///
        /// البوابة هنا في الخدمة مش في الشاشة عشان مفيش مسار يعدّي من
        /// غيرها — نفس قاعدة الحضور والجزاءات. دي فلوس بتتضاف أو تتخصم
        /// من الأجر مباشرة، وكانت **الحركة الوحيدة اللي بتلمس فلوس من
        /// غير كلمة سر** مع إن نوعها معرّف في SensitiveAction من زمان
        /// ومحدش استخدمه.
        /// </summary>
        public async Task<WageAdjustment> RecordAdjustmentAsync(
            int workerId, DateTime date, WageAdjustmentType type, decimal amountEgp,
            string? note = null, string operationsPassword = "")
        {
            if (amountEgp <= 0)
                throw new ArgumentException("المبلغ لازم يكون أكبر من صفر", nameof(amountEgp));

            var gate = await _gate.VerifyAsync(SensitiveAction.SaveWageAdjustment, operationsPassword);
            if (!gate.IsAllowed)
                throw new InvalidOperationException(gate.Message);

            var adjustment = new WageAdjustment
            {
                WorkerId = workerId,
                Date = date.Date,
                Type = type,
                AmountEgp = amountEgp,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
            };

            await _adjustmentRepo.AddAsync(adjustment);
            await _adjustmentRepo.SaveChangesAsync();

            await _log.LogAsync(
                ActivityEventType.WageAdjustmentSaved, "WageAdjustment", adjustment.Id,
                entityName: type == WageAdjustmentType.Advance ? "سلفة" : "حافز",
                reason: adjustment.Note,
                details: $"{amountEgp:N0} ج يوم {adjustment.Date:yyyy/MM/dd}");

            return adjustment;
        }

        /// <summary>
        /// يحذف حركة مسجّلة بالخطأ (حذف فعلي، مالهاش قيمة تاريخية زي
        /// الجزاء الغلط). بكلمة سر برضه: حذف سلفة بيرجّع فلوس لأجر
        /// العامل زي ما تسجيلها بيخصمها.
        /// </summary>
        public async Task RemoveAdjustmentAsync(int adjustmentId, string operationsPassword = "")
        {
            var gate = await _gate.VerifyAsync(SensitiveAction.SaveWageAdjustment, operationsPassword);
            if (!gate.IsAllowed)
                throw new InvalidOperationException(gate.Message);

            var adjustment = await _adjustmentRepo.GetByIdAsync(adjustmentId)
                ?? throw new InvalidOperationException("الحركة المحددة غير موجودة");

            _adjustmentRepo.Remove(adjustment);
            await _adjustmentRepo.SaveChangesAsync();

            await _log.LogAsync(
                ActivityEventType.WageAdjustmentDeleted, "WageAdjustment", adjustment.Id,
                entityName: adjustment.Type == WageAdjustmentType.Advance ? "سلفة" : "حافز",
                details: $"كانت {adjustment.AmountEgp:N0} ج يوم {adjustment.Date:yyyy/MM/dd}");
        }

        /// <summary>كل حركات يوم معين لكل العمال (لعرضها وحذفها في شاشة التسجيل اليومي)</summary>
        public Task<IReadOnlyList<WageAdjustment>> GetByDateAsync(DateTime date)
            => _adjustmentRepo.GetByDateAsync(date);
    }
}
