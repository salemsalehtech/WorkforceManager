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

            // حالة الطي المحفوظة من آخر مرة — من غير حركة، الحركة بس
            // لدوسة المستخدم الحية. قبل أول ApplyUiScale عشان الـResize
            // الأول (عند فتح النافذة) يحترم الحالة دي من غير وميض
            ApplyInitialSidebarState();

            // التخطيط بيتصغّر لو الشاشة أضيق من مساحة التصميم — شوف ApplyUiScale
            SizeChanged += (_, _) => ApplyUiScale();
            ClampRestoreSizeToScreen();

            InitializeNavIndicator();
        }

        // ======================= المؤشر الدهبي المنزلق (القايمة الجانبية) =======================

        /// <summary>
        /// true بعد أول تموضع حقيقي (بعد ما النافذة تحمّل فعليًا) — أي
        /// Checked بعده بيتحرك بانزلاق (Storyboard)، مش بيتحط فجأة.
        /// </summary>
        private bool _navIndicatorPositioned;

        /// <summary>
        /// بيوصّل مستمع Checked مشترك على كل بنود التنقل العشرة — **زيادة
        /// على** الـhandlers الموجودة (NavWorkers_Checked إلخ)، مش بديل
        /// عنهم؛ الاتنين بيشتغلوا مع بعض على نفس الحدث من غير تعارض. غرضه
        /// الوحيد تحريك NavIndicator، منفصل تمامًا عن منطق التنقل نفسه.
        /// </summary>
        private void InitializeNavIndicator()
        {
            foreach (var item in new[]
            {
                NavHomeItem, NavWorkersItem, NavProductsItem, NavDailyEntryItem, NavMemoryItem, NavEvaluationItem,
                NavReportsItem, NavActivityLogItem, NavSettingsItem, NavDepartmentAccountsItem, NavHelpItem
            })
            {
                item.Checked += NavItem_Checked;
            }
        }

        /// <summary>
        /// أول Checked بيحصل مصطنع جوّه InitializeComponent (IsChecked="True"
        /// على NavWorkersItem في الـXAML) قبل ما النافذة تتحمّل فعليًا —
        /// التخطيط وقتها مش جاهز، فـTransformToAncestor هيرجّع إحداثيات
        /// غلط. بدل ما نتجاهله (زي حراس NavX_Checked اللي بتشيك MainContent)،
        /// بنأجّل التموضع الأول لحدث Loaded بالظبط — نفس مبدأ "استنى
        /// التحميل الطبيعي بدل نداء ينافسه" المستخدم في أكتر من مكان في
        /// المشروع — من غير حركة (تموضع مباشر، مش انزلاق، أول مرة بس).
        /// </summary>
        private void NavItem_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton item) return;

            if (!IsLoaded)
            {
                RoutedEventHandler? deferred = null;
                deferred = (_, _) =>
                {
                    Loaded -= deferred;
                    PositionNavIndicator(item, animate: false);
                    _navIndicatorPositioned = true;
                };
                Loaded += deferred;
                return;
            }

            PositionNavIndicator(item, animate: _navIndicatorPositioned);
            _navIndicatorPositioned = true;
        }

        /// <summary>
        /// بيحرّك NavIndicator لموضع البند المختار. `TransformToAncestor`
        /// نفس تقنية PositionTourStep الموجودة فعلًا في محرك الجولة —
        /// بيحسب الموضع الحقيقي بغض النظر عن التمرير جوّه NavScrollViewer.
        /// انزلاق جاري وسط تحديد جديد بيتلغي/يتستبدل لوحده (سلوك WPF
        /// الطبيعي لـStoryboard جديدة على نفس الخاصية)، فمفيش تكديس أو قفزة.
        /// </summary>
        private void PositionNavIndicator(RadioButton item, bool animate)
        {
            var targetY = item.TransformToAncestor(NavIndicatorHost).Transform(new Point(0, 0)).Y;
            var targetHeight = item.ActualHeight;

            if (!animate)
            {
                NavIndicatorTransform.Y = targetY;
                NavIndicator.Height = targetHeight;
                NavIndicator.Opacity = 1;
                return;
            }

            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromSeconds(0.2);

            var moveAnimation = new DoubleAnimation { To = targetY, Duration = duration, EasingFunction = easing };
            Storyboard.SetTarget(moveAnimation, NavIndicatorTransform);
            Storyboard.SetTargetProperty(moveAnimation, new PropertyPath(TranslateTransform.YProperty));

            var heightAnimation = new DoubleAnimation { To = targetHeight, Duration = duration, EasingFunction = easing };
            Storyboard.SetTarget(heightAnimation, NavIndicator);
            Storyboard.SetTargetProperty(heightAnimation, new PropertyPath(FrameworkElement.HeightProperty));

            var storyboard = new Storyboard();
            storyboard.Children.Add(moveAnimation);
            storyboard.Children.Add(heightAnimation);
            storyboard.Begin();
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

        // عرض الشريط وهو مطوي — بالظبط عرض زرار الطي (30) + هامشيه (9+9
        // تقريبًا) عشان يفضل الزرار واقف في مكان معقول، مش ملزوق في الحرف
        private const double SidebarCollapsedWidth = 52;

        private const int SidebarToggleAnimationMs = 220;

        private bool _isSidebarCollapsed;

        /// <summary>
        /// نسخة قابلة للربط من _isSidebarCollapsed — عشان شاشات زي العمال
        /// والمنتجات تقدر تربط عدد أعمدة شبكة الكروت بحالة الشريط
        /// (GridColumnWidthConverter، AncestorType=Window) من غير ما تعرف
        /// أي تفاصيل داخلية عن MainWindow. بتتحدّث مع _isSidebarCollapsed
        /// في نفس اللحظة في كل مكان بيتغيّر فيه (ApplyInitialSidebarState،
        /// AnimateSidebarCollapse).
        /// </summary>
        public static readonly DependencyProperty IsSidebarCollapsedProperty = DependencyProperty.Register(
            nameof(IsSidebarCollapsed), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

        public bool IsSidebarCollapsed
        {
            get => (bool)GetValue(IsSidebarCollapsedProperty);
            private set => SetValue(IsSidebarCollapsedProperty, value);
        }

        // آخر عرض منطقي وصل من ApplyUiScale — لازم نحتفظ بيه عشان زرار
        // الطي يقدر يحسب عرض حالة الفتح الصح لحظة الدوسة، من غير ما يستنى
        // Resize جديد. القيمة الافتراضية معقولة لحد أول SizeChanged.
        private double _lastLogicalWidth = 1244;

        private void ApplySidebarWidth(double logicalWidth)
        {
            if (SidebarBorder is null) return;

            _lastLogicalWidth = logicalWidth;

            // مطوي: العرض ثابت (SidebarCollapsedWidth)، مايتجاوبش مع نسبة
            // الشاشة خالص — Resize والشريط مطوي المفروض يفضل مطوي بعرضه
            // الثابت، مش يرجع يتمدد لوحده
            if (_isSidebarCollapsed)
            {
                SidebarBorder.Width = SidebarCollapsedWidth;
                return;
            }

            // SidebarColumn بقى Auto (شوف MainWindow.xaml) — عرض الشريط
            // الفعلي بقى خاصية Width على SidebarBorder نفسه (double عادي،
            // قابل للتحريك بـDoubleAnimation)، مش GridLength بتاع العمود
            SidebarBorder.Width = ExpandedSidebarWidth(logicalWidth);
        }

        private static double ExpandedSidebarWidth(double logicalWidth) =>
            Math.Clamp(logicalWidth * SidebarRatio, SidebarMin, SidebarMax);

        /// <summary>
        /// حالة الطي المحفوظة من آخر تشغيل — بتتطبّق فورًا من غير أي حركة
        /// (الحركة بس لدوسة المستخدم الحية جوّه AnimateSidebarCollapse).
        /// </summary>
        private void ApplyInitialSidebarState()
        {
            _isSidebarCollapsed = AppSettingsStore.Load().SidebarCollapsed;
            IsSidebarCollapsed = _isSidebarCollapsed;
            SidebarToggleIcon.Kind = _isSidebarCollapsed ? PackIconKind.ChevronDoubleLeft : PackIconKind.ChevronDoubleRight;
            SidebarContent.Opacity = _isSidebarCollapsed ? 0 : 1;
            // IsEnabled=false بيوقف الـHit-testing وTab-focus مع بعض على
            // كل المحتوى اللي جواه — Opacity=0 لوحدها بتخفي بصريًا بس
            // تسيب العناصر قابلة للدوسة/التنقل بالكيبورد وهي مش باينة
            SidebarContent.IsEnabled = !_isSidebarCollapsed;
            SidebarToggleButton.ToolTip = _isSidebarCollapsed ? "فتح القائمة الجانبية" : "طي القائمة الجانبية";
            // العرض الفعلي بيتظبط في أول ApplyUiScale (SizeChanged عند فتح
            // النافذة) — مفيش داعي نكرره هنا

            if (!_isSidebarCollapsed && !AppSettingsStore.Load().SidebarToggleHintShown)
                PlaySidebarToggleHintPulse();
        }

        /// <summary>
        /// نبضة بسيطة (تكبير/تصغير خفيف) على زرار الطي أول مرة البرنامج
        /// يتفتح فيها بعد إضافة الميزة — عشان تلفت النظر للزرار الجديد.
        /// بتتعرض مرة واحدة بس (نفس نمط LastSeenTourVersion) ثم تتسجل.
        /// </summary>
        private void PlaySidebarToggleHintPulse()
        {
            var scaleTransform = new ScaleTransform(1, 1);
            SidebarToggleButton.RenderTransformOrigin = new Point(0.5, 0.5);
            SidebarToggleButton.RenderTransform = scaleTransform;

            var pulse = new DoubleAnimation
            {
                From = 1, To = 1.3,
                Duration = TimeSpan.FromMilliseconds(280),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(2),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);

            var settings = AppSettingsStore.Load();
            settings.SidebarToggleHintShown = true;
            AppSettingsStore.Save(settings);
        }

        private void SidebarToggle_Click(object sender, RoutedEventArgs e) => AnimateSidebarCollapse(!_isSidebarCollapsed);

        /// <summary>
        /// Storyboard حقيقي (مش قفزة فورية): العرض بيتحرك من الحالي للهدف،
        /// والمحتوى (SidebarContent، كل حاجة ماعدا زرار الطي نفسه) بيختفي/
        /// يظهر بالتوازي — بيختفي بدري شوية عند الطي (عشان النص ميتقصّش
        /// وهو لسه باين وسط الضغط) وبيتأخر شوية عند الفتح (يظهر بعد ما
        /// المساحة تبقى كافية ليه).
        ///
        /// **بعد Completed**: BeginAnimation(WidthProperty, null) بيشيل
        /// الحركة القديمة عن الخاصية — وإلا ApplySidebarWidth (بينادى تاني
        /// عند أي Resize بعد كده) مش هيقدر يكتب فوق قيمة لسه متحكم فيها من
        /// Storyboard قديم، والشريط هيتجمّد على آخر عرض اتحرك ليه.
        /// </summary>
        private void AnimateSidebarCollapse(bool collapse)
        {
            _isSidebarCollapsed = collapse;
            IsSidebarCollapsed = collapse;
            SidebarToggleIcon.Kind = collapse ? PackIconKind.ChevronDoubleLeft : PackIconKind.ChevronDoubleRight;
            SidebarToggleButton.ToolTip = collapse ? "فتح القائمة الجانبية" : "طي القائمة الجانبية";
            // بيتقفل فورًا وقت الطي (قبل الحركة) عشان محدش يقدر يدوس على
            // عنصر لسه شبه باين وسط التصغير؛ بيترجع يتفتح بعد الفتح كامل
            // (جوّه Completed) عشان ميبقاش قابل للتفاعل قبل ما يكون باين خالص
            if (collapse) SidebarContent.IsEnabled = false;

            var fromWidth = SidebarBorder.ActualWidth > 0 ? SidebarBorder.ActualWidth : SidebarBorder.Width;
            var toWidth = collapse ? SidebarCollapsedWidth : ExpandedSidebarWidth(_lastLogicalWidth);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var widthAnimation = new DoubleAnimation
            {
                From = fromWidth, To = toWidth,
                Duration = TimeSpan.FromMilliseconds(SidebarToggleAnimationMs),
                EasingFunction = ease
            };
            Storyboard.SetTarget(widthAnimation, SidebarBorder);
            Storyboard.SetTargetProperty(widthAnimation, new PropertyPath(WidthProperty));

            var contentOpacity = new DoubleAnimation
            {
                From = collapse ? 1 : 0, To = collapse ? 0 : 1,
                Duration = TimeSpan.FromMilliseconds(collapse ? 140 : 160),
                BeginTime = collapse ? TimeSpan.Zero : TimeSpan.FromMilliseconds(80),
                EasingFunction = ease
            };
            Storyboard.SetTarget(contentOpacity, SidebarContent);
            Storyboard.SetTargetProperty(contentOpacity, new PropertyPath(OpacityProperty));

            var storyboard = new Storyboard();
            storyboard.Children.Add(widthAnimation);
            storyboard.Children.Add(contentOpacity);
            storyboard.Completed += (_, _) =>
            {
                SidebarBorder.BeginAnimation(WidthProperty, null);
                SidebarBorder.Width = toWidth;
                SidebarContent.BeginAnimation(OpacityProperty, null);
                SidebarContent.Opacity = collapse ? 0 : 1;
                if (!collapse) SidebarContent.IsEnabled = true;
            };
            storyboard.Begin();

            var settings = AppSettingsStore.Load();
            settings.SidebarCollapsed = collapse;
            AppSettingsStore.Save(settings);
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
            RefreshSidebarToggleBadge();
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
            RefreshSidebarToggleBadge();
        }

        /// <summary>
        /// نقطة صغيرة على زرار طي الشريط بتبان لو فيه تنبيه معلّق (عمليات
        /// جديدة أو خطط ذاكرة مستحقة) — عشان التنبيه ميضيعش لمجرد إن
        /// المستخدم طاوي الشريط ومابيشوفش ActivityBadge/MemoryBadge نفسهم.
        /// بتتنادى من ذيل RefreshActivityBadge/RefreshMemoryBadge الاتنين.
        /// </summary>
        private void RefreshSidebarToggleBadge()
        {
            SidebarToggleBadgeDot.Visibility =
                ActivityBadge.Visibility == Visibility.Visible || MemoryBadge.Visibility == Visibility.Visible
                    ? Visibility.Visible
                    : Visibility.Collapsed;
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

        private static bool HasArabic(string text) =>
            text.Any(c => c >= '؀' && c <= 'ۿ');

        private void NavHome_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return; // بيحصل مرة واحدة أثناء تهيئة النافذة
            if (ExitSandboxOnRealNavigation()) return;
            MainContent.Content = _session.GetRequiredService<HomeView>();
            RefreshActivityBadge();
            RefreshMemoryBadge();
        }

        private void NavWorkers_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return; // بيحصل مرة واحدة أثناء تهيئة النافذة
            if (ExitSandboxOnRealNavigation()) return;
            MainContent.Content = _session.GetRequiredService<WorkersView>();
            RefreshActivityBadge();
            RefreshMemoryBadge();
        }

        private void NavProducts_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            if (ExitSandboxOnRealNavigation()) return;
            MainContent.Content = _session.GetRequiredService<ProductsView>();
            RefreshActivityBadge();
            RefreshMemoryBadge();
        }

        private void NavDailyEntry_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            if (ExitSandboxOnRealNavigation()) return;
            MainContent.Content = _session.GetRequiredService<DailyEntryView>();
            RefreshActivityBadge();
            RefreshMemoryBadge();
        }

        private void NavEvaluation_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            if (ExitSandboxOnRealNavigation()) return;
            MainContent.Content = _session.GetRequiredService<ReportsView>();
            RefreshActivityBadge();
            RefreshMemoryBadge();
        }

        private void NavReports_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            if (ExitSandboxOnRealNavigation()) return;
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
            if (ExitSandboxOnRealNavigation()) return;
            MainContent.Content = _session.GetRequiredService<MemoryView>();
            RefreshActivityBadge();
            RefreshMemoryBadge();
        }

        private void NavActivityLog_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            if (ExitSandboxOnRealNavigation()) return;
            MainContent.Content = _session.GetRequiredService<ActivityLogView>();
            // فتح الشاشة بيصفّر آخر وقت مشاهدة جوه الـ ViewModel نفسها؛
            // الرجوع هنا بعد شوية (تنقّل تاني) هو اللي بيعرض الصفر فعليًا
            RefreshActivityBadge();
            RefreshMemoryBadge();
        }

        private void NavSettings_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            if (ExitSandboxOnRealNavigation()) return;
            MainContent.Content = _session.GetRequiredService<SettingsView>();
            RefreshActivityBadge();
            RefreshMemoryBadge();
        }

        private void NavDepartmentAccounts_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            if (ExitSandboxOnRealNavigation()) return;
            MainContent.Content = _session.GetRequiredService<DepartmentAccountsView>();
            RefreshActivityBadge();
            RefreshMemoryBadge();
        }

        private void NavHelp_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            if (ExitSandboxOnRealNavigation()) return;
            MainContent.Content = _session.GetRequiredService<HelpView>();
            RefreshActivityBadge();
            RefreshMemoryBadge();
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

            if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                AnimateSidebarCollapse(!_isSidebarCollapsed);
            }
        }
    }
}
