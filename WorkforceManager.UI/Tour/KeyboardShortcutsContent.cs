namespace WorkforceManager.UI.Tour
{
    /// <summary>اختصار واحد في جدول "اختصارات لوحة المفاتيح" بالدليل</summary>
    public sealed record KeyboardShortcut(string Keys, string Action, string Where);

    /// <summary>
    /// كل اختصارات البرنامج في مكان واحد للمستخدم (قسم في الدليل). أي اختصار
    /// جديد في الكود (KeyboardShortcuts، MainWindow.Window_PreviewKeyDown،
    /// أو KeyBinding في شاشة) لازم يتضاف هنا — اختصار محدش يعرف بيه مالوش لازمة.
    /// </summary>
    public static class KeyboardShortcutsContent
    {
        public static IReadOnlyList<KeyboardShortcut> Entries { get; } = new List<KeyboardShortcut>
        {
            new("Ctrl+K", "بحث سريع عن أي عامل أو منتج أو إعداد", "من أي مكان"),
            new("Ctrl+F", "دوّر في الشاشة الحالية", "العمال، المنتجات، الذاكرة، سجل العمليات — وغيرها بيفتح البحث السريع"),
            new("Ctrl+N", "إضافة جديد", "العمال، المنتجات، الحسابات الإدارية"),
            new("Ctrl+S", "حفظ", "أي نافذة إضافة أو تعديل، الذاكرة، تسجيل الإنتاج (الحضور، أو الرحلة اللي فيها المؤشر)"),
            new("Ctrl+B", "طي أو فتح القائمة الجانبية", "من أي مكان"),
            new("Ctrl+Z", "تراجع عن آخر تعديل أو حذف", "تسجيل الإنتاج ← سجلات اليوم"),
            new("Enter", "انتقل للخانة اللي بعدها", "تسجيل الإنتاج (وفي خانة اختيار العامل: بيضيفه)"),
            new("Esc", "قفل النافذة المفتوحة أو الخروج من الجولة", "أي نافذة"),
            new("↑ ↓", "التنقل بين الاسم وكلمة المرور وزرار الدخول", "شاشة تسجيل الدخول")
        };
    }
}
