using System.IO;
using System.Windows;
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
                    // معاملات الكتابة (نفس الـ DbContext بتاع الـ Scope) — للتحقق الذري قبل الحفظ
                    services.AddScoped<IUnitOfWork, EfUnitOfWork>();

                    // Business Services
                    services.AddScoped<WorkerAssignmentGuard>();
                    services.AddScoped<WorkdayCalculationService>();
                    services.AddScoped<PerformanceEvaluationService>();
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
                    services.AddScoped<HourlyWorkdayService>();
                    services.AddScoped<PayrollService>();
                    services.AddScoped<ProductionReportService>();
                    services.AddScoped<WageAdjustmentService>();
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
                    services.AddSingleton<WeeklyReportExcelService>();

                    // Windows / Views
                    services.AddSingleton<MainWindow>();
                    // الشاشات الداخلية Transient: نسخة جديدة نظيفة مع كل تنقّل
                    services.AddTransient<Views.WorkersView>();
                    services.AddTransient<ViewModels.WorkersViewModel>();
                    services.AddTransient<Views.DailyEntryView>();
                    services.AddTransient<ViewModels.DailyEntryViewModel>();
                    services.AddTransient<Views.ReportsView>();
                    services.AddTransient<ViewModels.ReportsViewModel>();
                    services.AddTransient<Views.ProductsView>();
                    services.AddTransient<ViewModels.ProductsViewModel>();
                    services.AddTransient<Views.ActivityLogView>();
                    services.AddTransient<ViewModels.ActivityLogViewModel>();
                    services.AddTransient<Views.SettingsView>();
                    services.AddTransient<ViewModels.SettingsViewModel>();
                })
                .Build();
        }

        /// <summary>
        /// قفل النسخة الواحدة: بيمنع فتح نسختين من البرنامج في نفس الوقت —
        /// نسختين بيكتبوا على نفس قاعدة الـ SQLite بيعرّضوا البيانات للتعارض
        /// والنسخ الاحتياطي للخبطة. بيفضل ممسوك طول عمر البرنامج.
        /// </summary>
        private static Mutex? _singleInstanceMutex;

        protected override async void OnStartup(StartupEventArgs e)
        {
            // منع تشغيل نسخة تانية من البرنامج (النسخة الأولى بتفضل هي الشغالة)
            _singleInstanceMutex = new Mutex(true, @"Local\WorkforceManager_SingleInstance", out var isFirstInstance);
            if (!isFirstInstance)
            {
                MessageBox.Show(
                    "البرنامج مفتوح بالفعل — استخدم النافذة المفتوحة.\n(فتح نسختين في نفس الوقت ممكن يبوّظ البيانات)",
                    "البرنامج شغال", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // معالج أخطاء عام: أي استثناء غير متوقع يوصل لخيط الواجهة بيظهر
            // للمستخدم برسالة واضحة بدل ما البرنامج يقفل فجأة من غير سبب مفهوم
            DispatcherUnhandledException += (_, args) =>
            {
                MessageBox.Show(
                    $"حصل خطأ غير متوقع:\n\n{args.Exception.Message}",
                    "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true; // منع إغلاق البرنامج بسبب الخطأ
            };

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

                var mainWindow = AppHost.Services.GetRequiredService<MainWindow>();
                MainWindow = mainWindow;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                mainWindow.Show();

                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                // فشل بدء التشغيل (قاعدة بيانات مقفولة/تالفة، مساحة قرص، ...):
                // نعرض السبب بوضوح ونقفل بأمان بدل ما البرنامج يختفي من غير رسالة
                MessageBox.Show(
                    $"تعذّر بدء تشغيل البرنامج:\n\n{ex.Message}\n\n" +
                    "لو المشكلة مستمرة، فيه نسخة احتياطية من البيانات في مجلد Backups.",
                    "خطأ في بدء التشغيل", MessageBoxButton.OK, MessageBoxImage.Error);
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
        private void ApplyTheme(bool darkMode)
        {
            var existing = Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source?.OriginalString.Contains("Dark.xaml") == true);

            if (!darkMode)
            {
                if (existing is not null) Resources.MergedDictionaries.Remove(existing);
                return;
            }

            if (existing is not null) return; // متطبّق خلاص

            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("Themes/Dark.xaml", UriKind.Relative)
            });
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            await AppHost.StopAsync();
            AppHost.Dispose();
            base.OnExit(e);
        }
    }
}
