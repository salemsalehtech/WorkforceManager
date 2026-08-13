using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// مسؤولة عن كل عمليات "الكتابة" على العمال: إضافة عامل جديد، تعديل
    /// بياناته، إيقافه (Soft Delete)، إعادة تفعيله، وإدارة مهاراته
    /// (ربطه/فك ربطه بمراحل الإنتاج) — القراءة والعرض مسؤولية الاستعلامات
    /// في IWorkerRepository مباشرة.
    /// </summary>
    public class WorkerManagementService
    {
        private readonly IWorkerRepository _workerRepo;
        private readonly IGenericRepository<WorkerSkill> _skillRepo;
        private readonly SoftDeleteService _softDelete;
        private readonly DeletionScopeService _scope;

        private readonly ActivityLogService _log;

        public WorkerManagementService(
            IWorkerRepository workerRepo,
            IGenericRepository<WorkerSkill> skillRepo,
            SoftDeleteService softDelete,
            DeletionScopeService scope,
            ActivityLogService log)
        {
            _workerRepo = workerRepo;
            _skillRepo = skillRepo;
            _softDelete = softDelete;
            _scope = scope;
            _log = log;
        }

        /// <summary>
        /// يضيف عامل جديد. الاسم هو المطلوب الوحيد.
        ///
        /// مفيش كود وظيفي خالص: العمود اتشال من الداتابيز — الاسم كافي
        /// للبحث والتمييز، والبذرة بقت بتربط المهارات بالاسم.
        ///
        /// ومفيش ملاحظات مهارات كمان: خانتها اتشالت من الفورم واتبدّلت
        /// بتقييم النجوم لكل مرحلة (<c>SkillRatingService</c>)، فالعامل
        /// الجديد بيتسجل بـ <c>SkillsNotes = null</c>.
        /// </summary>
        public async Task<Worker> CreateWorkerAsync(
            string fullName, string? phoneNumber = null,
            DateTime? hireDate = null, HourlyRole? hourlyRole = null,
            decimal dailyWageEgp = 0)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                throw new ArgumentException("اسم العامل مطلوب", nameof(fullName));
            if (dailyWageEgp < 0)
                throw new ArgumentException("سعر اليومية لا يصح يكون سالبًا", nameof(dailyWageEgp));

            var worker = new Worker
            {
                FullName = fullName.Trim(),
                PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim(),
                HireDate = hireDate,
                HourlyRole = hourlyRole,
                DailyWageEgp = dailyWageEgp
            };

            await _workerRepo.AddAsync(worker);
            await _workerRepo.SaveChangesAsync();

            await _log.LogAsync(
                ActivityEventType.WorkerCreated, "Worker", worker.Id,
                entityName: worker.FullName,
                details: dailyWageEgp > 0 ? $"سعر اليومية {dailyWageEgp:N0} ج" : "من غير سعر يومية");

            return worker;
        }

        /// <summary>
        /// يعدّل البيانات الأساسية لعامل موجود (نفس قواعد التحقق بتاعة الإضافة).
        ///
        /// <c>SkillsNotes</c> مش بيتلمس هنا **عن قصد**:
        /// خانته اتشالت من الفورم، فلو فضل بيتكتب من هنا كان أول تعديل
        /// لأي عامل هيصفّر ملاحظاته — واللي
        /// <c>DatabaseSeeder.SeedHourlyRolesAsync</c> بيقرا منها، والبحث
        /// بيدوّر جواها. فالقاعدة: مفيش حد بيكتب فيه، واللي متكتب فاضل.
        /// </summary>
        public async Task<Worker> UpdateWorkerAsync(
            int workerId, string fullName,
            string? phoneNumber = null, DateTime? hireDate = null,
            HourlyRole? hourlyRole = null, decimal dailyWageEgp = 0)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                throw new ArgumentException("اسم العامل مطلوب", nameof(fullName));
            if (dailyWageEgp < 0)
                throw new ArgumentException("سعر اليومية لا يصح يكون سالبًا", nameof(dailyWageEgp));

            var worker = await _workerRepo.GetByIdAsync(workerId)
                ?? throw new InvalidOperationException("العامل المحدد غير موجود");

            // السعر ده بيضرب في يوميات كل الفترات — القديمة كمان، لأنه
            // مش لقطة وقت التسجيل. فتغييره حركة فلوس بمعنى الكلمة
            var oldWage = worker.DailyWageEgp;

            worker.FullName = fullName.Trim();
            worker.PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
            worker.HireDate = hireDate;
            worker.HourlyRole = hourlyRole;
            worker.DailyWageEgp = dailyWageEgp;

            _workerRepo.Update(worker);
            await _workerRepo.SaveChangesAsync();

            if (oldWage != dailyWageEgp)
                await _log.LogAsync(
                    ActivityEventType.WorkerWageChanged, "Worker", worker.Id,
                    entityName: worker.FullName,
                    details: $"من {oldWage:N0} ج إلى {dailyWageEgp:N0} ج لليومية");

            return worker;
        }

        /// <summary>
        /// يحدّد صورة العامل أو يشيلها (<paramref name="photoData"/> = null).
        ///
        /// منفصلة عن <see cref="UpdateWorkerAsync"/> لنفس سبب
        /// <c>SetProductImageAsync</c>: تعديل الاسم مالوش لازمة يبعت الصورة
        /// كلها معاه في كل مرة، ولا يمسحها بالغلط لو المتصل نسي يبعتها.
        /// </summary>
        public async Task<Worker> SetWorkerPhotoAsync(int workerId, byte[]? photoData)
        {
            var worker = await _workerRepo.GetByIdAsync(workerId)
                ?? throw new InvalidOperationException("العامل المحدد غير موجود");

            worker.PhotoData = photoData is { Length: > 0 } ? photoData : null;

            _workerRepo.Update(worker);
            await _workerRepo.SaveChangesAsync();
            return worker;
        }

        /// <summary>
        /// إيقاف عامل (Soft Delete): بيختفي من القوائم النشطة لكن كل
        /// سجلاته التاريخية (إنتاج/حضور/جزاءات) بتفضل محفوظة — القرار
        /// المتفق عليه بدل الحذف النهائي.
        /// </summary>
        public async Task DeactivateWorkerAsync(int workerId)
        {
            var worker = await _workerRepo.GetByIdAsync(workerId)
                ?? throw new InvalidOperationException("العامل المحدد غير موجود");

            worker.IsActive = false;
            _workerRepo.Update(worker);
            await _workerRepo.SaveChangesAsync();
        }

        /// <summary>
        /// يشيل عامل من النظام نهائيًا — **حذف ناعم** بكلمة سر وسبب.
        ///
        /// الفرق عن الإيقاف: الإيقاف مؤقت وله رجوع (العامل في إجازة أو
        /// وقف شغل)، والحذف بيقول "الشخص ده مبقاش من المصنع خالص".
        /// الاتنين بيخفوه من القوايم، بس الحذف بيسجّل مين شاله وليه.
        ///
        /// سجلاته التاريخية (إنتاج، أجور، حضور) بتفضل كلها زي ما هي
        /// وبتعرض اسمه وقت الحذف — عشان كشف أجور قديم مايتحوّلش لأرقام
        /// من غير أسماء.
        /// </summary>
        public async Task<SoftDeleteResult> DeleteWorkerAsync(
            int workerId, string operationsPassword, string reason)
        {
            var worker = await _workerRepo.GetByIdAsync(workerId)
                ?? throw new InvalidOperationException("العامل المحدد غير موجود");

            var result = await _softDelete.DeleteAsync(
                worker,
                new DeletionDescriptor
                {
                    Action = SensitiveAction.DeleteWorker,
                    EventType = ActivityEventType.WorkerDeleted,
                    EntityType = nameof(Worker),
                    EntityId = worker.Id,
                    EntityName = worker.FullName
                },
                operationsPassword,
                reason,
                // مفيش تاريخ أجور عليه = يتمسح من الجدول خالص. عليه
                // تاريخ = يتعلّم بس، عشان كشوف أجوره القديمة تفضل تقرا
                // اسمه بدل ما تبقى أرقام من غير أصحاب
                removePermanently: await _scope.CanRemoveWorkerAsync(worker.Id)
                    ? () => _workerRepo.Remove(worker)
                    : null);

            // العامل المشال ميظهرش في القوايم النشطة كمان: الحذف الناعم
            // مالوش فلتر عام على العامل (عشان التقارير التاريخية)، فالإخفاء
            // بيتم بنفس العلامة اللي كل الشاشات بتحترمها أصلاً
            if (result.IsDeleted && !result.WasPermanent && worker.IsActive)
            {
                worker.IsActive = false;
                _workerRepo.Update(worker);
                await _workerRepo.SaveChangesAsync();
            }

            return result;
        }

        /// <summary>إعادة تفعيل عامل موقوف (رجع للشغل مثلاً) — سجله القديم كله بيرجع معاه</summary>
        public async Task ReactivateWorkerAsync(int workerId)
        {
            var worker = await _workerRepo.GetByIdAsync(workerId)
                ?? throw new InvalidOperationException("العامل المحدد غير موجود");

            worker.IsActive = true;
            _workerRepo.Update(worker);
            await _workerRepo.SaveChangesAsync();
        }

        /// <summary>
        /// يضيف مهارة لعامل (يربطه بمرحلة إنتاج معينة بمستوى إتقان).
        /// لو المهارة موجودة بالفعل بيحدّث مستوى الإتقان بس بدل ما يرمي
        /// خطأ — أسهل في الاستخدام من الواجهة.
        /// </summary>
        public async Task<WorkerSkill> AssignSkillAsync(
            int workerId, int productionStageId, SkillLevel level = SkillLevel.Proficient)
        {
            var existing = (await _skillRepo.FindAsync(
                s => s.WorkerId == workerId && s.ProductionStageId == productionStageId))
                .FirstOrDefault();

            if (existing is not null)
            {
                existing.Level = level;
                _skillRepo.Update(existing);
                await _skillRepo.SaveChangesAsync();
                return existing;
            }

            var skill = new WorkerSkill
            {
                WorkerId = workerId,
                ProductionStageId = productionStageId,
                Level = level,
                CreatedAt = DateTime.Now
            };

            await _skillRepo.AddAsync(skill);
            await _skillRepo.SaveChangesAsync();
            return skill;
        }

        /// <summary>يشيل مهارة من عامل (حذف فعلي لسطر الربط — مش بيأثر على سجلات الإنتاج التاريخية)</summary>
        public async Task RemoveSkillAsync(int workerId, int productionStageId)
        {
            var existing = (await _skillRepo.FindAsync(
                s => s.WorkerId == workerId && s.ProductionStageId == productionStageId))
                .FirstOrDefault()
                ?? throw new InvalidOperationException("المهارة المحددة غير مرتبطة بهذا العامل");

            _skillRepo.Remove(existing);
            await _skillRepo.SaveChangesAsync();
        }
    }
}
