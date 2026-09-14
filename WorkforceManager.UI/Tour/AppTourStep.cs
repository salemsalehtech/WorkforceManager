namespace WorkforceManager.UI.Tour
{
    /// <summary>أي شاشة تتفتح قبل الخطوة دي — None معناها تفضل على الشاشة الحالية (مفيش تنقّل)</summary>
    public enum TourScreen
    {
        None,
        Workers,
        Products,
        DailyEntry,
        Memory,
        Evaluation,
        Reports,
        ActivityLog,
        Settings,
        DepartmentAccounts
    }

    /// <summary>
    /// خطوة واحدة في جولة "إيه الجديد": شاشة تتفتح (لو محتاجة)، عنصر يتلوّن
    /// عليه سبوت لايت (بالاسم — <see cref="MainWindow.FindTourTarget"/>)،
    /// وعنوان/وصف بلغة المستخدم العادية.
    /// </summary>
    public class AppTourStep
    {
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required string TargetElementName { get; init; }
        public TourScreen Screen { get; init; } = TourScreen.None;
    }
}
