using WorkforceManager.Business.DTOs;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// العملية اتوقفت **قبل أي كتابة** لأن العامل مكلّف بالفعل بمنتج/مرحلة
    /// تانية في نفس اليوم، والقاعدة دي بتتخطى بموافقة صريحة بس.
    ///
    /// مش خطأ في المدخلات — ده سؤال للمستخدم. عشان كده بيرث من
    /// <see cref="InvalidOperationException"/> (فأي كود قديم بيمسك
    /// استثناءات التحقق بيفضل شغال) بس بنوع منفصل وبتفاصيل منظّمة، عشان
    /// الواجهة تعرف تفرّق وتفتح مربع تأكيد بدل رسالة رفض.
    ///
    /// المتصل اللي عايز يكمّل بيعيد نفس الطلب بـ <c>confirmOverride: true</c>.
    /// </summary>
    public class AssignmentConfirmationRequiredException : InvalidOperationException
    {
        public AssignmentConfirmationRequiredException(IReadOnlyList<AssignmentConflictDto> conflicts)
            : base(BuildMessage(conflicts))
        {
            Conflicts = conflicts;
        }

        /// <summary>تفاصيل كل تعارض (المكلّف به حاليًا + المطلوب إضافته)</summary>
        public IReadOnlyList<AssignmentConflictDto> Conflicts { get; }

        private static string BuildMessage(IReadOnlyList<AssignmentConflictDto> conflicts) =>
            string.Join("\n\n", conflicts.Select(c => c.ConfirmationQuestion));
    }
}
