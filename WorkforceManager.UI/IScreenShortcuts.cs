namespace WorkforceManager.UI
{
    /// <summary>
    /// شاشة بتقبل اختصارات الكيبورد العامة (Ctrl+N/Ctrl+S/Ctrl+F). MainWindow
    /// بيوجّه الاختصار للشاشة المعروضة في MainContent بس — فاختصار شاشة
    /// العمال عمره ما يشتغل وانت على المنتجات. كل دالة بترجع true لو
    /// الشاشة عملت حاجة فعلًا، وبتنادي أوامر موجودة أصلًا في الـViewModel.
    /// </summary>
    public interface IScreenShortcuts
    {
        bool TryQuickAdd() => false;
        bool TrySave() => false;
        bool TryFocusSearch() => false;
    }
}
