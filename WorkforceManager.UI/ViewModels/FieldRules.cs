namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// قواعد أخطاء الخانات (مطلوب / رقم / مدى) — كل دالة بترجع نص الخطأ
    /// بالعربي أو "" لو الخانة سليمة. الـViewModels بتحط النتيجة في
    /// خاصية XError وبتتعرض تحت الخانة بـFieldError. قواعد العمل (حاجة
    /// معتمدة على بيانات تانية في قاعدة البيانات) مش هنا — دي بتفضل Notify.
    /// </summary>
    public static class FieldRules
    {
        public static string Required(string? value, string message) =>
            string.IsNullOrWhiteSpace(value) ? message : "";

        public static string Required(object? value, string message) =>
            value is null ? message : "";

        /// <summary>
        /// رقم عشري أكبر من صفر (مبلغ سلفة/حافز مثلًا) — نفس decimal.TryParse
        /// اللي الفورم بيستخدمه وقت الحفظ بالظبط، عشان التحقق والقيمة المحفوظة
        /// مايختلفوش أبدًا
        /// </summary>
        public static string PositiveDecimal(string? text, string message) =>
            decimal.TryParse(text, out var value) && value > 0 ? "" : message;

        /// <summary>رقم صحيح ≥ صفر، أو فاضي (الفاضي معناه "مفيش قيمة" مش صفر)</summary>
        public static string OptionalNonNegativeInt(string? text, string message) =>
            string.IsNullOrWhiteSpace(text) || (int.TryParse(text.Trim(), out var value) && value >= 0) ? "" : message;
    }
}
