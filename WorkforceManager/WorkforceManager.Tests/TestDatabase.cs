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

        /// <summary>
        /// "شنطة" — 3 مراحل. لازم يكون فيه منتج بأكتر من مرحلتين عشان نختبر
        /// دفعة واقفة في **نص** الخط (لا أول ولا آخر) — دي الحالة اللي كل
        /// منطق الترحيل قايم عليها.
        /// </summary>
        public const int ProductBagId = 3;

        /// <summary>
        /// "تلاتات" — 3 مراحل كوتة كل واحدة 3 قطع. موجود عشان اختبارات
        /// التقريب: كل الكوتات التانية 10 والقسمة عليها بتطلع تامة، فقاعدة
        /// تقريب اليوميات مكانش ينفع تتختبر من غيره.
        /// </summary>
        public const int ProductThirdsId = 4;

        public const int RingStage1Id = 1;    // دبلة / تشكيل
        public const int RingStage2Id = 2;    // دبلة / تلميع
        public const int ChainStage1Id = 3;   // سلسلة / لحام
        public const int BagStage1Id = 4;     // شنطة / قص
        public const int BagStage2Id = 5;     // شنطة / خياطة
        public const int BagStage3Id = 6;     // شنطة / تشطيب
        public const int ThirdsStage1Id = 7;  // تلاتات / تلت
        public const int ThirdsStage2Id = 8;  // تلاتات / تلت تاني
        public const int ThirdsStage3Id = 9;  // تلاتات / تلت تالت

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
            services.AddScoped<IWageAdjustmentRepository, WageAdjustmentRepository>();
            services.AddScoped<IGenericRepository<ProductionStage>, GenericRepository<ProductionStage>>();
            services.AddScoped<IProductionDayClosureRepository, ProductionDayClosureRepository>();
            services.AddScoped<IActivityEventRepository, ActivityEventRepository>();
            services.AddScoped<IWorkerSkillRepository, WorkerSkillRepository>();
            services.AddScoped<IGenericRepository<OperationsCredential>, GenericRepository<OperationsCredential>>();
            services.AddScoped<IGenericRepository<WorkerSkill>, GenericRepository<WorkerSkill>>();
            services.AddScoped<IGenericRepository<AppUser>, GenericRepository<AppUser>>();
            services.AddScoped<IUnitOfWork, EfUnitOfWork>();

            services.AddScoped<WorkerAssignmentGuard>();
            services.AddScoped<ProductionFlowService>();
            services.AddScoped<DayClosureService>();
            services.AddScoped<DailyProductionReportService>();
            services.AddScoped<WorkdayCalculationService>();
            services.AddScoped<PenaltyService>();
            services.AddScoped<AttendanceAutomationService>();
            services.AddScoped<AttendanceService>();
            services.AddScoped<HourlyWorkdayService>();
            services.AddScoped<WeeklySummaryService>();
            services.AddScoped<PayrollService>();
            services.AddScoped<ProductManagementService>();
            services.AddScoped<WorkerManagementService>();

            // الأنظمة المشتركة: الهوية واحدة للكل، والبوابة والسجل والحذف
            // بيتنادوا من مكان واحد
            services.AddSingleton<CurrentUserContext>();
            services.AddScoped<OperationsPasswordService>();
            services.AddScoped<ActivityLogService>();
            services.AddScoped<SoftDeleteService>();
            services.AddScoped<DeletionScopeService>();
            services.AddScoped<SkillRatingService>();
            services.AddScoped<AuthService>();
            services.AddScoped<ProductActivityService>();
            services.AddScoped<PendingWorkService>();
            services.AddScoped<ScrapService>();
            services.AddScoped<ProductionStageOutputService>();
            services.AddScoped<ProductionReportService>();
            services.AddScoped<WageAdjustmentService>();
            services.AddScoped<ReportBuilderService>();
            services.AddScoped<ProductionChartService>();
            services.AddScoped<DepartmentAttendanceService>();
            services.AddSingleton<ReportTableExcelService>();
            services.AddSingleton<PayslipStripExcelService>();

            _provider = services.BuildServiceProvider();

            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            Seed(db);

            // أسباب الهالك بتتزرع من نفس الدالة اللي التركيب الحقيقي
            // بيستخدمها — عشان الاختبار يشوف نفس القايمة اللي المستخدم
            // هيشوفها، مش قايمة تانية مكتوبة هنا
            DatabaseSeeder.SeedScrapReasonsAsync(db).GetAwaiter().GetResult();
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

        /// <summary>
        /// بيسجّل مستخدم اختباري ثابت دخول (بيعمله لو مش موجود) وينادي
        /// CurrentUserContext.SignIn بمعرّفه الحقيقي — الاختبارات اللي
        /// بتحتاج OperationsPasswordService (كلمة سر العمليات بقت لكل
        /// حساب لوحده) لازم تنادي دي الأول، عشان AppUserId يبقى صف
        /// حقيقي موجود (قيد المفتاح الأجنبي بين OperationsCredential
        /// وAppUser).
        /// </summary>
        public async Task<int> SignInTestUserAsync(string username = "test-user")
        {
            using var scope = CreateScope();
            var db = GetService<AppDbContext>(scope);

            var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Username == username);
            if (user is null)
            {
                user = new AppUser
                {
                    Username = username, PasswordHash = "x", PasswordSalt = "x", DisplayName = username
                };
                db.AppUsers.Add(user);
                await db.SaveChangesAsync();
            }

            GetService<CurrentUserContext>(scope).SignIn(username, user.DisplayName, user.Id);
            return user.Id;
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

        /// <summary>كل سجلات الإنتاج الفعلي — للتأكد من الرقم المستقل ده اتحفظ فعلاً</summary>
        public async Task<List<ProductionStageOutput>> GetProductionStageOutputsAsync()
        {
            using var scope = CreateScope();
            var db = GetService<AppDbContext>(scope);
            return await db.ProductionStageOutputs.AsNoTracking().ToListAsync();
        }

        private static void Seed(AppDbContext db)
        {
            db.Products.AddRange(
                new Product { Id = ProductRingId, Name = "دبلة", IsActive = true },
                new Product { Id = ProductChainId, Name = "سلسلة", IsActive = true },
                new Product { Id = ProductBagId, Name = "شنطة", IsActive = true },
                new Product { Id = ProductThirdsId, Name = "تلاتات", IsActive = true });

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
                },
                new ProductionStage
                {
                    Id = BagStage1Id, ProductId = ProductBagId, StageName = "قص",
                    SortOrder = 1, PiecesPerWorkday = 10, IsActive = true
                },
                new ProductionStage
                {
                    Id = BagStage2Id, ProductId = ProductBagId, StageName = "خياطة",
                    SortOrder = 2, PiecesPerWorkday = 10, IsActive = true
                },
                new ProductionStage
                {
                    Id = BagStage3Id, ProductId = ProductBagId, StageName = "تشطيب",
                    SortOrder = 3, PiecesPerWorkday = 10, IsActive = true
                },
                // كوتة 3 عن قصد: 1 ÷ 3 = 0.3333... فالتقريب بيبان.
                // كل الكوتات التانية 10، و1 ÷ 10 بيطلع رقم تام، فأي غلط
                // في قاعدة التقريب كان بيعدّي من غير ما تمسكه ولا اختبار
                new ProductionStage
                {
                    Id = ThirdsStage1Id, ProductId = ProductThirdsId, StageName = "تلت",
                    SortOrder = 1, PiecesPerWorkday = 3, IsActive = true
                },
                new ProductionStage
                {
                    Id = ThirdsStage2Id, ProductId = ProductThirdsId, StageName = "تلت تاني",
                    SortOrder = 2, PiecesPerWorkday = 3, IsActive = true
                },
                new ProductionStage
                {
                    Id = ThirdsStage3Id, ProductId = ProductThirdsId, StageName = "تلت تالت",
                    SortOrder = 3, PiecesPerWorkday = 3, IsActive = true
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
            var stageIds = new[]
            {
                RingStage1Id, RingStage2Id, ChainStage1Id,
                BagStage1Id, BagStage2Id, BagStage3Id,
                ThirdsStage1Id, ThirdsStage2Id, ThirdsStage3Id
            };
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

        /// <summary>
        /// **ClearPool مش ClearAllPools.**
        ///
        /// كان ClearAllPools، وهو **عام على العملية كلها**: بيقفل
        /// الاتصالات المتجمّعة لكل قواعد البيانات المفتوحة في العملية.
        /// والاختبارات بتشتغل بالتوازي، فكل اختبار بيخلص كان بيسحب
        /// الاتصالات من تحت كل اختبار تاني شغال في نفس اللحظة.
        ///
        /// النتيجة: اختبار بيقع مرة كل عشرين تشغيلة تقريبًا، ومش نفس
        /// الاختبار كل مرة، ومن غير أي علاقة بالكود اللي بيختبره. ده
        /// أسوأ نوع أعطال لأنه بيعلّم الناس تتجاهل الأحمر — ويوم ما
        /// يبقى العطل حقيقي محدش هيصدّقه.
        ///
        /// ClearPool بياخد اتصال وبيفضّي تجمّع **سلسلة الاتصال بتاعته
        /// هي بس** — وكل نسخة هنا ليها ملفها الخاص.
        /// </summary>
        public void Dispose()
        {
            _provider.Dispose();

            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
                Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);

            // ملف مؤقت: لو ويندوز لسه ماسكه، تركه أهون من إفشال اختبار
            // ناجح — مجلد الـ Temp بيتنضّف لوحده
            try
            {
                if (File.Exists(_dbPath)) File.Delete(_dbPath);
            }
            catch (IOException)
            {
            }
        }
    }
}
