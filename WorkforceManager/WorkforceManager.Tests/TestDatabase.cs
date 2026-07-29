using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using WorkforceManager.Data.Repositories;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// قاعدة بيانات SQLite حقيقية في ملف مؤقت لكل اختبار.
    ///
    /// ملف حقيقي مش قاعدة داخل الذاكرة عن قصد: اختبار التزامن بيحتاج
    /// قفل الكتابة الحقيقي بتاع SQLite (BEGIN IMMEDIATE) بين اتصالين
    /// منفصلين — وده مبيتحققش مع مزوّد InMemory (اللي أصلاً مش بيدعم
    /// المعاملات) ولا مع قاعدة الذاكرة المشتركة.
    /// </summary>
    public sealed class TestDatabase : IDisposable
    {
        private readonly string _dbPath;
        private readonly ServiceProvider _provider;

        // ------- معرّفات البيانات المزروعة (ثابتة عشان الاختبارات تبقى مقروءة) -------
        public const int WorkerAhmedId = 1;
        public const int WorkerSaidId = 2;

        /// <summary>عاملة بالساعة (دور رص) — مالهاش مهارات ولا إنتاج على مراحل</summary>
        public const int WorkerMonaHourlyId = 3;

        public const int ProductRingId = 1;   // "دبلة" — مرحلتين
        public const int ProductChainId = 2;  // "سلسلة" — مرحلة واحدة

        public const int RingStage1Id = 1;    // دبلة / تشكيل
        public const int RingStage2Id = 2;    // دبلة / تلميع
        public const int ChainStage1Id = 3;   // سلسلة / لحام

        public TestDatabase()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"wfm-test-{Guid.NewGuid():N}.db");

            var services = new ServiceCollection();

            // نفس تسجيلات App.xaml.cs — لو اتغيرت هناك ومااتغيرتش هنا الاختبارات بتقع،
            // وده مقصود (بيمسك أي خدمة جديدة اتنسيت في الـ DI)
            services.AddDbContext<AppDbContext>(options =>
                // Default Timeout: لو اتصال تاني ماسك قفل الكتابة، استنى بدل ما ترمي فورًا
                options.UseSqlite($"Data Source={_dbPath};Default Timeout=30"));

            services.AddScoped<IWorkerRepository, WorkerRepository>();
            services.AddScoped<IProductRepository, ProductRepository>();
            services.AddScoped<IDailyProductionRepository, DailyProductionRepository>();
            services.AddScoped<IAttendanceRepository, AttendanceRepository>();
            services.AddScoped<IPenaltyRepository, PenaltyRepository>();
            services.AddScoped<IHourlyWorkLogRepository, HourlyWorkLogRepository>();
            services.AddScoped<IGenericRepository<ProductionStage>, GenericRepository<ProductionStage>>();
            services.AddScoped<IUnitOfWork, EfUnitOfWork>();

            services.AddScoped<WorkerAssignmentGuard>();
            services.AddScoped<ProductionFlowService>();
            services.AddScoped<WorkdayCalculationService>();
            services.AddScoped<PenaltyService>();
            services.AddScoped<AttendanceAutomationService>();
            services.AddScoped<AttendanceService>();
            services.AddScoped<HourlyWorkdayService>();

            _provider = services.BuildServiceProvider();

            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            Seed(db);
        }

        /// <summary>يوم الإنتاج اللي كل الاختبارات بتشتغل عليه</summary>
        public static DateTime Today => new(2026, 7, 29);

        /// <summary>Scope جديد بـ DbContext مستقل — بيحاكي "عملية واحدة" في التطبيق</summary>
        public IServiceScope CreateScope() => _provider.CreateScope();

        public T GetService<T>(IServiceScope scope) where T : notnull =>
            scope.ServiceProvider.GetRequiredService<T>();

        /// <summary>تنفيذ عملية في Scope مستقل (زي ما الـ ViewModels بتعمل بالظبط)</summary>
        public async Task<TResult> InScopeAsync<TService, TResult>(Func<TService, Task<TResult>> action)
            where TService : notnull
        {
            using var scope = CreateScope();
            return await action(GetService<TService>(scope));
        }

        /// <summary>كل سجلات الإنتاج في اليوم — للتأكد من اللي اتحفظ فعلاً</summary>
        public async Task<List<DailyProduction>> GetProductionAsync()
        {
            using var scope = CreateScope();
            var db = GetService<AppDbContext>(scope);
            return await db.DailyProductions
                .Include(p => p.ProductionStage)
                .AsNoTracking()
                .ToListAsync();
        }

        private static void Seed(AppDbContext db)
        {
            db.Products.AddRange(
                new Product { Id = ProductRingId, Name = "دبلة", IsActive = true },
                new Product { Id = ProductChainId, Name = "سلسلة", IsActive = true });

            db.ProductionStages.AddRange(
                new ProductionStage
                {
                    Id = RingStage1Id, ProductId = ProductRingId, StageName = "تشكيل",
                    SortOrder = 1, PiecesPerWorkday = 10, IsActive = true
                },
                new ProductionStage
                {
                    Id = RingStage2Id, ProductId = ProductRingId, StageName = "تلميع",
                    SortOrder = 2, PiecesPerWorkday = 10, IsActive = true
                },
                new ProductionStage
                {
                    Id = ChainStage1Id, ProductId = ProductChainId, StageName = "لحام",
                    SortOrder = 1, PiecesPerWorkday = 10, IsActive = true
                });

            db.Workers.AddRange(
                new Worker { Id = WorkerAhmedId, FullName = "أحمد", IsActive = true, DailyWageEgp = 200m },
                new Worker { Id = WorkerSaidId, FullName = "سعيد", IsActive = true, DailyWageEgp = 200m },
                // العاملة بالساعة: HourlyRole مش null — ده اللي بيخليها "بالساعة"
                new Worker
                {
                    Id = WorkerMonaHourlyId, FullName = "منى", IsActive = true,
                    DailyWageEgp = 150m, HourlyRole = HourlyRole.Racking
                });

            // كل عامل مؤهل لكل المراحل — التأهيل مش موضوع الاختبارات دي،
            // ولازم يعدّي عشان نوصل لقاعدة التكليف اللي بنختبرها
            var stageIds = new[] { RingStage1Id, RingStage2Id, ChainStage1Id };
            var workerIds = new[] { WorkerAhmedId, WorkerSaidId };

            db.WorkerSkills.AddRange(
                from workerId in workerIds
                from stageId in stageIds
                select new WorkerSkill
                {
                    WorkerId = workerId,
                    ProductionStageId = stageId,
                    Level = SkillLevel.Proficient
                });

            db.SaveChanges();
        }

        public void Dispose()
        {
            _provider.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
    }
}
