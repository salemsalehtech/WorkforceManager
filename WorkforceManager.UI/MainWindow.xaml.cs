using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;
using WorkforceManager.UI.Views;

namespace WorkforceManager.UI
{
    /// <summary>
    /// النافذة الرئيسية: قائمة جانبية ثابتة + منطقة محتوى (MainContent)
    /// بتستبدل الـ View المعروض حسب الاختيار. كل شاشة بتتحل من الـ DI
    /// (Transient) — يعني كل تنقّل أو ضغطة على زرار القائمة بتبني الشاشة
    /// من جديد ببيانات طازة من قاعدة البيانات، فمفيش شاشة بتعرض أرقام قديمة.
    /// كل هاندلر متربط بحدثي Checked (تنقّل فعلي/كيبورد) وClick (إعادة
    /// تحميل عند الضغط على الشاشة المختارة أصلاً — Checked مبيتنفذش وقتها).
    /// أول Checked لزرار "العمال" بيحصل أثناء InitializeComponent قبل ما
    /// MainContent يتبني — الحارس (null check) بيتخطاه، والـ Constructor
    /// بيحمّل الشاشة الافتراضية بعدها.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly CurrentUserContext _currentUser;

        /// <summary>
        /// نطاق الجلسة اللي النافذة دي جزء منه. الشاشات بتتطلب منه مش من
        /// الجذر، عشان تموت مع الجلسة لما المستخدم يسجّل خروج — ده اللي
        /// بيخلي الحساب الجديد يلاقي شاشات نضيفة من غير تنظيف يدوي.
        /// </summary>
        private readonly IServiceProvider _session;

        public MainWindow(CurrentUserContext currentUser, IServiceProvider session)
        {
            _currentUser = currentUser;
            _session = session;

            InitializeComponent();

            // مكان الإشعارات بيتسجّل مرة واحدة هنا — بعدها أي شاشة
            // بتنادي Notify والإشعار بيوصل من غير ما تعرف مين بيعرضه
            Toasts.Register();

            // تاريخ اليوم بالعربي في بطاقة أسفل القائمة الجانبية
            TodayText.Text = DateTime.Today.ToString(
                "dddd d MMMM yyyy", new System.Globalization.CultureInfo("ar-EG"));

            ShowIdentity();
            RefreshActivityBadge();
            RefreshMemoryBadge();

            // شريط عنوان النافذة بيتلوّن بعد ما الـ Handle يتعمل — قبل
            // كده مفيش نافذة فعلية تتلوّن
            SourceInitialized += (_, _) => WindowChromeColors.Apply(this);

            // الشاشة الافتراضية عند فتح البرنامج: شاشة العمال
            MainContent.Content = _session.GetRequiredService<WorkersView>();

            // مايتقفلش من غير توقيع نهاية اليوم — شوف MainWindow_Closing
            Closing += MainWindow_Closing;

            // الحاوية بتفك تسجيلها مع النافذة: الجلسة بتتقفل فعليًا عند
            // تسجيل الخروج، والـ static كان هيفضل ماسك حاوية ميتة
            Closed += (_, _) => Toasts.Unregister();

            // التخطيط بيتصغّر لو الشاشة أضيق من مساحة التصميم — شوف ApplyUiScale
            SizeChanged += (_, _) => ApplyUiScale();
            ClampRestoreSizeToScreen();
        }

        // مساحة التصميم اللي كل الشاشات مبنية عليها — المرجع اللي المقياس
        // بيتحسب منه، مش مقاس شاشة جهاز بعينه.

        /// <summary>
        /// المقياس المطبّق على الواجهة دلوقتي. الديالوجات بتستخدمه عشان
        /// تطلع بنفس حجم الشاشة اللي وراها — من غيره التطبيق بيتكبّر
        /// ونوافذه لأ (فرق بيوصل 30% على شاشة 1080 و65% على 4K).
        ///
        /// 1 هي القيمة الافتراضية عن قصد: فيه رسايل بتظهر **قبل** ما
        /// النافذة الرئيسية تتعمل أصلًا (زي "البرنامج شغال بالفعل")،
        /// وساعتها مفيش مقياس تتقاس عليه.
        /// </summary>
        public static double CurrentScale { get; private set; } = 1.0;

        // أعرض شاشة (المنتجات) محتاجة ~918 + الشريط الجانبي
        private const double DesignWidth = 1200;

