using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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

        /// <summary>
        /// الدوس على خانة البحث بيفتح قايمة الاقتراحات.
        ///
        /// القايمة بتتقفل مع كل إضافة عامل، فالفتح لازم يبقى فعل صريح من
        /// المستخدم. مربوطة بدوسة الماوس بس مش بالفوكس: بعد الإضافة
        /// بالماوس الكود بيرجّع الفوكس للخانة (عشان يكتب اللي بعده)، ولو
        /// الفوكس كان بيفتح كانت هتترجع تفتح فورًا وتلغي القفل.
        /// </summary>
        private void WorkerSearch_Clicked(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: FlowStageRow stage })
                stage.IsPickerOpen = true;
        }

        // ============ الزحلقة للمرحلة بعد إضافة عامل ============
        // المستخدم كان بينزل بالماوس بعد كل عامل عشان يوصل للمرحلة اللي
        // بعدها. دلوقتي الشاشة بتوقّف المرحلة اللي لسه مالياها في أول
        // الإطار، فاللي بعدها بتبقى تحتيها على طول — ولو محتاج يضيف عامل
        // تاني على نفس المرحلة لسه قدامه مش محتاج يرجع لفوق.

        /// <summary>قايمة مراحل كل رحلة مفتوحة — عشان نعرف نزحلق في أنهي واحدة</summary>
        private readonly Dictionary<FlowSessionViewModel, ItemsControl> _stageLists = new();

        /// <summary>مسافة صغيرة فوق البطاقة عشان متلزقش في حافة الإطار</summary>
        private const double StageTopGap = 12;

        private void FlowStages_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ItemsControl { DataContext: FlowSessionViewModel session } list) return;

            _stageLists[session] = list;

            // -= الأول: الشاشة ممكن تتحمّل تاني (تبديل تبويبات) والاشتراك
            // كان هيتكرر فتحصل الزحلقة مرتين
            session.WorkerAdded -= OnWorkerAdded;
            session.WorkerAdded += OnWorkerAdded;
        }

        private void FlowStages_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ItemsControl { DataContext: FlowSessionViewModel session }) return;

            session.WorkerAdded -= OnWorkerAdded;
            _stageLists.Remove(session);
        }

        private void OnWorkerAdded(FlowStageRow stage)
        {
            var list = _stageLists.FirstOrDefault(pair => pair.Key.FlowStages.Contains(stage)).Value;
            if (list is null) return;

            // بعد ما التخطيط يخلص: شريحة العامل لسه بتتضاف والقايمة
            // بتتقفل، والارتفاع بيتغير — الزحلقة قبل كده بتحسب مكان قديم
            Dispatcher.BeginInvoke(
                new Action(() => ScrollStageToTop(list, stage)), DispatcherPriority.Loaded);
        }

        private void ScrollStageToTop(ItemsControl list, FlowStageRow stage)
        {
            if (list.ItemContainerGenerator.ContainerFromItem(stage) is not FrameworkElement container)
                return;

            var scroller = FindAncestor<ScrollViewer>(list);
            if (scroller is null)
            {
                container.BringIntoView(); // احتياطي: على الأقل تبقى ظاهرة
                return;
            }

            var top = container.TransformToAncestor(scroller).Transform(new Point(0, 0)).Y;
            scroller.ScrollToVerticalOffset(scroller.VerticalOffset + top - StageTopGap);
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
