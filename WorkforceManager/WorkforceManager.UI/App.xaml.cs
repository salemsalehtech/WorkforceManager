using System.IO;
using System.Windows;
using MaterialDesignThemes.Wpf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using WorkforceManager.Data.Repositories;

namespace WorkforceManager.UI
{
    public partial class App : Application
    {
        /// <summary>
        /// مضيف التطبيق (Host) اللي بيدير كل الاعتماديات (Dependency Injection).
        /// ده اللي بيخلي كل طبقة (UI / Business / Data) منفصلة ومربوطة ببعض
        /// بشكل نظيف من غير ما أي طبقة تعمل "new" لطبقة تانية يدويًا.
        /// </summary>
        public static IHost AppHost { get; private set; } = null!;

        public App()
        {
            AppCulture.Pin();

            AppHost = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // مسار قاعدة البيانات: مجلد بيانات التطبيق الخاص بالمستخدم (مش داخل مجلد البرنامج نفسه)
                    Directory.CreateDirectory(AppPaths.DataFolder);

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseSqlite($"Data Source={AppPaths.DbPath}"));

                    // Repositories
                    services.AddScoped<IWorkerRepository, WorkerRepository>();
                    services.AddScoped<IProductRepository, ProductRepository>();
                    services.AddScoped<IDailyProductionRepository, DailyProductionRepository>();
                    services.AddScoped<IAttendanceRepository, AttendanceRepository>();
                    services.AddScoped<IPenaltyRepository, PenaltyRepository>();
                    services.AddScoped<IHourlyWorkLogRepository, HourlyWorkLogRepository>();
                    services.AddScoped<IWageAdjustmentRepository, WageAdjustmentRepository>();
                    services.AddScoped<IProductionDayClosureRepository, ProductionDayClosureRepository>();
                    services.AddScoped<IActivityEventRepository, ActivityEventRepository>();
                    services.AddScoped<IWorkerSkillRepository, WorkerSkillRepository>();
                    services.AddScoped<IGenericRepository<OperationsCredential>, GenericRepository<OperationsCredential>>();
                    services.AddScoped<IGenericRepository<ProductionStage>, GenericRepository<ProductionStage>>();
                    services.AddScoped<IGenericRepository<WorkerSkill>, GenericRepository<WorkerSkill>>();
                    services.AddScoped<IGenericRepository<AppUser>, GenericRepository<AppUser>>();
                    services.AddScoped<IGenericRepository<WorkerPerformanceTitle>, GenericRepository<WorkerPerformanceTitle>>();
                    // معاملات الكتابة (نفس الـ DbContext بتاع الـ Scope) — للتحقق الذري قبل الحفظ
                    services.AddScoped<IUnitOfWork, EfUnitOfWork>();

                    // Business Services
                    services.AddScoped<WorkerAssignmentGuard>();
                    services.AddScoped<WorkdayCalculationService>();
                    services.AddScoped<AttendanceAutomationService>();
                    services.AddScoped<AttendanceService>();
                    services.AddScoped<PenaltyService>();
                    services.AddScoped<WeeklySummaryService>();
                    services.AddScoped<WorkerManagementService>();
                    services.AddScoped<ProductManagementService>();
                    services.AddScoped<ProductionFlowService>();
                    services.AddScoped<DayClosureService>();
                    services.AddScoped<DailyProductionReportService>();
                    services.AddScoped<ProductionChartService>();
                    services.AddScoped<ProductActivityService>();
                    services.AddScoped<PendingWorkService>();
                    services.AddScoped<ScrapService>();
                    services.AddScoped<ProductionStageOutputService>();
                    services.AddScoped<HourlyWorkdayService>();
                    services.AddScoped<PayrollService>();
                    services.AddScoped<ProductionReportService>();
                    services.AddScoped<WageAdjustmentService>();
                    services.AddScoped<ReportBuilderService>();
                    services.AddScoped<DepartmentAttendanceService>();
                    services.AddScoped<WorkerRecognitionService>();
                    services.AddScoped<ProductionTrendService>();
                    // الهوية المشتركة: مصدر واحد لـ"مين عمل كده" — الحذف الناعم
                    // وسجل العمليات الاتنين بيقروا منه
                    services.AddSingleton<CurrentUserContext>();
                    services.AddScoped<OperationsPasswordService>();
                    services.AddScoped<ActivityLogService>();
                    services.AddScoped<SoftDeleteService>();
                    services.AddScoped<DeletionScopeService>();
                    services.AddScoped<SkillRatingService>();
                    services.AddScoped<AuthService>();
                    // خدمة التصدير Singleton لأنها بدون حالة ولا بتلمس قاعدة البيانات
                    services.AddSingleton<ReportTableExcelService>();
                    services.AddSingleton<PayslipStripExcelService>();