        // **الرقم ده مقيس مش مختار.** الشريط الجانبي هو اللي بيحدده: هو
        // العنصر الوحيد اللي لازم يظهر بالكامل من غير تمرير، وقياسه الفعلي
        // 706 (9 بنود تنقل + بطاقة اليوم + بطاقة الحساب + زرار الحفظ
        // النهائي). كان محطوط 700 بالتخمين، فـ"الحسابات الإدارية" كانت
        // بتتقص بـ6 بكسل وتختفي بالكامل على الشاشات الكبيرة — لأن التكبير
        // بيقلّل الارتفاع المنطقي (1020 ÷ 1.457 = 700).
        //
        // الفرق (54) مساحة بند تنقل إضافي تقريبًا. لو اتضاف بند جديد،
        // الـ ScrollViewer في الـ XAML هيمنع الاختفاء الصامت — بس الصح
        // إن الرقم ده يتقاس تاني بدل ما القايمة تفضل بتزحلق.
        private const double DesignHeight = 760;

        /// <summary>
        /// بيظبّط التخطيط كله على مساحة الشاشة — بيصغّر **وبيكبّر**.
        ///
        /// التكبير مقصود مش سهو. لما كان مقفول عند 1، الشاشة الكبيرة كانت
        /// بتوسّع الأعمدة بس والخط والأيقونات يفضلوا ثابتين — فالشريط
        /// الجانبي يطلع 288 عرض ونصه 14، يعني نص تايه في لوحة فاضية،
        /// بينما نفس النص على شاشة صغيرة يبان مريح. الشكوى كانت دي
        /// بالظبط: "كبير في شاشة وصغير في شاشة".
        ///
        /// النتيجة العملية: **كل شاشة 16:9 بتوصل لنفس العرض المنطقي
        /// (~1244) مهما كانت دقتها** — 1366 و1920 و4K بيرسموا نفس
        /// التخطيط حرفيًا، والفرق في الحجم الفيزيائي بس. التنازل إن
        /// الشاشة الكبيرة بتعرض نفس عدد الصفوف بحجم أريح مش صفوف أكتر
        /// (قرار مؤكد مع المستخدم بعد مقارنة بصرية).
        ///
        /// مفيش حد أقصى: النسبة مربوطة بالشاشة نفسها فهي محدودة
        /// بطبيعتها، وأي سقف مصطنع بيعمل قفزة عند حد معيّن.
        ///
        /// بيقيس على الأصغر في البعدين: التكبير على العرض لوحده كان
        /// هيقص المحتوى من تحت على شاشة قصيرة.
        /// </summary>
        private void ApplyUiScale()
        {
            if (UiScale is null || ActualWidth <= 0 || ActualHeight <= 0) return;

            var scale = Math.Min(ActualWidth / DesignWidth, ActualHeight / DesignHeight);

            UiScale.ScaleX = scale;
            UiScale.ScaleY = scale;

            // الديالوجات نوافذ منفصلة مش جوه الشجرة دي، فمش بتتكبّر معاها.
            // بتقرا الرقم ده عشان تطلع بنفس الحجم — شوف CrispWindows
            CurrentScale = scale;

            // العرض اللي التخطيط نفسه شايفه — بعد التصغير، مش عرض النافذة
            // الخام. على الشاشات العادية المقياس = 1 فالاتنين واحد.
            ApplySidebarWidth(ActualWidth / scale);
        }

        // الشريط نسبة من العرض المنطقي مش رقم ثابت.
        //
        // بعد ما التكبير اشتغل (ApplyUiScale فوق)، العرض المنطقي بيستقر
        // حوالي 1244–1337 على أي شاشة 16:9 — و15% منه أقل من الحد الأدنى،
        // فعمليًا **الحد الأدنى هو اللي بيحكم على الشاشات العادية**.
        // النسبة مش كود ميت رغم كده: على الشاشات العريضة جدًا (21:9 مثلاً،
        // العرض المنطقي ~1659) هي اللي بتمنع الشريط من إنه يبقى شعرة في
        // لوحة عريضة.
        private const double SidebarRatio = 0.15;

        // الحد الأدنى مقيس مش متخمّن: أطول بند ("تسجيل الإنتاج اليومي")
        // عرضه 119 بخط Tajawal عند 14، والثابت حواليه 84 (هوامش الحاوية
        // 12+12، حشو البند 16+16، الأيقونة 18 ومسافتها 10) = 203. والبند
        // المختار بيبقى SemiBold فبيتمدد شوية — 210 بتغطيه.
        private const double SidebarMin = 210;

