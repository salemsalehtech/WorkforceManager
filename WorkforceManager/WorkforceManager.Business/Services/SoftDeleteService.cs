using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// الحذف الناعم — المكان الوحيد اللي بيشيل أي حاجة في البرنامج.
    ///
    /// كل عملية حذف بتمر من هنا بتعمل تلات حاجات **في معاملة واحدة**:
    ///   1. تتحقق من كلمة سر العمليات (نظام 1)
    ///   2. تعلّم الكيان محذوف وتسجّل مين/إمتى/ليه + لقطة الاسم (نظام 2)
    ///   3. تكتب الحدث في سجل العمليات (نظام 3)
    ///
    /// التلاتة مع بعض عن قصد: حذف من غير سبب مسجّل بيخلي السؤال "الشغل
    /// ده راح فين؟" مالوش إجابة، وحذف اتسجل من غير ما يتنفّذ (أو العكس)
    /// بيخلي السجل يكدب. فيا كله يا مفيش.
    ///
    /// اللي بينادي الخدمة دي **مبيلمسش** الكيان ولا السجل بنفسه.
    /// </summary>
    public class SoftDeleteService
    {
        private readonly OperationsPasswordService _gate;
        private readonly ActivityLogService _log;
        private readonly CurrentUserContext _currentUser;
        private readonly IActivityEventRepository _events;
        private readonly IUnitOfWork _unitOfWork;

        public SoftDeleteService(
            OperationsPasswordService gate,
            ActivityLogService log,
            CurrentUserContext currentUser,
            IActivityEventRepository events,
            IUnitOfWork unitOfWork)
        {
            _gate = gate;
            _log = log;
            _currentUser = currentUser;
            // الحفظ بيتم من هنا: كل الريبوهات بتشارك نفس الـ DbContext في
            // نفس الـ Scope، فحفظة واحدة بتنزّل الكيان والحدث مع بعض
            _events = events;
            _unitOfWork = unitOfWork;
        }

        /// <summary>
        /// يشيل كيان حذف ناعم بعد التحقق من كلمة السر، ويسجّل الحدث.
        /// </summary>
        /// <param name="entity">الكيان المطلوب حذفه (لازم يكون متتبَّع من الـ DbContext)</param>
        /// <param name="descriptor">وصف الكيان للسجل — الاسم والنوع ونوع الحدث</param>
        /// <param name="password">كلمة سر العمليات اللي كتبها المستخدم</param>
        /// <param name="reason">سبب الحذف — إجباري، ومن غيره السجل مالوش قيمة</param>
        /// <param name="saveChanges">
        /// false لو اللي بينادي ماسك معاملة أكبر وهيحفظ بنفسه (زي حذف يوم
        /// إنتاج كامل: كل سجلاته بتتشال مع بعض في حفظة واحدة).
        /// </param>
        public async Task<SoftDeleteResult> DeleteAsync<TEntity>(
            TEntity entity,
            DeletionDescriptor descriptor,
            string password,
            string reason,
            bool saveChanges = true)
            where TEntity : class, ISoftDeletable
        {
            if (string.IsNullOrWhiteSpace(reason))
                return SoftDeleteResult.Fail("لازم تكتب سبب الحذف — السجل من غير سبب مالوش قيمة");

            if (entity.IsDeleted)
                return SoftDeleteResult.Fail("الحاجة دي متشالة بالفعل");

            var gate = await _gate.VerifyAsync(descriptor.Action, password);
            if (!gate.IsAllowed)
                return SoftDeleteResult.Fail(gate.Message);

            // من هنا ورايح: الكيان والسجل لازم يتحفظوا مع بعض
            if (saveChanges)
            {
                await using var transaction = await _unitOfWork.BeginWriteTransactionAsync();
                Apply(entity, descriptor, reason);
                await _log.LogAsync(
                    descriptor.EventType, descriptor.EntityType, descriptor.EntityId,
                    descriptor.EntityName, reason, descriptor.Details, saveChanges: false);

                await _events.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            else
            {
                Apply(entity, descriptor, reason);
                await _log.LogAsync(
                    descriptor.EventType, descriptor.EntityType, descriptor.EntityId,
                    descriptor.EntityName, reason, descriptor.Details, saveChanges: false);
            }

            return SoftDeleteResult.Success(gate.IsNotConfigured);
        }

        /// <summary>بيعلّم حقول الحذف على الكيان — الخطوة الوحيدة اللي بتلمسه</summary>
        private void Apply(ISoftDeletable entity, DeletionDescriptor descriptor, string reason)
        {
            entity.IsDeleted = true;
            entity.DeletedAt = DateTime.Now;
            entity.DeletedBy = _currentUser.ActorName;
            entity.DeletionReason = reason.Trim();
            // لقطة الاسم: السجلات التاريخية بتعرضه بدل ما تشاور على صف
            // اتشال، ولو حد سمّى كيان جديد بنفس الاسم القديم يفضل مميّز
            entity.DeletedName = descriptor.EntityName;
        }
    }

    /// <summary>
    /// وصف الكيان المحذوف للسجل. نوع واحد بدل 5 معاملات نصية متفرقة —
    /// اللي بينادي مبيبقاش عنده فرصة يلخبط ترتيبهم.
    /// </summary>
    public class DeletionDescriptor
    {
        /// <summary>العملية الحساسة المقابلة (للبوابة والرسالة)</summary>
        public required SensitiveAction Action { get; init; }

        /// <summary>نوع الحدث اللي هيتسجّل</summary>
        public required ActivityEventType EventType { get; init; }

        /// <summary>نوع الكيان كنص ("Worker" / "Product" ...)</summary>
        public required string EntityType { get; init; }

        public required int EntityId { get; init; }

        /// <summary>اسم الكيان وقت الحذف — بيتخزن كلقطة على الكيان وفي السجل</summary>
        public required string EntityName { get; init; }

        /// <summary>تفاصيل إضافية للسجل (اختياري)</summary>
        public string? Details { get; init; }
    }

    /// <summary>نتيجة الحذف — نوع خاص عشان سبب الرفض يوصل للشاشة</summary>
    public class SoftDeleteResult
    {
        public bool IsDeleted { get; private init; }
        public string Message { get; private init; } = string.Empty;

        /// <summary>اتنفّذ من غير كلمة سر لأن مفيش واحدة متسجّلة — الشاشة بتنبّه</summary>
        public bool PasswordNotConfigured { get; private init; }

        public static SoftDeleteResult Success(bool passwordNotConfigured = false) =>
            new() { IsDeleted = true, PasswordNotConfigured = passwordNotConfigured };

        public static SoftDeleteResult Fail(string message) =>
            new() { IsDeleted = false, Message = message };
    }
}
