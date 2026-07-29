namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// تكليف عامل واحد على (منتج + مرحلة) في يوم إنتاج واحد — الوحدة
    /// اللي قاعدة "عامل واحد في مرحلة واحدة" بتشتغل عليها. بتستخدم
    /// للمسجل بالفعل وللمطلوب إضافته على السواء، عشان المقارنة تبقى
    /// بين نفس النوع من غير تحويلات.
    /// </summary>
    public class WorkerAssignmentDto
    {
        public int WorkerId { get; init; }
        public string WorkerName { get; init; } = string.Empty;

        public int ProductId { get; init; }
        public string ProductName { get; init; } = string.Empty;

        public int ProductionStageId { get; init; }
        public string StageName { get; init; } = string.Empty;

        /// <summary>"منتج – مرحلة" للعرض في رسائل التحذير</summary>
        public string Display => $"{ProductName} – {StageName}";
    }

    /// <summary>
    /// تعارض واحد: العامل مكلّف بالفعل بـ <see cref="Existing"/> ولسه
    /// المستخدم عايز يضيفه لـ <see cref="Attempted"/> في نفس اليوم.
    /// بيتبعت للواجهة كتفاصيل منظّمة (مش نص جاهز) عشان الرسالة تتبني
    /// في مكان واحد وتفضل التفاصيل متاحة لأي استخدام تاني.
    /// </summary>
    public class AssignmentConflictDto
    {
        public required WorkerAssignmentDto Existing { get; init; }
        public required WorkerAssignmentDto Attempted { get; init; }

        /// <summary>سؤال التأكيد الموحّد المعروض للمستخدم</summary>
        public string ConfirmationQuestion =>
            $"العامل \"{Attempted.WorkerName}\" مكلّف بالفعل النهارده بـ \"{Existing.Display}\".\n" +
            $"تحب تضيفه كمان لـ \"{Attempted.Display}\"؟";
    }

    /// <summary>
    /// نتيجة فحص قاعدة التكليف لمجموعة تكليفات مطلوبة.
    ///
    /// النتيجة ليها 3 حالات مانعة للالتباس:
    /// • <see cref="HasDuplicates"/> → تكرار حرفي، ممنوع دايمًا (مش حالة تأكيد).
    /// • <see cref="RequiresConfirmation"/> → تعارض، محتاج موافقة صريحة.
    /// • مفيش ولا واحدة → التكليف عادي وبيتحفظ على طول.
    /// الترتيب ده مقصود: التكرار بيتفحص الأول وبيغلب التعارض.
    /// </summary>
    public class AssignmentCheckResultDto
    {
        /// <summary>تكليفات مطلوبة موجودة بالحرف بالفعل (نفس العامل والمرحلة واليوم) — R5</summary>
        public IReadOnlyList<WorkerAssignmentDto> Duplicates { get; init; } = Array.Empty<WorkerAssignmentDto>();

        /// <summary>تكليفات بتخلي العامل على أكتر من (منتج/مرحلة) في نفس اليوم — R2</summary>
        public IReadOnlyList<AssignmentConflictDto> Conflicts { get; init; } = Array.Empty<AssignmentConflictDto>();

        public bool HasDuplicates => Duplicates.Count > 0;
        public bool RequiresConfirmation => Conflicts.Count > 0;
        public bool IsClear => !HasDuplicates && !RequiresConfirmation;
    }
}
