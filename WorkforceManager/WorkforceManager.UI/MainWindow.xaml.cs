using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
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

        public MainWindow(CurrentUserContext currentUser)
        {
            _currentUser = currentUser;

            InitializeComponent();

            // مكان الإشعارات بيتسجّل مرة واحدة هنا — بعدها أي شاشة
            // بتنادي Notify والإشعار بيوصل من غير ما تعرف مين بيعرضه
            Toasts.Register();

            // تاريخ اليوم بالعربي في بطاقة أسفل القائمة الجانبية
            TodayText.Text = DateTime.Today.ToString(
                "dddd d MMMM yyyy", new System.Globalization.CultureInfo("ar-EG"));

            ShowIdentity();
            RefreshActivityBadge();

            // شريط عنوان النافذة بيتلوّن بعد ما الـ Handle يتعمل — قبل
            // كده مفيش نافذة فعلية تتلوّن
            SourceInitialized += (_, _) => WindowChromeColors.Apply(this);

            // الشاشة الافتراضية عند فتح البرنامج: شاشة العمال
            MainContent.Content = App.AppHost.Services.GetRequiredService<WorkersView>();

            // مايتقفلش من غير توقيع نهاية اليوم — شوف MainWindow_Closing
            Closing += MainWindow_Closing;
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
        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            if (!Notify.Ask("تسجيل الخروج من الحساب الحالي؟", "تأكيد")) return;

            _currentUser.SignOut();

            var login = new LoginWindow();
            if (login.ShowDialog() != true)
            {
                Application.Current.Shutdown();
                return;
            }

            ShowIdentity();
            RefreshActivityBadge(); // الحساب الجديد ممكن يكون له عدد مختلف تمامًا

            // شاشة التسجيل اليومي Singleton (عشان رجوعك لها من شاشة تانية
            // ميمسحش رحلة لسه مش محفوظة) — من غيرها هنا، الحساب الجديد
            // كان هيلاقي رحلة الحساب اللي فات لسه واقفة على الشاشة
            App.AppHost.Services.GetRequiredService<ViewModels.DailyEntryViewModel>().ResetForNewSession();

            // الرجوع للشاشة الافتراضية بدل ما يفضل واقف على شاشة ممكن
            // ماعادش مسموح للحساب الجديد يشوفها (زي الحسابات الإدارية)
            NavWorkersItem.IsChecked = true;
            MainContent.Content = App.AppHost.Services.GetRequiredService<WorkersView>();
        }

        private static bool HasArabic(string text) =>
            text.Any(c => c >= '؀' && c <= 'ۿ');

        private void NavWorkers_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return; // بيحصل مرة واحدة أثناء تهيئة النافذة
            MainContent.Content = App.AppHost.Services.GetRequiredService<WorkersView>();
            RefreshActivityBadge();
        }

        private void NavProducts_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = App.AppHost.Services.GetRequiredService<ProductsView>();
            RefreshActivityBadge();
        }

        private void NavDailyEntry_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = App.AppHost.Services.GetRequiredService<DailyEntryView>();
            RefreshActivityBadge();
        }

        private void NavEvaluation_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = App.AppHost.Services.GetRequiredService<ReportsView>();
            RefreshActivityBadge();
        }

        private void NavReports_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = App.AppHost.Services.GetRequiredService<ReportBuilderView>();
            RefreshActivityBadge();
        }

        /// <summary>
        /// بيفتح شاشة الإنتاج اليومي على خطة ذاكرة بترتيب مراحلها.
        ///
        /// بيعلّم عنصر التنقل كمان — من غير كده الشاشة بتتغيّر والشريط
        /// الجانبي فاضل مأشّر على مكان تاني، فالمستخدم مش عارف هو فين.
        /// </summary>
        public async Task OpenDailyEntryForMemoryAsync(int productId, IReadOnlyList<int> stageOrder)
        {
            if (MainContent is null) return;

            var view = App.AppHost.Services.GetRequiredService<DailyEntryView>();
            MainContent.Content = view;
            NavDailyEntryItem.IsChecked = true;
            RefreshActivityBadge();

            await App.AppHost.Services.GetRequiredService<ViewModels.DailyEntryViewModel>()
                .StartFromMemoryAsync(productId, stageOrder);
        }

        private void NavMemory_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = App.AppHost.Services.GetRequiredService<MemoryView>();
            RefreshActivityBadge();
        }

        private void NavActivityLog_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = App.AppHost.Services.GetRequiredService<ActivityLogView>();
            // فتح الشاشة بيصفّر آخر وقت مشاهدة جوه الـ ViewModel نفسها؛
            // الرجوع هنا بعد شوية (تنقّل تاني) هو اللي بيعرض الصفر فعليًا
            RefreshActivityBadge();
        }

        private void NavSettings_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = App.AppHost.Services.GetRequiredService<SettingsView>();
            RefreshActivityBadge();
        }

        private void NavDepartmentAccounts_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = App.AppHost.Services.GetRequiredService<DepartmentAccountsView>();
            RefreshActivityBadge();
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
                    if (await RunFinalSaveFlowAsync(fromCloseAttempt: true))
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
        /// → توقيع. مشترك بين زرار القائمة الجانبية ومعالج الإغلاق عشان
        /// الاتنين يمشوا بنفس المسار بالظبط ونفس الرسايل.
        /// </summary>
        /// <param name="fromCloseAttempt">
        /// جاي من محاولة إغلاق مش من الزرار — بيخلي البرنامج يوضّح
        /// للمستخدم ليه النافذة ما اتقفلتش، بدل ما ديالوج يظهر فجأة.
        /// </param>
        /// <returns>النهارده بقى مغطّى بتوقيع (سواء دلوقتي أو من قبل)؟</returns>
        private async Task<bool> RunFinalSaveFlowAsync(bool fromCloseAttempt = false)
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

            if (fromCloseAttempt)
                Notify.Warn(
                    "فيه شغل النهارده لسه ما اتوقّعش عليه. لازم \"حفظ نهائي\" الأول قبل ما تقفل البرنامج.",
                    "مش هينفع تقفل");

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