        // فوق كده الشريط بيبقى مساحة ضايعة مش قايمة تنقل
        private const double SidebarMax = 320;

        private void ApplySidebarWidth(double logicalWidth)
        {
            if (SidebarColumn is null) return;

            SidebarColumn.Width = new GridLength(
                Math.Clamp(logicalWidth * SidebarRatio, SidebarMin, SidebarMax));
        }

        /// <summary>
        /// بيقصّ المقاس المستعاد (اللي بيرجعله لما يخرج من التكبير) على
        /// مساحة شاشة الجهاز.
        ///
        /// من غير ده النافذة بتفتح بـ1200×720 المكتوبة في الـ XAML حتى لو
        /// الشاشة أصغر من كده — فأول ما المستخدم يخرج من وضع التكبير
        /// تلاقي نص النافذة برّه الشاشة وشريط العنوان مش موجود يسحب بيه.
        /// SystemParameters بترجّع مساحة الشغل (من غير شريط المهام) بالـ DIP،
        /// وهي نفس وحدة Width/Height بتوع النافذة.
        /// </summary>
        private void ClampRestoreSizeToScreen()
        {
            var maxWidth = SystemParameters.WorkArea.Width;
            var maxHeight = SystemParameters.WorkArea.Height;

            if (Width > maxWidth) Width = Math.Max(MinWidth, maxWidth);
            if (Height > maxHeight) Height = Math.Max(MinHeight, maxHeight);
        }

