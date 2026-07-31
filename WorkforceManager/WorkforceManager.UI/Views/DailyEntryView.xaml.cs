using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// شاشة التسجيل اليومي: الكود هنا شكلي بس (ربط الـ ViewModel + تنقّل
    /// الكيبورد في قايمة اقتراحات العمال) — كل منطق الشغل في
    /// DailyEntryViewModel حسب نمط MVVM المتبع في المشروع.
    /// </summary>
    public partial class DailyEntryView : UserControl
    {
        public DailyEntryView(DailyEntryViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // تحميل المنتجات والعمال أول ما الشاشة تظهر
            Loaded += async (_, _) => await viewModel.InitializeAsync();
        }

        // ============ خانة البحث عن عامل في بطاقة المرحلة ============
        // الهدف: إدخال بالكيبورد من غير ما اليد تسيبه — تكتب حروف، تختار
        // بالسهمين، Enter يضيف والخانة تفضى للعامل اللي بعده.

        private void WorkerSearch_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: FlowStageRow stage }) return;

            switch (e.Key)
            {
                case Key.Down:
                    stage.MoveSuggestion(1);
                    e.Handled = true;
                    break;

                case Key.Up:
                    stage.MoveSuggestion(-1);
                    e.Handled = true;
                    break;

                case Key.Escape:
                    stage.ResetWorkerPicker();
                    e.Handled = true;
                    break;

                case Key.Enter:
                    // مفيش اختيار وفيه أكتر من نتيجة = مش هنخمّن، المستخدم ينزل بالسهم
                    if (stage.TryPickSuggestion()) TryAddWorker(stage);
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>ضغطة واحدة على اسم في القايمة بتضيفه — من غير ما يدوّر على زرار</summary>
        private void Suggestions_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListBox { DataContext: FlowStageRow stage } list) return;
            // الدوس على شريط التمرير مش اختيار عامل
            if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is null) return;
            if (stage.SelectedWorkerToAdd is null) return;

            TryAddWorker(stage);
            e.Handled = true;

            // الفوكس رجع للخانة عشان يكتب اسم العامل اللي بعده علطول
            FindAncestor<StackPanel>(list)?.MoveFocus(
                new TraversalRequest(FocusNavigationDirection.First));
        }

        /// <summary>الاختيار بالسهمين لازم يفضل باين لو القايمة أطول من الإطار</summary>
        private void Suggestions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox { SelectedItem: { } item } list) list.ScrollIntoView(item);
        }

        private static void TryAddWorker(FlowStageRow stage)
        {
            if (stage.AddWorkerCommand.CanExecute(null)) stage.AddWorkerCommand.Execute(null);
        }

        private static T? FindAncestor<T>(DependencyObject? from) where T : DependencyObject
        {
            for (var node = from; node is not null; node = VisualTreeHelper.GetParent(node))
                if (node is T match) return match;

            return null;
        }
    }
}
