using WorkforceManager.Core.Enums;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// اللون والأيقونة لكل حالة حضور — مكان واحد عشان الحالة تبان بنفس
    /// الشكل في أي حتة (شريحة الاختيار، شريط السطر الجانبي، عدّادات
    /// الملخص فوق). الألوان مأخوذة من فرش نظام التصميم في App.xaml
    /// (SuccessBrush / WarnBrush / DangerBrush) عشان تفضل متسقة معاه.
    /// </summary>
    public static class AttendanceVisuals
    {
        /// <summary>لون الحالة (نفس قيم الفرش في App.xaml)</summary>
        public const string PresentColor = "#0B6E4F";   // SuccessBrush
        public const string ExcusedColor = "#B7791F";   // WarnBrush
        public const string UnexcusedColor = "#B00020"; // DangerBrush
        public const string UnsetColor = "#C7CFDD";     // رمادي باهت = لسه مفيش تسجيل

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
