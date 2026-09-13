using WorkforceManager.Core.Enums;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// اللون والأيقونة لكل حالة حضور — مكان واحد عشان الحالة تبان بنفس
    /// الشكل في أي حتة (شريحة الاختيار، شريط السطر الجانبي، عدّادات
    /// الملخص فوق).
    ///
    /// **دي أسماء فُرَش مش أكواد ألوان.** الكلاس ده كان بيرجّع
    /// \u200E"#0B6E4F"\u200E وأخواته — كود ثابت مبيتغيرش مع الثيم، فالوضع الليلي
    /// كان بيطلع بنفس أخضر وأحمر النهاري. دلوقتي بيرجّع اسم الدور
    /// واللوحة بتقرر اللون، فشوف <see cref="ThemeBrush"/>.
    /// </summary>
    public static class AttendanceVisuals
    {
        /// <summary>مفتاح الفرشاة في لوحة الألوان</summary>
        public const string PresentColor = "GoodBrush";
        public const string ExcusedColor = "WarnBrush";
        public const string UnexcusedColor = "DangerBrush";
        public const string UnsetColor = "InkFaintBrush";   // لسه مفيش تسجيل

        public static string ColorFor(AttendanceStatus? status) => status switch
        {
            AttendanceStatus.Present => PresentColor,
            AttendanceStatus.AbsentWithPermission => ExcusedColor,
            AttendanceStatus.AbsentWithoutPermission => UnexcusedColor,
            _ => UnsetColor
        };

        /// <summary>اسم أيقونة MaterialDesign المعبّرة عن الحالة</summary>
        public static string IconFor(AttendanceStatus? status) => status switch
        {
            AttendanceStatus.Present => "CheckCircle",
            AttendanceStatus.AbsentWithPermission => "ClockAlert",
            AttendanceStatus.AbsentWithoutPermission => "CloseCircle",
            _ => "HelpCircleOutline"
        };
    }
}
