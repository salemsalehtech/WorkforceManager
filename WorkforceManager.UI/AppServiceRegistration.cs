using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using WorkforceManager.Data.Repositories;

namespace WorkforceManager.UI
{
    /// <summary>
    /// تسجيلات DI الحقيقية للبرنامج (Repositories + Services + DbContext +
    /// Views/ViewModels) في مكان واحد. <see cref="App"/> (القاعدة الحقيقية)
    /// و<see cref="Sandbox.SandboxSession"/> (وضع التجربة التفاعلي) بينادوا
    /// نفس الدالة دي بالظبط، فرق بينهم اتصال قاعدة البيانات بس — عشان
    /// مايبقاش فيه نسخة تالتة يدوية من القايمة دي تنجرف عن الحقيقية من غير
    /// ما حد ياخد باله (زي ما حصل فعلًا بين App.xaml.cs وTestDatabase.cs
    /// قبل كده — القايمة هناك عن قصد منفصلة، برّه نطاق التغيير ده).
    /// </summary>
    internal static class AppServiceRegistration
    {
        internal static IServiceCollection AddWorkforceManagerCore(
            this IServiceCollection services, string sqliteConnectionString)
        {
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite(sqliteConnectionString));

            // Repositories
            services.AddScoped<IWorkerRepository, WorkerRepository>();
            services.AddScoped<IProductRepository, ProductRepository>();
            services.AddScoped<IDailyProductionRepository, DailyProductionRepository>();
            services.AddScoped<IAttendanceRepository, AttendanceRepository>();
            services.AddScoped<IPenaltyRepository, PenaltyRepository>();
            services.AddScoped<IHourlyWorkLogRepository, HourlyWorkLogRepository>();
            services.AddScoped<IWageAdjustmentRepository, WageAdjustmentRepository>();
            services.AddScoped<IDailyOperationsSignOffRepository, DailyOperationsSignOffRepository>();
            services.AddScoped<IProductionMemoryRepository, ProductionMemoryRepository>();
            services.AddScoped<IActivityEventRepository, ActivityEventRepository>();
            services.AddScoped<IWorkerSkillRepository, WorkerSkillRepository>();
            services.AddScoped<IGenericRepository<OperationsCredential>, GenericRepository<OperationsCredential>>();
            services.AddScoped<IGenericRepository<ProductionStage>, GenericRepository<ProductionStage>>();
            services.AddScoped<IGenericRepository<WorkerSkill>, GenericRepository<WorkerSkill>>();
            services.AddScoped<IGenericRepository<AppUser>, GenericRepository<AppUser>>();
            services.AddScoped<IGenericRepository<WorkerPerformanceTitle>, GenericRepository<WorkerPerformanceTitle>>();
            services.AddScoped<IGenericRepository<InitialBalance>, GenericRepository<InitialBalance>>();
            services.AddScoped<IGenericRepository<InitialBalanceRange>, GenericRepository<InitialBalanceRange>>();
            services.AddScoped<IGenericRepository<InitialBalanceUsage>, GenericRepository<InitialBalanceUsage>>();
            services.AddScoped<IInitialBalanceRepository, InitialBalanceRepository>();
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
            services.AddScoped<DailyOperationsSignOffService>();
            services.AddScoped<ProductionMemoryService>();
            services.AddScoped<DailyProductionReportService>();
            services.AddScoped<ProductionChartService>();
            services.AddScoped<ProductActivityService>();
            services.AddScoped<PendingWorkService>();
            services.AddScoped<ScrapService>();
            services.AddScoped<ProductionStageOutputService>();
            services.AddScoped<InitialBalanceService>();
            services.AddScoped<HistoricalPendingMigrationService>();
            services.AddScoped<HourlyWorkdayService>();
            services.AddScoped<PayrollService>();
            services.AddScoped<ProductionReportService>();
            services.AddScoped<WageAdjustmentService>();
            services.AddScoped<ReportBuilderService>();
            services.AddScoped<DepartmentAttendanceService>();
            services.AddScoped<WorkerRecognitionService>();
            services.AddScoped<ProductionTrendService>();
            services.AddScoped<GlobalSearchService>();
            services.AddScoped<SearchIntentService>();
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
            // **Scoped مش Singleton**: كل تسجيل دخول بيعمل نطاق
            // جديد (شوف StartSession)، فالنافذة وكل اللي جوّاها
            // بيتولدوا من أول وجديد للحساب الداخل — والخروج
            // بيتخلص منهم كلهم بـ Dispose واحد بدل تنظيف يدوي
            // بند بند كان بينسى حاجة مع كل شاشة جديدة تتضاف.
            // جوّه الجلسة الواحدة Scoped بتتصرف زي Singleton
            // بالظبط، فسلوك التنقّل الموثّق تحت مابيتغيرش.
            services.AddScoped<MainWindow>();
            // الشاشات الداخلية Transient: نسخة جديدة نظيفة مع كل تنقّل
            services.AddTransient<Views.WorkersView>();
            services.AddTransient<ViewModels.WorkersViewModel>();
            // التسجيل اليومي وحدها Scoped مش Transient: فيها توزيع
            // عمال على مراحل لسه مش محفوظ، ونسخة جديدة كل تنقّل
            // كانت بتمسحه لو المستخدم راح لشاشة تانية ورجع من غير
            // حفظ. الحمل الفعلي (استعلامات قاعدة البيانات) لسه
            // بيحصل مرة واحدة بس (شوف DailyEntryViewModel._initialized)
            // — الفرق إن الرجوع للشاشة بقى مبيعيدش بناءها من الصفر.
            //
            // كانت Singleton؛ Scoped بتدّي نفس الضمانة بالظبط جوّه
            // الجلسة، وبتضيف إن الحساب الجديد بعد الخروج بيلاقي
            // شاشة نضيفة من غير ما حد يفتكر يصفّرها بإيده.
            services.AddScoped<Views.DailyEntryView>();
            services.AddScoped<ViewModels.DailyEntryViewModel>();
            services.AddTransient<Views.ReportsView>();
            services.AddTransient<ViewModels.ReportsViewModel>();
            services.AddTransient<Views.ReportBuilderView>();
            services.AddTransient<ViewModels.ReportBuilderViewModel>();
            services.AddTransient<Views.ProductsView>();
            services.AddTransient<ViewModels.ProductsViewModel>();
            services.AddTransient<Views.MemoryView>();
            services.AddTransient<ViewModels.MemoryViewModel>();
            services.AddTransient<Views.ActivityLogView>();
            services.AddTransient<ViewModels.ActivityLogViewModel>();
            services.AddTransient<Views.SettingsView>();
            services.AddTransient<ViewModels.SettingsViewModel>();
            services.AddTransient<Views.DepartmentAccountsView>();
            services.AddTransient<ViewModels.DepartmentAccountsViewModel>();
            services.AddTransient<Views.HelpView>();
            services.AddTransient<ViewModels.HelpViewModel>();

            return services;
        }
    }
}