        /// <summary>
        /// بيحدّث شارة "عمليات جديدة" على زرار سجل العمليات. بينادى بعد
        /// كل تنقّل — أرخص من مراقبة الداتابيز، واستعلام واحد مفهرس
        /// (CountSinceAsync) فمفيش تكلفة حقيقية تتحس. فتح شاشة السجل نفسها
        /// بيصفّر آخر وقت مشاهدة (ActivityLogViewModel.LoadAsync)، فالتنقّل
        /// اللي بعدها بيرجّع الشارة صفر.
        /// </summary>
        private async void RefreshActivityBadge()
        {
            using var scope = App.AppHost.Services.CreateScope();
            var log = scope.ServiceProvider.GetRequiredService<ActivityLogService>();
            var count = await log.GetUnseenCountAsync(_currentUser.AppUserId);

            ActivityBadgeText.Text = count > 99 ? "٩٩+" : count.ToString();
            ActivityBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// بيحدّث شارة "خطط مستحقة" على زرار الذاكرة. نفس نمط
        /// <see cref="RefreshActivityBadge"/> بالظبط — استعلام واحد رخيص
        /// (GetDueAsync بيرجّع المستحق النهارده أو المتأخر بس).
        /// </summary>
        private async void RefreshMemoryBadge()
        {
            using var scope = App.AppHost.Services.CreateScope();
            var memories = scope.ServiceProvider.GetRequiredService<ProductionMemoryService>();
            var count = (await memories.GetDueAsync(DateTime.Today)).Count;

            MemoryBadgeText.Text = count > 99 ? "٩٩+" : count.ToString();
            MemoryBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// بحث سريع عن عامل أو منتج من أي شاشة. القايمة بتتحمّل هنا (نفس
        /// المستودعات اللي شاشتي العمال والمنتجات بتستخدمها أصلاً) وتتبعت
        /// جاهزة للدايالوج، اللي مش محتاج DI خالص — شوف GlobalSearchDialog.
        /// </summary>
        private async void GlobalSearch_Click(object sender, RoutedEventArgs e)
        {
            var workers = await _session.GetRequiredService<IWorkerRepository>().GetActiveWithSkillsAsync();
            var products = await _session.GetRequiredService<IProductRepository>().GetActiveWithStagesAsync();

            var items = workers.Select(w => new GlobalSearchItem { Name = w.FullName, IsWorker = true })
                .Concat(products.Select(p => new GlobalSearchItem { Name = p.Name, IsWorker = false }))
                .OrderBy(i => i.Name)
                .ToList();

            var chosen = GlobalSearchDialog.Ask(this, items);
            if (chosen is null) return;

            // الترتيب مهم: نخلّي RadioButton الملاحة يحل الشاشة عادي الأول (زي أي تنقّل يدوي)،
            // وبعدين بس نلاقي الـView/ViewModel اللي اتحطوا فعلًا في MainContent ونظبط البحث
            // عليها — لو حلّينا View تانية بأنفسنا هنا كانت هتتكرّر (الشاشتين Transient)
            if (chosen.IsWorker) NavWorkersItem.IsChecked = true;
            else NavProductsItem.IsChecked = true;

            if (MainContent?.Content is WorkersView { DataContext: ViewModels.WorkersViewModel workersVm })
                workersVm.SearchText = chosen.Name;
            else if (MainContent?.Content is ProductsView { DataContext: ViewModels.ProductsViewModel productsVm })
                productsVm.SearchText = chosen.Name;
        }

        // ======================= جولة "إيه الجديد" (Tour.AppTourContent) =======================

        private enum TourAction { Next, Previous, Skip }

        private TaskCompletionSource<TourAction>? _tourStepTcs;

        /// <summary>
        /// بيشغّل الجولة خطوة خطوة: تنقّل للشاشة الصح لو الخطوة محتاجاها
        /// (وتبويب "تسجيل الإنتاج اليومي" الصح لو محدّد)، استنى التخطيط
        /// يستقر، لوّن سبوت لايت على العنصر المستهدف، واستنى "التالي"/"السابق"/
        /// "تخطي الكل" (أو Escape، أو دوسة على المنطقة المعتمة — نفس تأثير
        /// "تخطي الكل"). بينادى من App.OfferAppTourIfNewAsync وHelpViewModel.
        ///
        /// عنصر مش موجود دلوقتي (نادر — بس ممكن لو حد غيّر XAML بعدين
        /// ونسي يحدّث المحتوى)، أو موجود بس مخفي/بلا مساحة (عناصر بتظهر
        /// بشرط، زي زرار "إضافة حساب" اللي بيختفي لغير مدير القسم) —
        /// بيتخطّى بنفس اتجاه الحركة الحالي (تقدّم أو رجوع)، مش بيوقف
        /// الجولة كلها.
        /// </summary>
        public async Task RunTourAsync(IReadOnlyList<Tour.AppTourStep> steps)
        {
            TourOverlay.Visibility = Visibility.Visible;

            try
            {
                var i = 0;
                var delta = 1; // اتجاه الحركة الحالي — بيتغيّر لـ-1 لو المستخدم دوس "السابق"

                while (i >= 0 && i < steps.Count)
                {
                    var step = steps[i];

                    NavigateToTourScreen(step.Screen);

                    if (step.TabIndex is int tab)
                        _session.GetRequiredService<ViewModels.DailyEntryViewModel>().SelectedTabIndex = tab;

                    await Task.Delay(150); // استقرار التخطيط بعد التنقّل/التبويب قبل ما نقيس مكان العنصر

                    if (FindTourTarget(step.TargetElementName) is not { } target ||
                        target.Visibility != Visibility.Visible || target.ActualWidth <= 0 || target.ActualHeight <= 0)
                    {
                        i += delta; // موجود جوه الشجرة بس مخفي فعليًا دلوقتي، أو مش موجود أصلًا
                        continue;
                    }

                    PositionTourStep(target, i + 1, steps.Count, step.Title, step.Description);
                    TourBackButton.IsEnabled = i > 0;

                    _tourStepTcs = new TaskCompletionSource<TourAction>();
                    var action = await _tourStepTcs.Task;

                    if (action == TourAction.Skip) return;

                    delta = action == TourAction.Previous ? -1 : 1;
                    i += delta;
                }
            }
            finally
            {
                TourOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void NavigateToTourScreen(Tour.TourScreen screen)
        {
            switch (screen)
            {
                case Tour.TourScreen.Workers: NavWorkersItem.IsChecked = true; break;
                case Tour.TourScreen.Products: NavProductsItem.IsChecked = true; break;
                case Tour.TourScreen.DailyEntry: NavDailyEntryItem.IsChecked = true; break;
                case Tour.TourScreen.Memory: NavMemoryItem.IsChecked = true; break;
                case Tour.TourScreen.Evaluation: NavEvaluationItem.IsChecked = true; break;
                case Tour.TourScreen.Reports: NavReportsItem.IsChecked = true; break;
                case Tour.TourScreen.ActivityLog: NavActivityLogItem.IsChecked = true; break;
                case Tour.TourScreen.Settings: NavSettingsItem.IsChecked = true; break;
                case Tour.TourScreen.DepartmentAccounts: NavDepartmentAccountsItem.IsChecked = true; break;
                case Tour.TourScreen.None: break;
            }
        }

        /// <summary>بيدوّر على عنصر بالاسم: الشريط الجانبي الأول (ثابت دايمًا)، وبعدين الشاشة المفتوحة حاليًا</summary>
        private FrameworkElement? FindTourTarget(string name)
        {
            if (FindName(name) is FrameworkElement sidebarElement) return sidebarElement;
            return (MainContent?.Content as FrameworkElement)?.FindName(name) as FrameworkElement;
        }

        /// <summary>
        /// بيحسب مكان العنصر بالنسبة لـ TourOverlay (إحداثيات فعلية —
        /// شوف كومنت FlowDirection على TourOverlay في XAML)، ويبني ثقب
        /// السبوت لايت والفقاعة حواليه.
        /// </summary>
        private void PositionTourStep(
            FrameworkElement target, int stepNumber, int totalSteps, string title, string description)
        {
            var bounds = target.TransformToVisual(TourOverlay)
                .TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight));

            const double pad = 8;
            var hole = new Rect(bounds.X - pad, bounds.Y - pad, bounds.Width + pad * 2, bounds.Height + pad * 2);

            var outer = new RectangleGeometry(new Rect(0, 0, TourOverlay.ActualWidth, TourOverlay.ActualHeight));
            var inner = new RectangleGeometry(hole, 8, 8);
            TourDimPath.Data = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);

            TourHighlight.Width = hole.Width;
            TourHighlight.Height = hole.Height;
            TourHighlight.Margin = new Thickness(hole.X, hole.Y, 0, 0);

            TourStepCounter.Text = $"{stepNumber} من {totalSteps}";
            TourTitle.Text = title;
            TourDescription.Text = description;

            // الفقاعة تحت العنصر لو فيه مساحة، وإلا فوقه — عشان متطلعش برّه الشاشة
            const double calloutWidth = 340, calloutHeight = 210;
            var calloutTop = hole.Bottom + 12;
            if (calloutTop + calloutHeight > TourOverlay.ActualHeight)
                calloutTop = Math.Max(0, hole.Top - calloutHeight - 12);

            var calloutLeft = Math.Clamp(bounds.X, 12, Math.Max(12, TourOverlay.ActualWidth - calloutWidth - 12));
            TourCallout.Margin = new Thickness(calloutLeft, calloutTop, 0, 0);
        }

        private void TourNext_Click(object sender, RoutedEventArgs e) => _tourStepTcs?.TrySetResult(TourAction.Next);

        private void TourBack_Click(object sender, RoutedEventArgs e) => _tourStepTcs?.TrySetResult(TourAction.Previous);

        private void TourSkip_Click(object sender, RoutedEventArgs e) => _tourStepTcs?.TrySetResult(TourAction.Skip);

        /// <summary>دوسة على المنطقة المعتمة برّه الفقاعة = زي "تخطي الكل" — نفس تعامل أي Overlay بيتقفل بدوسة برّه</summary>
        private void TourDim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
            _tourStepTcs?.TrySetResult(TourAction.Skip);

        /// <summary>Escape يقفل الجولة لو شغّالة — بيتحقق من الظهور هنا عشان مايتصادمش مع أي استخدام تاني لـEscape في البرنامج</summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && TourOverlay.Visibility == Visibility.Visible)
                _tourStepTcs?.TrySetResult(TourAction.Skip);
        }

