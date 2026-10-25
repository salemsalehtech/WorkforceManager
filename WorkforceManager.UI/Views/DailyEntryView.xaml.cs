using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.Services;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// شاشة التسجيل اليومي: الكود هنا شكلي بس (ربط الـ ViewModel + تنقّل
    /// الكيبورد في قايمة اقتراحات العمال) — كل منطق الشغل في
    /// DailyEntryViewModel حسب نمط MVVM المتبع في المشروع.
    /// </summary>
    public partial class DailyEntryView : UserControl, IScreenShortcuts, IEmbeddedScreenHost, ILandingResettable, INestedLandingHost
    {
        private readonly IServiceScopeFactory? _scopeFactory;
        private readonly List<HubCardInfo> _cards = new();

        public DailyEntryView(DailyEntryViewModel viewModel, IServiceScopeFactory scopeFactory)
        {
            InitializeComponent();
            DataContext = viewModel;
            _scopeFactory = scopeFactory;

            SetupLanding(viewModel);

            // تحميل المنتجات والعمال أول ما الشاشة تظهر
            Loaded += (_, _) => EntranceAnimation.PlayFadeSlideIn(this);
            Loaded += async (_, _) => await viewModel.InitializeAsync();
            Loaded += async (_, _) => await RefreshStatsAsync();

            // KeyDown (مش Preview) عن قصد: خانة اختيار العامل في المرحلة بتعلّم
            // Enter إنه اتعالج في PreviewKeyDown بتاعها (إضافة عامل + نقل للمرحلة
            // اللي بعدها)، فالحدث ده مابيوصلش هنا أصلًا — مفيش معالجة مزدوجة
            KeyDown += AdvanceOnEnter;
        }

        // ===== صفحة الكروت: كارت لكل تبويب (الأرقام هي نفسها SelectedTabIndex في الـ ViewModel) =====

        private static readonly (string Title, string Description, MaterialDesignThemes.Wpf.PackIconKind Icon)[] PageDefinitions =
        {
            ("إنتاج العمال اليوم", "تسجيل رحلات إنتاج العمال بالمنتج والمراحل", MaterialDesignThemes.Wpf.PackIconKind.ClipboardEditOutline),
            ("الرصيد الأولي", "قطع شغّالة من قبل ما تبدأ التسجيل في البرنامج", MaterialDesignThemes.Wpf.PackIconKind.PackageVariant),
            ("مراجعة اليوم", "كل اللي اتسجّل في اليوم المختار — مراجعة وتعديل", MaterialDesignThemes.Wpf.PackIconKind.FormatListBulleted),
            ("خطط الإنتاج المحفوظة", "خطط جاهزة تبدأ منها التسجيل بضغطة", MaterialDesignThemes.Wpf.PackIconKind.BookmarkMultipleOutline),
            ("الهالك", "قطع اتشالت من الخط ومش هتتكمّل", MaterialDesignThemes.Wpf.PackIconKind.DeleteSweepOutline),
        };

        private void SetupLanding(DailyEntryViewModel viewModel)
        {
            for (var i = 0; i < PageDefinitions.Length; i++)
            {
                var (title, description, icon) = PageDefinitions[i];
                _cards.Add(new HubCardInfo { Key = i.ToString(), Title = title, Description = description, Icon = icon });
            }

            Landing.Setup("تسجيل يوميات العمال", "اختار اللي عايز تسجّله أو تراجعه", _cards);
            Landing.CardChosen += card => viewModel.OpenTab(int.Parse(card.Key));
            PageBar.BackRequested += ShowLanding;

            // أي طلب برمجي لتبويب (الجولة، البحث السريع، ابدأ الآن من الذاكرة) بيفتح الصفحة من الكروت
            viewModel.TabRequested += () => ShowPage(viewModel.SelectedTabIndex);
        }

        // ===== INestedLandingHost: إدارة الإنتاج بتعرض المسار بتاعنا في شريطها =====

        public event Action? OpenPageChanged;

        public string? OpenPageTitle =>
            PagesLayer.Visibility == Visibility.Visible && DataContext is DailyEntryViewModel vm &&
            vm.SelectedTabIndex >= 0 && vm.SelectedTabIndex < PageDefinitions.Length
                ? PageDefinitions[vm.SelectedTabIndex].Title
                : null;

        public void CloseOpenPage() => ShowLanding();

        public void ShowLanding()
        {
            PagesLayer.Visibility = Visibility.Collapsed;
            Landing.Visibility = Visibility.Visible;
            _ = RefreshStatsAsync();
            OpenPageChanged?.Invoke();
        }

        private void ShowPage(int index)
        {
            if (index < 0 || index >= PageDefinitions.Length) return;

            Landing.Visibility = Visibility.Collapsed;
            PagesLayer.Visibility = Visibility.Visible;
            PageBar.Set("تسجيل يوميات العمال", PageDefinitions[index].Title);
            OpenPageChanged?.Invoke();
        }

        private async Task RefreshStatsAsync()
        {
            if (_scopeFactory is null) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var stats = await scope.ServiceProvider.GetRequiredService<HubStatsService>()
                    .GetDailyEntryStatsAsync(DateTime.Today);

                _cards[0].StatText = stats.ProductionRecordsToday == 0 ? "لسه مفيش تسجيل النهارده" : $"{stats.ProductionRecordsToday} سجل النهارده";
                _cards[1].StatText = stats.ActiveInitialBalances == 0 ? "" : $"{stats.ActiveInitialBalances} رصيد شغّال";
                _cards[3].StatText = stats.DueMemoryPlans == 0 ? "" : $"{stats.DueMemoryPlans} خطة مستحقة";
                _cards[4].StatText = stats.ScrapPiecesToday == 0 ? "" : $"{stats.ScrapPiecesToday} قطعة هالك النهارده";
            }
            catch
            {
                // الأرقام زينة على الكروت — فشل تحميلها مايمنعش فتح أي صفحة
            }
        }

        // ===== IEmbeddedScreenHost: الذاكرة بقت تبويب هنا (MemoryTab) =====

        public object? Show(Type screenType)
        {
            if (screenType != typeof(MemoryView) || DataContext is not DailyEntryViewModel vm) return null;

            vm.OpenTab(DailyEntryViewModel.MemoryTab);
            return MemoryHost.EnsureCreated();
        }

        public object? Find(Type screenType) => screenType == typeof(MemoryView) ? MemoryHost.Content : null;

        public FrameworkElement? FindElement(string name) =>
            (MemoryHost.Content as FrameworkElement)?.FindName(name) as FrameworkElement;

        /// <summary>Enter في خانة إدخال عادية (قطع، سبب، مبلغ...) = انتقل للخانة اللي بعدها بترتيب Tab</summary>
        private static void AdvanceOnEnter(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
            if (!KeyboardShortcuts.ShouldAdvanceOnEnter(e.OriginalSource)) return;

            ((UIElement)e.OriginalSource).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }

        /// <summary>
        /// Ctrl+S حسب التبويب المفتوح: الإنتاج → حفظ الرحلة
        /// اللي فيها المؤشر، أو الرحلة الوحيدة لو مفيش غيرها. لو فيه أكتر من
        /// رحلة والمؤشر برّاهم مفيش حفظ — منخمّنش أنهي واحدة المقصودة.
        /// </summary>
        public bool TrySave()
        {
            if (DataContext is not DailyEntryViewModel vm) return false;

            return vm.SelectedTabIndex switch
            {
                DailyEntryViewModel.ProductionTab => KeyboardShortcuts.TryExecute(FocusedFlow(vm)?.SaveFlowCommand),
                _ => false
            };
        }

        private static FlowSessionViewModel? FocusedFlow(DailyEntryViewModel vm)
        {
            for (var node = Keyboard.FocusedElement as DependencyObject; node is not null;
                 node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
                if (node is FrameworkElement { DataContext: FlowSessionViewModel flow }) return flow;

            return vm.FlowSessions.Count == 1 ? vm.FlowSessions[0] : null;
        }

        /// <summary>
        /// تصليحات الخطة الشهرية لهذا المنتج/اليوم — Upsert مستقل تمامًا
        /// عن حفظ الرحلة، شوف FlowSessionViewModel.SaveCorrectionAsync.
        /// </summary>
        private async void CorrectionBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox { Tag: FlowSessionViewModel session }) return;

            if (!int.TryParse(session.CorrectionText, out var quantity))
            {
                session.CorrectionText = "0";
                quantity = 0;
            }
            else
            {
                session.CorrectionText = quantity.ToString();
            }

            try
            {
                await session.SaveCorrectionAsync(quantity);
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "خطأ في حفظ التصليح");
            }
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
            if (sender is not ListBox { DataContext: FlowStageRow stage }) return;
            // الدوس على شريط التمرير مش اختيار عامل
            if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is null) return;
            if (stage.SelectedWorkerToAdd is null) return;

            TryAddWorker(stage);
            e.Handled = true;

            // الفوكس مش بيترجّع هنا: OnWorkerAdded بيوديه لخانة المرحلة
            // اللي بعدها — مكان واحد بيقرر يروح فين، سواء الإضافة كانت
            // بالماوس أو بـ Enter
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

        /// <summary>
        /// شاشة "العمال على المراحل" بتتربط على الرحلة المفتوحة (ActiveFlow) فالـDataContext بتاعها بيتغيّر من غير
        /// ما Loaded يتنادى تاني — نفك اشتراك القديمة ونشترك في الجديدة عشان الزحلقة/الفوكس للمرحلة اللي بعدها تفضل شغالة.
        /// </summary>
        private void FlowStages_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is not ItemsControl list) return;

            if (e.OldValue is FlowSessionViewModel old)
            {
                old.WorkerAdded -= OnWorkerAdded;
                _stageLists.Remove(old);
            }

            if (e.NewValue is FlowSessionViewModel current)
            {
                _stageLists[current] = list;
                current.WorkerAdded -= OnWorkerAdded;
                current.WorkerAdded += OnWorkerAdded;
            }
        }

        private void FlowStages_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ItemsControl { DataContext: FlowSessionViewModel session }) return;

            session.WorkerAdded -= OnWorkerAdded;
            _stageLists.Remove(session);
        }

        private void OnWorkerAdded(FlowStageRow stage)
        {
            var session = _stageLists.Keys.FirstOrDefault(s => s.FlowStages.Contains(stage));
            if (session is null || !_stageLists.TryGetValue(session, out var list)) return;

            // أول مرحلة بعدها ليها عمال مؤهلين: المرحلة اللي مالهاش
            // مؤهلين خانتها مخفية أصلاً، فالمؤشر كان هيروح لمكان مسدود
            var next = session.FlowStages
                .SkipWhile(row => row != stage)
                .Skip(1)
                .FirstOrDefault(row => !row.HasNoQualified);

            // بعد ما التخطيط يخلص: شريحة العامل لسه بتتضاف والقايمة
            // بتتقفل، والارتفاع بيتغير — الزحلقة قبل كده بتحسب مكان قديم
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // الترتيب مهم: تركيز الفوكس بيخلي WPF يزحلق لوحده عشان
                // يبيّن الخانة، فبنسيبه يعمل كده الأول وبعدين نزحلق إحنا
                // للمكان اللي إحنا عايزينه — آخر زحلقة هي اللي بتفضل
                if (next is not null) FocusStageSearch(list, next);
                ScrollStageToTop(list, stage);
            }), DispatcherPriority.Loaded);
        }

        /// <summary>
        /// بيحط المؤشر في خانة بحث المرحلة دي من غير ما يفتح قايمتها.
        ///
        /// عشان اللي بيوزّع على خط ١١ مرحلة يكتب اسم + Enter، اسم +
        /// Enter، من غير ما يمسك الماوس. القايمة مبتفتحش بالفوكس — أول
        /// حرف بيكتبه هو اللي بيفتحها.
        /// </summary>
        private static void FocusStageSearch(ItemsControl list, FlowStageRow stage)
        {
            if (list.ItemContainerGenerator.ContainerFromItem(stage) is not DependencyObject container)
                return;

            if (FindDescendant<TextBox>(container, "WorkerSearchBox") is { } box)
                box.Focus();
        }

        private static T? FindDescendant<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);

                if (child is T match && match.Name == name) return match;
                if (FindDescendant<T>(child, name) is { } deeper) return deeper;
            }

            return null;
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
