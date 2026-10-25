using System.Windows.Controls;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// تبويب في إدارة العمال (كان في التسجيل اليومي). الكود هنا شكلي بس — نفس
    /// DailyEntryViewModel بتاع الجلسة، فاليوم المختار والبيانات اللي لسه
    /// مش محفوظة بيتشاركوا مع تسجيل الإنتاج اليومي.
    /// </summary>
    public partial class AdjustmentsView : UserControl, IScreenShortcuts
    {
        public AdjustmentsView(DailyEntryViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // InitializeAsync بتتنفذ مرة واحدة بس للجلسة (حارس _initialized جواها)
            Loaded += async (_, _) => await viewModel.InitializeAsync();
        }
    }
}