        /// <summary>
        /// اسم المصنع والقسم في رأس القايمة الجانبية وفي عنوان النافذة.
        ///
        /// كان مكتوب "إدارة الإنتاج والأجور" — جملة بتوصف البرنامج
        /// للمستخدم اللي فاتح البرنامج. المكان ده يجاوب سؤال أنفع:
        /// النسخة دي بتاعة مين.
        /// </summary>
        public void ShowIdentity()
        {
            var settings = Data.AppSettingsStore.Load();

            SidebarLogo.Refresh();
            Views.AppIcon.ApplyTo(this);

            var factory = string.IsNullOrWhiteSpace(settings.FactoryName)
                ? "WMS"
                : settings.FactoryName!.Trim();

            var department = settings.DepartmentName?.Trim() ?? "";

            FactoryText.Text = factory;

            // اللاتيني بيتقلب في واجهة عربية لو اتساب على اتجاه الأب
            FactoryText.FlowDirection = HasArabic(factory)
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;

            DepartmentText.Text = department;
            DepartmentText.Visibility = department.Length == 0
                ? Visibility.Collapsed
                : Visibility.Visible;

            Title = department.Length == 0 ? factory : $"{factory} — {department}";

            SignedInAsText.Text = _currentUser.ActorName;
        }

        /// <summary>
        /// خروج المستخدم الحالي وعرض شاشة الدخول تاني من غير ما البرنامج
        /// كله يقفل — مهم دلوقتي إن كل حساب إداري بقى ليه يوزر وباسورد
        /// لوحده، فأكتر من حد ممكن يستخدم نفس الجهاز في الشيفت.
        ///
        /// إلغاء شاشة الدخول (زرار الإغلاق) بعد الخروج معناه المستخدم
        /// مش عايز يدخل بحساب تاني دلوقتي — نفس قاعدة الإغلاق وقت فتح
        /// البرنامج (LoginWindow.Close_Click)، فالبرنامج بيقفل بدل ما
        /// يفضل واقف من غير حد داخل بيه.
        /// </summary>
        private async void Logout_Click(object sender, RoutedEventArgs e)
        {
            if (!Notify.Ask("تسجيل الخروج من الحساب الحالي؟", "تأكيد")) return;

            // نفس حارس معالج الإغلاق: ضغطتين سريعتين كانوا هيفتحوا فلوين
            if (_closeFlowRunning) return;
            _closeFlowRunning = true;

            try
            {
                // **نفس بوابة إغلاق البرنامج بالظبط** — تسجيل الخروج
                // بيسيب الجهاز لحساب تاني، فاليوم اللي مش موقّع بيضيع
                // مسؤوليته زي ما بيضيع لما البرنامج يتقفل
                if (!await RunFinalSaveFlowAsync(SignOffTrigger.Logout)) return;
            }
            finally
            {
                _closeFlowRunning = false;
            }

            // البوابة عدّت — النافذة بتتقفل جوه SignOutAndRestartSession،
            // ومعالج Closing مالوش داعي يسأل تاني
            _closeConfirmed = true;

            App.SignOutAndRestartSession();
        }

