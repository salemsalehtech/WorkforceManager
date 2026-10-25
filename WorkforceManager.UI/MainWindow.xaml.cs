using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Helpers;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
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
    ///
    /// **الملف ده شكل النافذة والتنقّل والشريط الجانبي بس.** باقي
    /// المسؤوليات (كانت كلها هنا في ملف واحد ضخم، شوف CLAUDE.md) في
    /// ملفات partial class تانية جنبه: MainWindow.GlobalSearch.cs (البحث
    /// الشامل)، MainWindow.Tour.cs (محرك الجولة/السبوت لايت)،
    /// MainWindow.Sandbox.cs (وضع التجربة + التدريب التوجيهي)،
    /// MainWindow.SignOff.cs (بوابة توقيع نهاية اليوم).
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

            Current = this;

            InitializeComponent();

            // مكان الإشعارات بيتسجّل مرة واحدة هنا — بعدها أي شاشة
            // بتنادي Notify والإشعار بيوصل من غير ما تعرف مين بيعرضه
            Toasts.Register();

            // ضغطة على حفظ نهائي أو تسجيل الخروج جوّه "المزيد" بتقفل القايمة المنسدلة
            FinalSaveButton.Click += (_, _) => MoreToggle.IsChecked = false;
            LogoutButton.Click += (_, _) => MoreToggle.IsChecked = false;

            ShowIdentity();
            RefreshActivityBadge();
            RefreshMemoryBadge();
            RefreshNotificationBell();
            Loaded += async (_, _) => await LoadPlanBriefingAsync();

            // لو المدير غيّر صورته من شاشة الحسابات الإدارية وهو داخل،
            // الأفاتار هنا في القايمة الجانبية لازم يتحدّث فورًا معاها
            _currentUser.PhotoChanged += ShowIdentity;
            Closed += (_, _) => _currentUser.PhotoChanged -= ShowIdentity;

            // شريط عنوان النافذة بيتلوّن بعد ما الـ Handle يتعمل — قبل
            // كده مفيش نافذة فعلية تتلوّن
            SourceInitialized += (_, _) => WindowChromeColors.Apply(this);

            // الشاشة الافتراضية عند فتح البرنامج: الرئيسية — شوف
            // HomeView/HomeSummaryService. البدء نفسه في App.xaml.cs متغيّرش:
            // ديالوج التوقيع المتأخر، تذكيرات الذاكرة، وعروض الجولة/التعلّم
            // كلهم Dialogs مستقلة فوق النافذة دي مهما كان محتواها الحالي
            MainContent.Content = _session.GetRequiredService<HomeView>();

            // مايتقفلش من غير توقيع نهاية اليوم — شوف MainWindow_Closing
            // (MainWindow.SignOff.cs)
            Closing += MainWindow_Closing;

            // الحاوية بتفك تسجيلها مع النافذة: الجلسة بتتقفل فعليًا عند
            // تسجيل الخروج، والـ static كان هيفضل ماسك حاوية ميتة
            Closed += (_, _) => Toasts.Unregister();

            // قفل غير طبيعي للنافذة وسط وضع تجربة مايسيبش ملف SQLite مؤقت معلّق
            Closed += (_, _) => _sandbox?.Dispose();


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
        // العنصر الوحيد اللي لازم يظهر بالكامل من غير تمرير. قياسه الأصلي
        // كان 706 (9 بنود تنقل + بطاقة اليوم + بطاقة الحساب + زرار الحفظ
        // النهائي، قبل ما شاشة "الدليل" تتضاف كبند عاشر). كان محطوط 700
        // بالتخمين، فـ"الحسابات الإدارية" كانت بتتقص بـ6 بكسل وتختفي
        // بالكامل على الشاشات الكبيرة — لأن التكبير بيقلّل الارتفاع
        // المنطقي (1020 ÷ 1.457 = 700).
        // إعادة القياس عند تصميم القايمة الجانبية الجديدة (خط IBM Plex Sans
        // Arabic، نقل بطاقة اليوم) أكّدت إن العدد الحالي (10 بنود) لسه
        // داخل نفس الرقم — نفس ارتفاع السطر تقريبًا حرفيًا بين الخطين
        // (16.8 DIP في الاتنين عند نفس المقاس)، ونقل البطاقة بترتيب بس
        // من غير تغيير المجموع الكلي. شوف CLAUDE.md لتفاصيل القياس.
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
        }

        /// <summary>
        /// "الشريط مطوي" قديمًا — دلوقتي مفيش شريط جانبي أصلًا (التنقل في شريط علوي)، فالمساحة كلها للمحتوى.
        /// الخاصية فضلت عشان شبكات الكروت (GridColumnWidthConverter في العمال والمنتجات) بتربط عدد
        /// أعمدتها بيها: true = العرض الواسع (6 أعمدة). ثابتة true.
        /// </summary>
        public static readonly DependencyProperty IsSidebarCollapsedProperty = DependencyProperty.Register(
            nameof(IsSidebarCollapsed), typeof(bool), typeof(MainWindow), new PropertyMetadata(true));

        public bool IsSidebarCollapsed => (bool)GetValue(IsSidebarCollapsedProperty);

        /// <summary>
        /// لوجو WMS (الكبير في الشريط المفتوح أو الصغير في المطوي) = "الرئيسية" —
        /// نفس IsChecked اللي عنصر التنقل بيعمله، فالمؤشر الدهبي والبادچات بيتحدّثوا لوحدهم
        /// </summary>
        private void HomeLogo_Click(object sender, RoutedEventArgs e) => NavHomeItem.IsChecked = true;

        /// <summary>خانة البحث في الرئيسية — نفس GlobalSearch_Click بتاع زرار الشريط وCtrl+K بالظبط</summary>
        internal void OpenGlobalSearch() => GlobalSearch_Click(this, new RoutedEventArgs());

        /// <summary>تبويب "الحضور والغياب" جوّه إدارة العمال (كان في التسجيل اليومي)</summary>
        internal void OpenWorkerAttendance() => NavAttendanceItem.IsChecked = true;


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

            ActivityBadgeText.Text = BadgeFormat.CountText(count);
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

            MemoryBadgeText.Text = BadgeFormat.CountText(count);
            MemoryBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// جرس الإشعارات: يجمّع بس التنبيهات اللي مالهاش مكان عام تاني —
        /// عمليات سجل غير مشاهَدة، وعمال محتاجين انتباه (مفيش سعر يومية أو
        /// مهارات). الذاكرة المستحقة والأرصدة القديمة ليهم كارت خاص بأفعاله
        /// (ابدأ الآن/أجّل) في الرئيسية، فمش متكرّرين هنا عن قصد — شوف
        /// CLAUDE.md. خطة الشهر "المتأخرة" مستبعدة كمان: حسابها الحلقة
        /// الثقيلة (~110 استعلام) اللي تشخيص الأداء السابق لقاها، وحسابها
        /// هنا كل تنقّل كان هيرجّع نفس المشكلة.
        ///
        /// الاستعلامين رخيصين ومفهرسين (COUNT بس، مش تحميل قايمة كاملة) —
        /// نفس فلسفة RefreshActivityBadge/RefreshMemoryBadge بالحرف.
        /// </summary>
        private async void RefreshNotificationBell()
        {
            using var scope = App.AppHost.Services.CreateScope();
            var unseenActivity = await scope.ServiceProvider.GetRequiredService<ActivityLogService>()
                .GetUnseenCountAsync(_currentUser.AppUserId);
            var needsAttention = await scope.ServiceProvider.GetRequiredService<IWorkerRepository>()
                .CountNeedingAttentionAsync();

            NotificationActivityText.Text = $"{unseenActivity} عملية جديدة في سجل العمليات";
            NotificationActivityItem.Visibility = unseenActivity > 0 ? Visibility.Visible : Visibility.Collapsed;

            NotificationAttentionText.Text = $"{needsAttention} عامل محتاج انتباه";
            NotificationAttentionItem.Visibility = needsAttention > 0 ? Visibility.Visible : Visibility.Collapsed;

            var planBehind = _planBriefing?.BehindCount ?? 0;
            NotificationPlanItem.Visibility = planBehind > 0 ? Visibility.Visible : Visibility.Collapsed;

            var total = unseenActivity + needsAttention + (planBehind > 0 ? 1 : 0);
            NotificationEmptyText.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
            NotificationBellBadgeText.Text = BadgeFormat.CountText(total);
            NotificationBellBadge.Visibility = total > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// موجز الصبح من الخطة الشهرية — آخر نتيجة لـLoadPlanBriefingAsync. بيتحسب
        /// **مرة واحدة** عند فتح البرنامج: التتبّع الكامل أتقل من استعلامات الجرس
        /// التانية (COUNT بس)، وتكراره مع كل تنقّل هو بالظبط المشكلة اللي اتشالت
        /// عشانها خطة الشهر من الجرس قبل كده (شوف التعليق فوق RefreshNotificationBell).
        /// </summary>
        private MonthlyPlanTodayBriefingDto? _planBriefing;

        private async Task LoadPlanBriefingAsync()
        {
            try
            {
                using var scope = App.AppHost.Services.CreateScope();
                _planBriefing = await scope.ServiceProvider.GetRequiredService<MonthlyPlanStatisticsService>()
                    .GetTodayBriefingAsync(DateTime.Today);
            }
            catch (Exception)
            {
                // الموجز تنبيه مساعد بس — فشله مايمنعش البرنامج يفتح
                _planBriefing = null;
                return;
            }

            if (_planBriefing.BehindCount == 0) return;

            NotificationPlanText.Text =
                $"{_planBriefing.BehindCount} منتج متأخر في الخطة — مطلوب منهم النهارده {_planBriefing.RequiredToday:N0} قطعة";
            RefreshNotificationBell();

            var names = string.Join("، ", _planBriefing.TopProducts);
            Notify.Info(
                $"{_planBriefing.BehindCount} منتج متأخر عن خطة الشهر ({names}) — المطلوب منهم النهارده {_planBriefing.RequiredToday:N0} قطعة",
                "موجز الصبح");
        }

        private void NotificationPlanItem_Click(object sender, RoutedEventArgs e)
        {
            NotificationBellToggle.IsChecked = false;
            NavMonthlyPlanItem.IsChecked = true;
        }

        private void NotificationActivityItem_Click(object sender, RoutedEventArgs e)
        {
            NotificationBellToggle.IsChecked = false;
            NavActivityLogItem.IsChecked = true;
        }

        /// <summary>بيهبط على شاشة العمال وبيفعّل نفس فلتر "محتاج انتباه" اللي زرارها فوق الشاشة بيعمله — مفيش قايمة تانية بتتبني هنا</summary>
        private void NotificationAttentionItem_Click(object sender, RoutedEventArgs e)
        {
            NotificationBellToggle.IsChecked = false;
            if (OpenScreen<Views.WorkersView>() is { DataContext: ViewModels.WorkersViewModel vm })
                vm.ShowNeedsAttentionCommand.Execute(null);
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
                ? "إدارة قسم الصنفرة"
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

            AccountAvatar.PhotoData = _currentUser.PhotoData;
            AccountAvatar.Initials = NameInitials.From(_currentUser.DisplayName ?? _currentUser.Username);
        }

        private static bool HasArabic(string text) =>
            text.Any(c => c >= '؀' && c <= 'ۿ');

        /// <summary>
        /// الشاشات اللي اتنقلت جوّه شاشات تانية: نوع الشاشة المنقولة ← (بند التنقل الأب الظاهر
        /// في القايمة الجانبية). أي كود قديم بيعمل `NavMemoryItem.IsChecked = true` (البحث السريع،
        /// الجولة، بلاطات الرئيسية، التوقيع اليومي) لسه شغال زي ما هو — الحدث بيتحوّل هنا
        /// للأب وبيتفتح التبويب/القسم المناسب، من غير ما كل مكان يعرف إن الشاشة اتنقلت.
        /// سجل العمليات مش هنا عن قصد: بيتفتح كشاشة كاملة من جرس الإشعارات (شوف OpenStandaloneRoute).
        /// </summary>
        private Dictionary<Type, RadioButton> EmbeddedRoutes => new()
        {
            [typeof(ProductsView)] = NavProductionItem,
            [typeof(MonthlyPlanView)] = NavProductionItem,
            [typeof(DailyEntryView)] = NavProductionItem,
            [typeof(MemoryView)] = NavProductionItem,
            [typeof(AttendanceView)] = NavWorkersItem,
            [typeof(PenaltiesView)] = NavWorkersItem,
            [typeof(AdjustmentsView)] = NavWorkersItem,
            [typeof(ReportsView)] = NavWorkersItem,
            [typeof(ReportBuilderView)] = NavWorkersItem,
            [typeof(DepartmentAccountsView)] = NavSettingsItem,
            [typeof(HelpView)] = NavSettingsItem,
        };

        /// <summary>النافذة الحالية — بتستخدمها LazyScreenHost عشان تبني الشاشات المتداخلة من نفس نطاق الجلسة</summary>
        public static MainWindow? Current { get; private set; }

        /// <summary>بيبني شاشة (View) من نطاق الجلسة — نفس اللي NavigateToView_Checked بيعمله</summary>
        public object ResolveScreen(Type viewType) => _session.GetRequiredService(viewType);

        /// <summary>
        /// الشاشة النوع T اللي ظاهرة دلوقتي — مباشرة في MainContent أو متداخلة جوّه
        /// شاشة أب (تبويب/قسم). بديل `MainContent.Content is T` اللي بقى مش كفاية.
        /// </summary>
        internal T? ScreenOf<T>() where T : class =>
            MainContent?.Content as T
            ?? (MainContent?.Content as IEmbeddedScreenHost)?.Find(typeof(T)) as T;

        /// <summary>
        /// يفتح شاشة بعينها حتى لو جوّه صفحة كروت (مثلًا قايمة العمال جوّه إدارة العمال) ويرجّعها جاهزة —
        /// بيعلّم البند الأب، وبيطلب من الأب يفتح الصفحة ويبنيها فورًا. لازم للبحث السريع والجولة: بند الأب
        /// لوحده بيفتح على الكروت، فـ `ScreenOf&lt;T&gt;()` كان بيرجّع null والهبوط مابيعملش حاجة.
        /// </summary>
        internal T? OpenScreen<T>() where T : class
        {
            var type = typeof(T);
            var parent = type == typeof(WorkersView) ? NavWorkersItem : EmbeddedRoutes.GetValueOrDefault(type);
            if (parent is null || MainContent is null) return ScreenOf<T>();

            if (parent.IsChecked != true) parent.IsChecked = true;
            if (MainContent.Content is not IEmbeddedScreenHost && parent.Tag is Type parentType)
                MainContent.Content = _session.GetRequiredService(parentType);

            return (MainContent.Content as IEmbeddedScreenHost)?.Show(type) as T ?? ScreenOf<T>();
        }

        /// <summary>
        /// بيحوّل بند تنقل مخفي لشاشته الأب: بيعلّم الأب (فيبني الشاشة لو مش ظاهرة)،
        /// وبعدين بيختار التبويب/القسم ويبني الشاشة المتداخلة فورًا — قبل ما حد يستنى Loaded —
        /// فالكود اللي بعده (`ScreenOf&lt;T&gt;()`) يلاقيها جاهزة.
        /// </summary>
        private void RouteToEmbedded(RadioButton hidden, Type embedded, RadioButton parent)
        {
            hidden.IsChecked = false; // عشان نفس البند يتفعّل تاني لو اتدعى تاني

            if (parent.IsChecked != true)
                parent.IsChecked = true; // بيبني الشاشة الأب لو مش ظاهرة
            if (MainContent.Content is not IEmbeddedScreenHost)
                MainContent.Content = _session.GetRequiredService((Type)parent.Tag);

            (MainContent.Content as IEmbeddedScreenHost)?.Show(embedded);
        }

        /// <summary>
        /// شاشة كاملة من غير بند ظاهر في الشريط (سجل العمليات من الجرس، الإعدادات من "المزيد") — فمفيش بند ظاهر يتعلّم.
        /// بنشيل التحديد من بنود الشريط عشان ما يفضلش بند معلّم وإنت على شاشة تانية.
        /// </summary>
        private void OpenStandaloneRoute(RadioButton hidden, Type viewType)
        {
            hidden.IsChecked = false;
            foreach (var item in VisibleNavItems) item.IsChecked = false;
            MainContent.Content = _session.GetRequiredService(viewType);
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            MoreToggle.IsChecked = false;
            NavSettingsItem.IsChecked = true;
        }

        private IEnumerable<RadioButton> VisibleNavItems => new[]
        {
            NavHomeItem, NavWorkersItem, NavProductionItem
        };

        /// <summary>
        /// Handler عام لكل بنود التنقل الـ12 — كانوا 12 نسخة متطابقة
        /// (فرق نوع الشاشة بس)، أي شارة رابعة أو تغيير في شرط الـsandbox
        /// كان محتاج يتعدّل في الـ12 مكان مع بعض. الشاشة المطلوبة بتتحدد من
        /// Tag بتاع الزرار نفسه (مضبوط في XAML)، زي CollapsedNavIcon_Click.
        /// </summary>
        private void NavigateToView_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return; // بيحصل مرة واحدة أثناء تهيئة النافذة
            if (ExitSandboxOnRealNavigation()) return;
            if (sender is not FrameworkElement { Tag: Type viewType }) return;

            // شاشة اتنقلت جوّه شاشة تانية — حوّل للأب بدل ما تتفتح لوحدها
            if (sender is RadioButton hidden && EmbeddedRoutes.TryGetValue(viewType, out var parentNav))
            {
                RouteToEmbedded(hidden, viewType, parentNav);
                RefreshActivityBadge();
                RefreshMemoryBadge();
                RefreshNotificationBell();
                return;
            }

            // سجل العمليات والإعدادات: شاشات كاملة من غير بند ظاهر في الشريط
            if (sender is RadioButton { GroupName: "HiddenRoutes" } standalone &&
                (viewType == typeof(ActivityLogView) || viewType == typeof(SettingsView)))
            {
                OpenStandaloneRoute(standalone, viewType);
                RefreshActivityBadge();
                RefreshMemoryBadge();
                RefreshNotificationBell();
                return;
            }

            MainContent.Content = _session.GetRequiredService(viewType);
            RefreshActivityBadge();
            RefreshMemoryBadge();
            RefreshNotificationBell();
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

            NavDailyEntryItem.IsChecked = true; // مسار مخفي → إدارة الإنتاج ← تسجيل الإنتاج اليومي (RouteToEmbedded)
            RefreshActivityBadge();
            RefreshMemoryBadge();
            RefreshNotificationBell();

            await _session.GetRequiredService<ViewModels.DailyEntryViewModel>()
                .StartFromMemoryAsync(memoryId, productId, stageOrder);
        }

        /// <summary>
        /// قائمة كارت العامل ← تبويب الحضور/الجزاءات/السلف في إدارة العمال والعامل متحدد.
        /// نفس DailyEntryViewModel (Scoped) بتاع التسجيل اليومي، فالتنقّل ده بيرجع لنفس
        /// النسخة بشغلها اللي لسه مش محفوظ.
        /// </summary>
        public async Task OpenDailyEntryForWorkerAsync(int workerId, string workerName, ViewModels.DailyEntryWorkerTarget target)
        {
            if (MainContent is null) return;

            var tab = target switch
            {
                ViewModels.DailyEntryWorkerTarget.Penalty => NavPenaltiesItem,
                ViewModels.DailyEntryWorkerTarget.Adjustment => NavAdjustmentsItem,
                _ => NavAttendanceItem
            };
            tab.IsChecked = true;
            RefreshActivityBadge();
            RefreshMemoryBadge();
            RefreshNotificationBell();

            await _session.GetRequiredService<ViewModels.DailyEntryViewModel>()
                .FocusWorkerAsync(workerId, workerName, target);
        }

        /// <summary>
        /// قائمة كارت المنتج ← "تقرير الإنتاج": شاشة التقارير، موضوع الإنتاج،
        /// المنتج ده محدد بس.
        ///
        /// IsChecked = true بس، زي هبوط البحث السريع بالظبط (MainWindow.
        /// GlobalSearch.cs) — النداء ده مش بيبني الشاشة بنفسه: NavReports_Checked
        /// (اللي IsChecked بيطلقه) هو اللي بيبني الـView الجديد ويحطه في
        /// MainContent، فـMainContent.Content بعد السطر ده هو نفس النسخة
        /// بالظبط (View وViewModel، ReportBuilderViewModel Transient). لو
        /// الشاشة مفتوحة أصلًا IsChecked مبيتغيّرش فمفيش Checked تاني —
        /// وWhenLoaded بتاعة النسخة الحالية اتحلّت خلاص فـawait بيرجع فورًا.
        /// </summary>
        public async Task OpenProductReportAsync(int productId)
        {
            if (MainContent is null) return;

            NavReportsItem.IsChecked = true;
            if (ScreenOf<ReportBuilderView>() is not { } view) return;

            await view.WhenLoaded;
            ((ViewModels.ReportBuilderViewModel)view.DataContext).ShowProductionFor(productId);
        }

        /// <summary>قائمة كارت المنتج ← "الخطة الشهرية": مؤشر الكيبورد على خانة كمية المنتج ده، نفس منطق OpenProductReportAsync بالظبط</summary>
        public async Task OpenMonthlyPlanForProductAsync(int productId)
        {
            if (MainContent is null) return;

            NavMonthlyPlanItem.IsChecked = true;
            if (ScreenOf<MonthlyPlanView>() is not { } view) return;

            await view.WhenLoaded;
            view.FocusProductQuantity(productId);
        }

        /// <summary>
        /// الاتجاه العكسي: من صف منتج في الخطة الشهرية ← صفحة المنتج نفسه في
        /// شاشة المنتجات — بالـID زي OpenProductReportAsync/OpenMonthlyPlanForProductAsync
        /// بالظبط، مش بالاسم زي LandOnProduct (شوف MainWindow.GlobalSearch.cs).
        /// </summary>
        public async Task OpenProductDetailAsync(int productId)
        {
            if (MainContent is null) return;

            NavProductsItem.IsChecked = true;
            if (ScreenOf<ProductsView>() is not { } view) return;

            await view.WhenLoaded;
            view.ShowProductDetail(productId);
        }

        /// <summary>
        /// Escape يقفل الجولة لو شغّالة (بيتحقق من الظهور هنا عشان مايتصادمش
        /// مع أي استخدام تاني لـEscape في البرنامج)، وCtrl+K بيفتح "بحث سريع"
        /// من أي مكان — بديل لدوسة الماوس على الزرار، نفس فكرة أي اختصار
        /// بحث معروف. الديالوج Modal فمفيش خطر يتفتح مرتين مع بعض. Ctrl+B
        /// بيطوي/يفتح الشريط الجانبي — نفس فعل زرار الطي بالظبط.
        /// </summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && TourOverlay.Visibility == Visibility.Visible)
                _tourStepTcs?.TrySetResult(TourAction.Skip);

            if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                GlobalSearch_Click(this, e);
            }

            // Ctrl+N/S/F بيتوجّهوا للشاشة المعروضة دلوقتي بس (IScreenShortcuts) —
            // الديالوجات نوافذ لوحدها فمفيش اختصار من هنا بيوصل لشاشة ورا ديالوج مفتوح
            var action = KeyboardShortcuts.Resolve(e.Key, Keyboard.Modifiers);
            if (action == ShortcutAction.None) return;

            var screen = MainContent?.Content as IScreenShortcuts;
            e.Handled = action switch
            {
                ShortcutAction.QuickAdd => screen?.TryQuickAdd() ?? false,
                ShortcutAction.Save => screen?.TrySave() ?? false,
                // الشاشة اللي مالهاش خانة بحث خاصة بيها → البحث السريع الشامل (زي Ctrl+K)
                ShortcutAction.Search => screen?.TryFocusSearch() == true || OpenGlobalSearchFromShortcut(),
                _ => false
            };
        }

        private bool OpenGlobalSearchFromShortcut()
        {
            GlobalSearch_Click(this, new RoutedEventArgs());
            return true;
        }
    }
}
