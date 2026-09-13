namespace WorkforceManager.Core.Helpers
{
    /// <summary>
    /// أول حرفين من الاسم — اللي بيتعرض في الدايرة مكان الصورة.
    ///
    /// كان مكتوب أربع مرات (بطاقة العامل، وصف الحضور، بطاقة المنتج،
    /// نافذة المؤهلين)، وواحدة منهم مكتوب فوقها "نفس قاعدة باقي
    /// الشاشات" — يعني اللي كتبها كان عارف إنها متكررة. النسخ الأربعة
    /// كانوا متطابقين، بس ده معناه إن أي تعديل في القاعدة لازم يفتكر
    /// أربع أماكن.
    /// </summary>
    public static class NameInitials
    {
        /// <summary>لما الاسم يبقى فاضي — علامة استفهام أوضح من دايرة فاضية</summary>
        private const string Unknown = "؟";

        /// <summary>
        /// اسم من كلمة واحدة بياخد أول حرفين منها، وأكتر من كلمة بتاخد
        /// أول حرف من أول كلمتين.
        /// </summary>
        public static string From(string? fullName)
        {
            var parts = (fullName ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0) return Unknown;

            return parts.Length == 1
                ? parts[0][..Math.Min(2, parts[0].Length)]
                : $"{parts[0][0]}{parts[1][0]}";
        }
    }
}
