using System.Windows.Controls;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// شاشة الإعدادات: الكود هنا شكلي بس (ربط الـ ViewModel) —
    /// كل المنطق في SettingsViewModel حسب نمط MVVM المتبع في المشروع.
    /// </summary>
    public partial class SettingsView : UserControl
    {
        public SettingsView(SettingsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // تحديث المعلومات المعروضة أول ما الشاشة تظهر
            Loaded += async (_, _) =>
            {
                viewModel.LoadInfo();                        // ملفات النسخ
                await viewModel.LoadOperationsStateAsync();  // حالة كلمة السر (قاعدة بيانات)
            };
        }
    }
}