        /// <summary>
        /// مين طلب التوقيع. بيغيّر رسالة الرفض بس — المنطق واحد للتلاتة
        /// (شوف <see cref="RunFinalSaveFlowAsync"/>).
        /// </summary>
        private enum SignOffTrigger
        {
            /// <summary>زرار "حفظ نهائي" — المستخدم طالب الفلو بنفسه</summary>
            Button,

            /// <summary>محاولة قفل البرنامج</summary>
            WindowClose,

            /// <summary>تسجيل خروج — بيسيب الجهاز لحساب تاني</summary>
            Logout
        }

        private static bool HasArabic(string text) =>
            text.Any(c => c >= '؀' && c <= 'ۿ');

        private void NavWorkers_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return; // بيحصل مرة واحدة أثناء تهيئة النافذة
            MainContent.Content = _session.GetRequiredService<WorkersView>();
            RefreshActivityBadge();
            RefreshMemoryBadge();
        }

        private void NavProducts_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = _session.GetRequiredService<ProductsView>();
            RefreshActivityBadge();
            RefreshMemoryBadge();
        }

        private void NavDailyEntry_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = _session.GetRequiredService<DailyEntryView>();
            RefreshActivityBadge();
            RefreshMemoryBadge();
        }

        private void NavEvaluation_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = _session.GetRequiredService<ReportsView>();
            RefreshActivityBadge();
            RefreshMemoryBadge();
        }

        private void NavReports_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = _session.GetRequiredService<ReportBuilderView>();
            RefreshActivityBadge();
            RefreshMemoryBadge();
        }

        /// <summary>
        /// بيفتح شاشة الإنتاج اليومي على خطة ذاكرة بترتيب مراحلها.
        ///
        /// بيعلّم عنصر التنقل كمان — من غير كده الشاشة بتتغيّر والشريط
        /// الجانبي فاضل مأشّر على مكان تاني، فالمستخدم مش عارف هو فين.
        /// </summary>
        public async Task OpenDailyEntryForMemoryAsync(int memoryId, int productId, IReadOnlyList<int> stageOrder)
        {
            if (MainContent is null) return;

            var view = _session.GetRequiredService<DailyEntryView>();
            MainContent.Content = view;
            NavDailyEntryItem.IsChecked = true;
            RefreshActivityBadge();
            RefreshMemoryBadge();

            await _session.GetRequiredService<ViewModels.DailyEntryViewModel>()
                .StartFromMemoryAsync(memoryId, productId, stageOrder);
        }

        private void NavMemory_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = _session.GetRequiredService<MemoryView>();
            RefreshActivityBadge();
            RefreshMemoryBadge();
        }

        private void NavActivityLog_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = _session.GetRequiredService<ActivityLogView>();
            // فتح الشاشة بيصفّر آخر وقت مشاهدة جوه الـ ViewModel نفسها؛
            // الرجوع هنا بعد شوية (تنقّل تاني) هو اللي بيعرض الصفر فعليًا
            RefreshActivityBadge();
            RefreshMemoryBadge();
        }

        private void NavSettings_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = _session.GetRequiredService<SettingsView>();
            RefreshActivityBadge();
            RefreshMemoryBadge();
        }

        private void NavDepartmentAccounts_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = _session.GetRequiredService<DepartmentAccountsView>();
            RefreshActivityBadge();
            RefreshMemoryBadge();
        }

        private void NavHelp_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = _session.GetRequiredService<HelpView>();
            RefreshActivityBadge();
            RefreshMemoryBadge();
        }

        // ======================= توقيع نهاية اليوم =======================

        private async void FinalSave_Click(object sender, RoutedEventArgs e) =>
            await RunFinalSaveFlowAsync();

        /// <summary>
        /// اتحقّقت اليوم فعلًا واتقفل من غير سؤال — <see cref="MainWindow_Closing"/>
        /// بينادي Close() تاني بعد نجاح التوقيع، وده بيرجّعه هنا؛ من غير
        /// العلم ده كان هيدخل في حلقة (Closing → RunFinalSaveFlowAsync
        /// → Close → Closing تاني).
        /// </summary>
        private bool _closeConfirmed;

        /// <summary>يمنع تراكم أكتر من محاولة توقيع لو المستخدم دبس X كذا مرة بسرعة</summary>
        private bool _closeFlowRunning;


        /// <summary>
        /// مايسمحش بإغلاق البرنامج قبل ما اليوم يتوقّع — نفس فلو زرار
        /// "حفظ نهائي" بالظبط، غير إنه بيتنادى تلقائي عند محاولة الإغلاق.
        ///
        /// **لازم تفضل sync بالكامل، مفيش await هنا خالص.** WPF بيسيب
        /// الـ Window في حالة "بيتقفل" داخليًا (`_isClosing`) لحد ما
        /// المعالج يرجع تمامًا، حتى لو `e.Cancel = true` اتحطت قبل كده —
        /// أي `await` جوّه المعالج ده (زي ما كان هنا قبل الإصلاح ده)
        /// بيرجّع التحكم لـ WPF قبل ما الحالة دي تتصفّر، فأي محاولة تفتح
        /// ديالوج بعدها (حتى لو الحدث نفسه أكّد الإلغاء) بترمي
        /// "Cannot ... call ... ShowDialog ... while a Window is closing" —
        /// وده بالظبط اللي كان بيخلي البرنامج يقفل من غير ما يطلب التوقيع.
        /// الحل: نلغي فورًا وبشكل متزامن، ونأجّل الفلو الحقيقي (اللي فيه
        /// async وديالوجات) لدورة Dispatcher تالية بـ BeginInvoke، بعد ما
        /// WPF يخلّص إلغاء الإغلاق ده تمامًا ويصفّر حالته.
        /// </summary>
        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (_closeConfirmed) return; // إغلاق حقيقي بعد توقيع ناجح — سيبه يكمل

            // **بنلغي دايمًا الأول**: السؤال "اليوم مغطّى بتوقيع؟" محتاج
            // قراءة من قاعدة البيانات (فيه نشاط بعد آخر توقيع؟)، وده async —
            // وممنوع نعمل await هنا. فبنلغي، وبنسأل في دورة Dispatcher
            // تالية؛ لو طلع مغطّى فعلاً بنقفل فورًا من غير ما يحس المستخدم.
            e.Cancel = true;
            if (_closeFlowRunning) return; // فلو شغال بالفعل من محاولة إغلاق سابقة

            _closeFlowRunning = true;

            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    if (await RunFinalSaveFlowAsync(SignOffTrigger.WindowClose))
                    {
                        _closeConfirmed = true;
                        Close();
                    }
                }
                catch (Exception ex)
                {
                    // من غير الـ catch ده الاستثناء بيروح لمعالج
                    // DispatcherUnhandledException العام وبيتبلع كتوست
                    // عام مالوش علاقة بالتوقيع — والمستخدم مايعرفش إن
                    // فلو الإغلاق نفسه هو اللي وقع
                    Notify.Error($"حصل خطأ أثناء الحفظ النهائي:\n\n{ex.Message}", "خطأ");
                }
                finally
                {
                    _closeFlowRunning = false;
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// فلو "حفظ نهائي" الكامل: باسورد → مراجعة كل حاجة حصلت النهارده
        /// → توقيع. **بوابة واحدة بتتنادى من تلات أماكن** (الزرار، محاولة
        /// الإغلاق، تسجيل الخروج) عشان التلاتة يمشوا بنفس المسار بالظبط
        /// — نسخة تانية من نفس الفحص كانت هتفترق عن دي أول تعديل.
        ///
        /// اللي بيفرق حسب المشغّل هو **رسالة الرفض بس**: المستخدم لازم
        /// يفهم ليه الحاجة اللي طلبها ما حصلتش، مش يشوف ديالوج ظهر فجأة.
        /// </summary>
        /// <returns>النهارده بقى مغطّى بتوقيع (سواء دلوقتي أو من قبل)؟</returns>
        private async Task<bool> RunFinalSaveFlowAsync(SignOffTrigger trigger = SignOffTrigger.Button)
        {
            var today = DateTime.Today;

            List<ActivityEvent> pending;
            using (var checkScope = App.AppHost.Services.CreateScope())
            {
                var signOff = checkScope.ServiceProvider.GetRequiredService<DailyOperationsSignOffService>();

                // "مغطّى" = فيه توقيع ومفيش أي شغل بعده. لو المستخدم وقّع
                // الساعة 6 وحذف إنتاج الساعة 8، اليوم بيرجع محتاج توقيع
                if (await signOff.IsFullySignedOffAsync(today))
                    return true; // مفيش حاجة لسه محتاجة إمضاء — اقفل عادي

                pending = (await signOff.GetActivitySinceLastSignOffAsync(today)).ToList();
            }

            // الزرار مالوش رسالة: المستخدم هو اللي طلب الفلو، فالديالوج
            // اللي جاي هو الرد. الاتنين التانيين لازم يتقالهم ليه الحاجة
            // اللي طلبوها اتوقفت
            if (trigger is SignOffTrigger.WindowClose)
                Notify.Warn(
                    "فيه شغل النهارده لسه ما اتوقّعش عليه. لازم \"حفظ نهائي\" الأول قبل ما تقفل البرنامج.",
                    "مش هينفع تقفل");
            else if (trigger is SignOffTrigger.Logout)
                Notify.Warn(
                    "فيه شغل النهارده لسه ما اتوقّعش عليه. لازم \"حفظ نهائي\" الأول قبل ما تسجّل خروج.",
                    "مش هينفع تسجّل خروج");

            using var gateScope = App.AppHost.Services.CreateScope();
            var gate = gateScope.ServiceProvider.GetRequiredService<OperationsPasswordService>();

            var input = SensitiveActionDialog.Ask(
                this, "حفظ نهائي",
                "توقيع نهاية اليوم — بيغطي كل حاجة حصلت في البرنامج النهارده بدل ما تتأكّد من كل عملية لوحدها.",
                SensitiveActionKind.Save, await gate.IsConfiguredAsync(), reasonRequired: false);

            if (input is null) return false;

            // الملخص بيعرض اللي لسه محتاج توقيع بس — عرض عمليات موقّعة
            // خلاص كان هيخلي المستخدم يمضي على نفس الحاجة مرتين
            var summary = new DailySignOffSummaryDialog(today, pending) { Owner = this };
            if (summary.ShowDialog() != true) return false;

            try
            {
                using var signScope = App.AppHost.Services.CreateScope();
                await signScope.ServiceProvider.GetRequiredService<DailyOperationsSignOffService>()
                    .SignOffAsync(today, input.Password);

                Notify.Info($"اتوقّع يوم {today:yyyy/MM/dd} بنجاح.", "تم الحفظ النهائي");
                return true;
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "مش هينفع");
                return false;
            }
        }
    }
}
