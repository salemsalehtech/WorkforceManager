namespace WorkforceManager.UI
{
    /// <summary>
    /// علامة على ديالوجات الإدخال/التعديل اللي Ctrl+S فيها = زرار الحفظ
    /// (IsDefault). مقصودة اختيارية مش على كل النوافذ: ديالوجات الأسئلة
    /// والتأكيد (MessageDialog، SensitiveActionDialog...) زرارها الافتراضي
    /// ممكن يكون "أيوه" على تجاهل تعديلات أو تأكيد حذف — Ctrl+S مينفعش أبدًا
    /// يوافق على حاجة زي دي.
    /// </summary>
    public interface ISaveShortcutDialog
    {
    }
}
