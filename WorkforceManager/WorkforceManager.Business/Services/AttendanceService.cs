using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// مسؤول عن تسجيل حضور/غياب العمال، ومنع التسجيل المكرر لنفس اليوم.
    ///
    /// قاعدة حماية أساسية (قرار متفق عليه): ممنوع تسجيل "غياب" لعامل
    /// عنده إنتاج مسجل في نفس اليوم — ده تناقض بيانات وبيسبب خصم غياب
    /// لعامل شغال فعليًا. اللي بيسجل لازم يمسح الإنتاج الأول لو العامل
    /// فعلاً كان غايب.
    /// </summary>
    public class AttendanceService
    {
        private readonly IAttendanceRepository _attendanceRepo;
        private readonly IDailyProductionRepository _productionRepo;
        private readonly AttendanceAutomationService _automation;
        private readonly OperationsPasswordService _gate;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ActivityLogService _log;

        public AttendanceService(
            IAttendanceRepository attendanceRepo,
            IDailyProductionRepository productionRepo,
            AttendanceAutomationService automation,
            OperationsPasswordService gate,
            IUnitOfWork unitOfWork,
            ActivityLogService log)
        {
            _log = log;
            _attendanceRepo = attendanceRepo;
            _productionRepo = productionRepo;
            _automation = automation;
            _gate = gate;
            _unitOfWork = unitOfWork;
        }

        /// <summary>
        /// يسجل حضور مجموعة عمال في نفس اليوم دفعة واحدة (Upsert جماعي).
        /// أساس زر "حفظ الحضور" في شاشة التسجيل: بدل ما نعمل استعلام +
        /// حفظ منفصل لكل عامل (اللي بيبقى عشرات الرحلات لقاعدة البيانات)،
        /// بنحمّل سجلات اليوم الموجودة مرة واحدة وبنحفظ كل التعديلات في
        /// حفظة واحدة. لو فيه أي عامل متعلم "غياب" وله إنتاج في نفس اليوم،
        /// الدفعة كلها بتترفض برسالة بتسمّي العمال — يا كله سليم يا مفيش.
        /// بيرجع عدد العمال اللي اتسجلوا/اتحدّثوا.
        /// </summary>
        /// <param name="operationsPassword">
        /// كلمة سر العمليات. الحفظ ده بيولّد جزاءات غياب تلقائية بتنقص من
        /// أجور العمال، فهو عملية بتلمس فلوس. البوابة هنا في الخدمة مش في
        /// الشاشة عشان مفيش مسار يعدّي من غيرها.
        /// </param>
        public async Task<AttendanceSaveResultDto> RecordAttendanceBatchAsync(
            DateTime date,
            IEnumerable<(int WorkerId, AttendanceStatus Status)> entries,
            string operationsPassword = "")
        {
            var entryList = entries.ToList();
            if (entryList.Count == 0)
                return new AttendanceSaveResultDto();

            // كلمة سر واحدة للدفعة كلها — مش واحدة لكل عامل
            var gate = await _gate.VerifyAsync(SensitiveAction.SaveAttendance, operationsPassword);
            if (!gate.IsAllowed)
                throw new InvalidOperationException(gate.Message);

            // قاعدة الحماية: مفيش غياب لعامل له شغل مسجل في نفس اليوم.
            // "له شغل" بيتحدد من مكان واحد (AttendanceAutomationService) عشان
            // يشمل العمال بالساعة كمان، مش بس اللي ليهم إنتاج على مراحل.
            var workersWithWork = await _automation.GetWorkersWithLoggedWorkAsync(date);
            var namesById = (await _productionRepo.GetByDateAsync(date))
                .GroupBy(r => r.WorkerId)
                .ToDictionary(g => g.Key, g => g.First().Worker.FullName);

            var conflicts = entryList
                .Where(e => e.Status != AttendanceStatus.Present && workersWithWork.Contains(e.WorkerId))
                .Select(e => namesById.TryGetValue(e.WorkerId, out var name) ? name : $"#{e.WorkerId}")
                .ToList();

            if (conflicts.Count > 0)
                throw new InvalidOperationException(
                    $"مينفعش تسجيل غياب لعمال لهم شغل مسجل في {date:yyyy/MM/dd}:\n" +
                    $"{string.Join("، ", conflicts)}\n\n" +
                    "لو فعلاً كانوا غايبين، امسح شغلهم الأول من تبويب \"سجلات اليوم\" — ومفيش أي حالة اتحفظت من الدفعة دي.");

            // الحضور + الجزاءات التلقائية في معاملة واحدة: يا الاتنين يا ولا واحد.
            // القفل بيتاخد من أول لحظة فمفيش نسختين يحفظوا نفس اليوم في نفس الوقت
            // ويطلعوا جزاءين لنفس الغياب.
            await using var transaction = await _unitOfWork.BeginWriteTransactionAsync();

            // كل سجلات اليوم الموجودة مرة واحدة، مفهرسة بالعامل للوصول السريع
            var existingByWorker = (await _attendanceRepo.GetByDateAsync(date))
                .ToDictionary(a => a.WorkerId);

            foreach (var (workerId, status) in entryList)
            {
                if (existingByWorker.TryGetValue(workerId, out var existing))
                {
                    existing.Status = status;
                    _attendanceRepo.Update(existing);
                }
                else
                {
                    await _attendanceRepo.AddAsync(new Attendance
                    {
                        WorkerId = workerId,
                        Date = date.Date,
                        Status = status
                    });
                }
            }

            await _attendanceRepo.SaveChangesAsync(); // حفظة واحدة لكل التعديلات

            // مصالحة جزاءات الغياب على العمال اللي الدفعة دي لمستهم بس
            var statusesByWorker = entryList
                .GroupBy(e => e.WorkerId)
                .ToDictionary(g => g.Key, g => g.Last().Status);

            var (created, removed) = await _automation.ReconcileAbsencePenaltiesAsync(
                date, statusesByWorker, statusesByWorker.Keys.ToList());

            await transaction.CommitAsync();

            await _log.LogAsync(
                ActivityEventType.AttendanceSaved, "Attendance", 0,
                entityName: $"يوم {date:yyyy/MM/dd}",
                details: $"{entryList.Count} عامل"
                         + (created > 0 ? $" — {created} جزاء غياب اتولّد" : "")
                         + (removed > 0 ? $" — {removed} جزاء غياب اتشال" : ""));

            return new AttendanceSaveResultDto
            {
                SavedCount = entryList.Count,
                AutoPenaltiesCreated = created,
                AutoPenaltiesRemoved = removed
            };
        }
    }
}
