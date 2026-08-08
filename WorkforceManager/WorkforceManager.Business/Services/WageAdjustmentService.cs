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

        public WageAdjustmentService(IWageAdjustmentRepository adjustmentRepo)
        {
            _adjustmentRepo = adjustmentRepo;
        }

        /// <summary>يسجل سلفة أو حافز جديد على عامل في تاريخ معين بمبلغ بالجنيه</summary>
        public async Task<WageAdjustment> RecordAdjustmentAsync(
            int workerId, DateTime date, WageAdjustmentType type, decimal amountEgp, string? note = null)
        {
            if (amountEgp <= 0)
                throw new ArgumentException("المبلغ لازم يكون أكبر من صفر", nameof(amountEgp));

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
            return adjustment;
        }

        /// <summary>يحذف حركة مسجّلة بالخطأ (حذف فعلي، مالهاش قيمة تاريخية زي الجزاء الغلط)</summary>
        public async Task RemoveAdjustmentAsync(int adjustmentId)
        {
            var adjustment = await _adjustmentRepo.GetByIdAsync(adjustmentId)
                ?? throw new InvalidOperationException("الحركة المحددة غير موجودة");

            _adjustmentRepo.Remove(adjustment);
            await _adjustmentRepo.SaveChangesAsync();
        }

        /// <summary>كل حركات يوم معين لكل العمال (لعرضها وحذفها في شاشة التسجيل اليومي)</summary>
        public Task<IReadOnlyList<WageAdjustment>> GetByDateAsync(DateTime date)
            => _adjustmentRepo.GetByDateAsync(date);
    }
}