                    // Windows / Views
                    services.AddSingleton<MainWindow>();
                    // الشاشات الداخلية Transient: نسخة جديدة نظيفة مع كل تنقّل
                    services.AddTransient<Views.WorkersView>();
                    services.AddTransient<ViewModels.WorkersViewModel>();
                    services.AddTransient<Views.DailyEntryView>();
                    services.AddTransient<ViewModels.DailyEntryViewModel>();
                    services.AddTransient<Views.ReportsView>();
                    services.AddTransient<ViewModels.ReportsViewModel>();
                    services.AddTransient<Views.ReportBuilderView>();
                    services.AddTransient<ViewModels.ReportBuilderViewModel>();
                    services.AddTransient<Views.ProductsView>();
                    services.AddTransient<ViewModels.ProductsViewModel>();
                    services.AddTransient<Views.ActivityLogView>();
                    services.AddTransient<ViewModels.ActivityLogViewModel>();
                    services.AddTransient<Views.SettingsView>();
                    services.AddTransient<ViewModels.SettingsViewModel>();
                    services.AddTransient<Views.DepartmentAccountsView>();
                    services.AddTransient<ViewModels.DepartmentAccountsViewModel>();
                })
                .Build();
        }

        /// <summary>
        /// قفل النسخة الواحدة: بيمنع فتح نسختين من البرنامج في نفس الوقت —
        /// نسختين بيكتبوا على نفس قاعدة الـ SQLite بيعرّضوا البيانات للتعارض
        /// والنسخ الاحتياطي للخبطة. بيفضل ممسوك طول عمر البرنامج.
        /// </summary>
        private static Mutex? _singleInstanceMutex;

        /// <summary>
        /// بيتأكد إن مجلد البيانات موجود وينفع الكتابة فيه **قبل** ما SQLite
        /// يلمسه.
        ///
        /// البيانات بقت في %ProgramData%، وصلاحية الكتابة عليه بتتحدد من ملف
        /// التثبيت. لو البرنامج اتنسخ يدوي من غير تثبيت، أو حد غيّر صلاحيات
        /// المجلد، SQLite بيقع بـ "unable to open database file" — رسالة
        /// مالهاش أي معنى للمستخدم ولا بتقوله يعمل إيه. الفحص ده بيقول
        /// المشكلة والمكان والعلاج.
        ///
        /// الكتابة الفعلية هي الاختبار الوحيد المعتبر: وجود المجلد أو قراءة
        /// صلاحياته مش بيقولوا إن الكتابة هتنفع.
        /// </summary>
        private static bool EnsureDataFolderWritable()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataFolder);

                var probe = Path.Combine(AppPaths.DataFolder, ".write-probe");
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
                return true;
            }
            catch (Exception ex)
            {
                Notify.Error(
                    "البرنامج مش قادر يكتب في مجلد البيانات:\n\n" +
                    $"{AppPaths.DataFolder}\n\n" +
                    $"({ex.Message})\n\n" +
                    "شغّل ملف التثبيت تاني، أو ادّي المجلد ده صلاحية تعديل للمستخدمين.",
                    "مشكلة في صلاحيات مجلد البيانات");
                return false;
            }
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            // منع تشغيل نسخة تانية من البرنامج (النسخة الأولى بتفضل هي الشغالة)
            _singleInstanceMutex = new Mutex(true, @"Local\WorkforceManager_SingleInstance", out var isFirstInstance);
            if (!isFirstInstance)
            {
                Notify.Info("البرنامج مفتوح بالفعل — استخدم النافذة المفتوحة.\n(فتح نسختين في نفس الوقت ممكن يبوّظ البيانات)", "البرنامج شغال");
                Shutdown();
                return;
            }

            // معالج أخطاء عام: أي استثناء غير متوقع يوصل لخيط الواجهة بيظهر
            // للمستخدم برسالة واضحة بدل ما البرنامج يقفل فجأة من غير سبب مفهوم
            DispatcherUnhandledException += (_, args) =>
            {
                Notify.Warn($"حصل خطأ غير متوقع:\n\n{args.Exception.Message}", "خطأ");
                args.Handled = true; // منع إغلاق البرنامج بسبب الخطأ
            };

            // قبل أي حاجة تلمس القرص — لو المجلد مش قابل للكتابة نقول السبب
            // والمكان بدل ما SQLite يقع بعطل مالهوش معنى للمستخدم
            if (!EnsureDataFolderWritable())
            {
                Shutdown();
                return;
            }

            // المظهر بيتحدد قبل ما أي شاشة تتبني: الشاشات بتقرا الألوان
            // بـ StaticResource اللي بتتحل مرة واحدة وقت التحميل، فالتبديل
            // لازم يحصل هنا مش بعدين
            ApplyTheme(AppSettingsStore.Load().DarkMode);

            try
            {
                await AppHost.StartAsync();

                using (var scope = AppHost.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    // نسخة احتياطية يومية قبل أي تعديل على قاعدة البيانات، عشان لو
                    // الـ Migration فشل لأي سبب تفضل عندنا نسخة سليمة من قبل التعديل —
                    // محليًا + خارجيًا لو المستخدم مفعّل مجلد خارجي من الإعدادات
                    var settings = AppSettingsStore.Load();

                    // المستخدم يقدر يقفل النسخة التلقائية من الإعدادات —
                    // قراره، وهو اللي بيتحمّل نتيجته
                    if (settings.AutoBackupOnStartup)
                        DatabaseBackupService.RunDailyBackup(
                            AppPaths.DbPath, settings.ExternalBackupFolder, settings.BackupRetentionDays);

                    // تطبيق أي Migration جديدة تلقائيًا (بيُنشئ قاعدة البيانات من الصفر
                    // لو مش موجودة أصلاً) — بديل EnsureCreatedAsync عشان تحديثات
                    // النماذج المستقبلية تتطبق على قاعدة بيانات العميل الحالية من
                    // غير ما نمسح بياناته
                    await db.Database.MigrateAsync();
                    await DatabaseSeeder.SeedIfEmptyAsync(db);

                    // أول تشغيل: إنشاء حساب الدخول الافتراضي لو مفيش مستخدمين
                    await scope.ServiceProvider.GetRequiredService<AuthService>().EnsureDefaultUserAsync();

                    // لازم بعد EnsureDefaultUserAsync مباشرة: أول حساب دخول
                    // (admin الافتراضي على تركيب جديد، أو أقدم حساب على قاعدة
                    // عميل قديمة) بيتحوّل لأول "مدير قسم" في الحسابات الإدارية
                    await DatabaseSeeder.SeedDefaultDepartmentManagerAsync(db);
                }

                // شاشة الدخول الأول — من غير دخول ناجح البرنامج مش بيفتح.
                // أثناء شاشة الدخول بنمنع الإغلاق التلقائي (مفيش نافذة رئيسية لسه)
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var login = new Views.LoginWindow();
                if (login.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }

                // تنظيف سجل العمليات من اللي عدّى مدة الاحتفاظ.
                //
                // بعد النسخة الاحتياطية عن قصد (اللي فوق): أي حدث بيتمسح
                // هنا لسه موجود في نسخة النهارده، فالغلط بيرجع.
                //
                // فشله عمره ما يمنع البرنامج من الفتح — ده تنظيف، مش شرط
                // تشغيل. نفس مبدأ النسخ الخارجي.
                try
                {
                    using var purgeScope = AppHost.Services.CreateScope();
                    var settings = AppSettingsStore.Load();
                    await purgeScope.ServiceProvider.GetRequiredService<ActivityLogService>()
                        .PurgeExpiredAsync(
                            settings.ActivityLogRetentionDays,
                            settings.ActivityLogFinancialRetentionDays);

                    // الصفوف اللي اتشالت أيام ما الحذف كان بيعلّم بس —
                    // بتتنضّف بنفس قاعدة الحذف الحالية
                    await DeletedRowsCleaner.PurgeAsync(
                        purgeScope.ServiceProvider.GetRequiredService<AppDbContext>());
                }
                catch
                {
                    // متجاهَل عن قصد — شوف الكومنت فوق
                }

                // تعبية حضور "حاضر" التلقائي للحسابات الإدارية (مدير/رئيس
                // قسم) — البرنامج مالوش نظام مهام مجدولة، فده مكانها
                // الوحيد. فشلها عمره ما يمنع البرنامج من الفتح، زي التنظيف فوق.
                try
                {
                    using var deptScope = AppHost.Services.CreateScope();
                    await deptScope.ServiceProvider.GetRequiredService<DepartmentAttendanceService>()
                        .EnsureDailyPresenceAsync();
                }
                catch
                {
                    // متجاهَل عن قصد — شوف الكومنت فوق
                }

                // تسجيل ألقاب "أحسن عامل" الأسبوعية/الشهرية اللي فترتها
                // قفلت من آخر تشغيل — نفس مبدأ التعبية فوق (مفيش نظام مهام
                // مجدولة، فبدء التشغيل هو نقطة اللحاق الوحيدة)
                try
                {
                    using var recognitionScope = AppHost.Services.CreateScope();
                    await recognitionScope.ServiceProvider.GetRequiredService<WorkerRecognitionService>()
                        .AwardTitlesForClosedPeriodsAsync();
                }
                catch
                {
                    // متجاهَل عن قصد — شوف الكومنت فوق
                }

                var mainWindow = AppHost.Services.GetRequiredService<MainWindow>();
                MainWindow = mainWindow;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                mainWindow.Show();

                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                // فشل بدء التشغيل (قاعدة بيانات مقفولة/تالفة، مساحة قرص،
                // خطأ في بناء الواجهة...): نعرض السبب ونقفل بأمان بدل ما
                // البرنامج يختفي من غير رسالة.
                //
                // **Notify.Error مش Warn**: ده فشل، والفشل لازم يوقف
                // ويستنى — والإشعار الطاير بيروح لوحده.
                var logPath = WriteCrashLog(ex);

                Notify.Error(
                    $"تعذّر بدء تشغيل البرنامج:\n\n{ex.Message}\n\n" +
                    $"التفاصيل الكاملة اتكتبت في:\n{logPath}",
                    "خطأ في بدء التشغيل");

                Shutdown(-1);
            }
        }

        /// <summary>
        /// بيدمج ملف الوضع الليلي فوق الألوان الفاتحة، أو بيشيله.
        ///
        /// الدمج بيدوس على المفاتيح اللي في App.xaml بنفس الأسماء — يعني
        /// أي شاشة بتقرا BrandBrush بتاخد النسخة الداكنة من غير ما تعرف
        /// إن فيه وضع تاني أصلاً.
        /// </summary>
        /// <summary>
        /// بيبدّل لوحة الألوان **في اللحظة** من غير إعادة تشغيل.
        ///
        /// بيشتغل لأن كل الأنماط الجديدة بتشاور على الألوان بـ
        /// DynamicResource: الربط بيفضل حيّ، فتبديل الملف بيتنقل
        /// للشاشات المفتوحة فورًا. كان محتاج إعادة تشغيل أيام ما كانت
        /// StaticResource (بتتحل مرة واحدة وقت تحميل الشاشة).
        ///
        /// التبديل في **مكان اللوحة القديمة** مش إضافة فوقها: الإضافة
        /// كانت بتخلي الملفين محمّلين والذاكرة تكبر مع كل تبديل.
        /// </summary>
        public static void ApplyTheme(bool darkMode)
        {
            var app = Current;
            if (app is null) return;

            ApplyPalette(app, darkMode);
            ApplyMaterialDesignBaseTheme(darkMode);

            // شريط عنوان النافذة برّه WPF، فمفيش Brush بيوصله — لازم
            // يتلوّن بنداء صريح مع كل تبديل ثيم، وإلا بيفضل أبيض فوق
            // برنامج كله أسود
            foreach (Window window in app.Windows)
                WindowChromeColors.Apply(window);
        }

        /// <summary>لوحة ألوان الهوية — الملف بيتبدّل في مكانه.</summary>
        private static void ApplyPalette(Application app, bool darkMode)
        {
            var dictionaries = app.Resources.MergedDictionaries;

            var index = -1;
            for (var i = 0; i < dictionaries.Count; i++)
                if (dictionaries[i].Source?.OriginalString.Contains("Palette.") == true)
                {
                    index = i;
                    break;
                }

            if (index < 0) return; // اللوحة مش موجودة — مفيش حاجة تتبدّل

            var wanted = darkMode ? "Themes/Palette.Dark.xaml" : "Themes/Palette.Light.xaml";
            if (dictionaries[index].Source?.OriginalString == wanted) return;

            dictionaries[index] = new ResourceDictionary { Source = new Uri(wanted, UriKind.Relative) };
        }

        /// <summary>
        /// بيخلي ثيم MaterialDesign يمشي مع ثيم البرنامج.
        ///
        /// من غير دي البرنامج بيفضل على BaseTheme="Light" المكتوب في
        /// App.xaml مهما اتغيّرت لوحتنا — و**دي كانت أكبر مشكلة في
        /// الوضع الليلي**: 22 ComboBox و39 DataGrid و10 DatePicker
        /// بيرسموا نفسهم من ثيم المكتبة مش من لوحتنا، فكانوا بيطلعوا
        /// بنص غامق على سطح غامق وقوايم منسدلة بيضا. لوحة الألوان
        /// لوحدها عمرها ما كانت هتوصلهم.
        ///
        /// الفشل هنا مش سبب لإيقاف تبديل الثيم: أسوأ حاجة إن عناصر
        /// المكتبة تفضل بالمظهر القديم، والباقي بيتبدّل عادي.
        /// </summary>
        private static void ApplyMaterialDesignBaseTheme(bool darkMode)
        {
            try
            {
                var helper = new PaletteHelper();
                var theme = helper.GetTheme();

                theme.SetBaseTheme(darkMode ? BaseTheme.Dark : BaseTheme.Light);
                helper.SetTheme(theme);
            }
            catch
            {
                // تغيّر في واجهة المكتبة ميوقفش تبديل الثيم
            }
        }

        /// <summary>
        /// بيكتب تفاصيل العطل الكاملة في ملف جنب قاعدة البيانات.
        ///
        /// رسالة الاستثناء لوحدها مبتكفيش: العطل اللي بيحصل وقت بناء
        /// الواجهة رسالته عامة، والسبب الحقيقي بيبقى في
        /// InnerException — وأحيانًا في التالت. الملف بيكتبهم كلهم مع
        /// مكان العطل بالظبط، فبدل "البرنامج مش بيفتح" يبقى فيه سطر
        /// بيقول العطل فين.
        /// </summary>
        private static string WriteCrashLog(Exception ex)
        {
            var path = Path.Combine(AppPaths.DataFolder, "crash.txt");

            try
            {
                var text = new System.Text.StringBuilder();
                text.AppendLine($"وقت العطل: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                text.AppendLine();

                for (var current = ex; current is not null; current = current.InnerException)
                {
                    text.AppendLine($"[{current.GetType().Name}] {current.Message}");
                    text.AppendLine(current.StackTrace);
                    text.AppendLine(new string('-', 60));
                }

                File.WriteAllText(path, text.ToString());
            }
            catch
            {
                // فشل كتابة السجل عمره ما يحجب رسالة العطل الأصلية
            }

            return path;
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            await AppHost.StopAsync();
            AppHost.Dispose();
            base.OnExit(e);
        }
    }
}
