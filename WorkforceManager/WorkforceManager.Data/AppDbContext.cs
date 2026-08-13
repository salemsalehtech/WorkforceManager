using Microsoft.EntityFrameworkCore;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Data
{
    /// <summary>
    /// نقطة الاتصال الوحيدة بقاعدة البيانات. كل الاستعلامات في المشروع
    /// بتمر من هنا عن طريق الـ Repositories، مفيش أي طبقة تانية بتتكلم
    /// مع SQLite مباشرة — ده بيضمن سهولة الصيانة والاختبار.
    /// </summary>
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Worker> Workers => Set<Worker>();
        public DbSet<Product> Products => Set<Product>();
        public DbSet<ProductionStage> ProductionStages => Set<ProductionStage>();
        public DbSet<WorkerSkill> WorkerSkills => Set<WorkerSkill>();
        public DbSet<DailyProduction> DailyProductions => Set<DailyProduction>();
        public DbSet<Attendance> Attendances => Set<Attendance>();
        public DbSet<Penalty> Penalties => Set<Penalty>();
        public DbSet<AppUser> AppUsers => Set<AppUser>();
        public DbSet<HourlyWorkLog> HourlyWorkLogs => Set<HourlyWorkLog>();
        public DbSet<WageAdjustment> WageAdjustments => Set<WageAdjustment>();
        public DbSet<ProductionDayClosure> ProductionDayClosures => Set<ProductionDayClosure>();
        public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();
        public DbSet<OperationsCredential> OperationsCredentials => Set<OperationsCredential>();
        public DbSet<ProductionScrap> ProductionScraps => Set<ProductionScrap>();
        public DbSet<ScrapReason> ScrapReasons => Set<ScrapReason>();
        public DbSet<ProductionStageOutput> ProductionStageOutputs => Set<ProductionStageOutput>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ---------- الحذف الناعم ----------
            // الفلتر العام متحطّ على الكيانات **الطرفية** بس (اللي محدش
            // بيشاور عليها): سجل الإنتاج ومهارة العامل.
            //
            // ومتحطّش عن قصد على Worker / Product / ProductionStage.
            // السبب مهم: DailyProduction.Worker علاقة إجبارية، ولو العامل
            // عليه فلتر فـ EF بيستبعد سجلات إنتاجه كمان في أي Include —
            // يعني حذف عامل كان هيمسح تاريخ أجوره من كل التقارير، وده
            // بالظبط اللي الحذف الناعم موجود عشان يمنعه.
            //
            // بدالها: إخفاء العامل/المنتج/المرحلة من القوايم بيتم بامتداد
            // مشترك واحد (SoftDeleteQueryExtensions.ExcludeDeleted) —
            // مكان واحد برضه، بس بيسيب الملاحة التاريخية شغالة.
            modelBuilder.Entity<DailyProduction>().HasQueryFilter(d => !d.IsDeleted);

            // مهارة عامل محذوف أو مرحلة محذوفة مالهاش أي معنى في أي شاشة،
            // ومفيش سجل تاريخي بيشاور عليها — فالفلتر العام آمن هنا
            modelBuilder.Entity<WorkerSkill>()
                .HasQueryFilter(ws => !ws.Worker.IsDeleted && !ws.ProductionStage.IsDeleted);

            // ---------- Product -> ProductionStage (1-to-many) ----------
            modelBuilder.Entity<ProductionStage>()
                .HasOne(s => s.Product)
                .WithMany(p => p.Stages)
                .HasForeignKey(s => s.ProductId)
                .OnDelete(DeleteBehavior.Cascade); // حذف منتج يحذف مراحله (منطقي، مفيش مرحلة من غير منتج)

            // ---------- WorkerSkill: Worker <-> ProductionStage (many-to-many عبر جدول ربط) ----------
            modelBuilder.Entity<WorkerSkill>()
                .HasOne(ws => ws.Worker)
                .WithMany(w => w.Skills)
                .HasForeignKey(ws => ws.WorkerId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<WorkerSkill>()
                .HasOne(ws => ws.ProductionStage)
                .WithMany(s => s.QualifiedWorkers)
                .HasForeignKey(ws => ws.ProductionStageId)
                .OnDelete(DeleteBehavior.Cascade);

            // منع تكرار نفس المهارة لنفس العامل مرتين
            modelBuilder.Entity<WorkerSkill>()
                .HasIndex(ws => new { ws.WorkerId, ws.ProductionStageId })
                .IsUnique();

            // ---------- DailyProduction: Worker + ProductionStage ----------
            modelBuilder.Entity<DailyProduction>()
                .HasOne(dp => dp.Worker)
                .WithMany(w => w.ProductionRecords)
                .HasForeignKey(dp => dp.WorkerId)
                .OnDelete(DeleteBehavior.Restrict); // منع حذف عامل له سجلات إنتاج تاريخية (حماية البيانات)

            modelBuilder.Entity<DailyProduction>()
                .HasOne(dp => dp.ProductionStage)
                .WithMany(s => s.ProductionRecords)
                .HasForeignKey(dp => dp.ProductionStageId)
                .OnDelete(DeleteBehavior.Restrict);

            // ---------- Attendance: Worker (1-to-many) ----------
            modelBuilder.Entity<Attendance>()
                .HasOne(a => a.Worker)
                .WithMany(w => w.AttendanceRecords)
                .HasForeignKey(a => a.WorkerId)
                .OnDelete(DeleteBehavior.Cascade); // حذف عامل (نادر، عادة بيتعمل له IsActive=false) يحذف سجلات حضوره

            // ---------- Penalty: Worker (1-to-many) ----------
            modelBuilder.Entity<Penalty>()
                .HasOne(p => p.Worker)
                .WithMany(w => w.Penalties)
                .HasForeignKey(p => p.WorkerId)
                .OnDelete(DeleteBehavior.Cascade); // نفس قاعدة الحضور: حذف عامل (نادر) يحذف جزاءاته

            // ---------- فهارس التاريخ ----------
            // كل استعلامات اليوم والأسبوع (شاشة التسجيل، التقارير، الملخص الأسبوعي)
            // بتفلتر بالتاريخ الأول — الفهارس المركّبة الموجودة بادئة بـ WorkerId
            // فمش بتخدم الاستعلامات دي، ومن غير فهرس Date كل استعلام بيلف على
            // الجدول كله (هيبان بطؤه مع تراكم شهور من البيانات)
            //
            // **شرط لازم عشان الفهارس دي تشتغل**: المقارنة تبقى على العمود
            // نفسه (dp.Date >= from.Date)، مش على دالة فوقه
            // (dp.Date.Date >= ...). التانية بتترجم لـ date(...) في SQL،
            // وSQLite ساعتها مش بيقدر يستخدم الفهرس فبيلف على الجدول كله —
            // يعني الفهرس بيدفع تكلفة الكتابة من غير ما يفيد أي قراءة.
            // كل أعمدة Date بتتكتب بـ date.Date (نص الليل) في كل مسارات
            // الكتابة، فالمقارنة المباشرة صح ومظبوطة.
            // (ActivityEvent.OccurredAt استثناء: فيه وقت، فمداه نصف مفتوح.)
            modelBuilder.Entity<DailyProduction>()
                .HasIndex(dp => dp.Date);

            modelBuilder.Entity<Attendance>()
                .HasIndex(a => a.Date);

            modelBuilder.Entity<Penalty>()
                .HasIndex(p => p.Date);

            // ---------- الهالك ----------
            // حذف مرحلة بيحذف هالكها: الهالك بيوصف قطع خلصت المرحلة دي،
            // فمن غيرها الرقم مالوش معنى — نفس قاعدة سجلات الإنتاج
            modelBuilder.Entity<ProductionScrap>()
                .HasOne(s => s.ProductionStage)
                .WithMany()
                .HasForeignKey(s => s.ProductionStageId)
                .OnDelete(DeleteBehavior.Cascade);

            // السبب بيتفضّى مش بيحذف السجل: تقرير الشهر اللي فات لازم
            // يفضل يعرض الكمية حتى لو السبب اتشال من الإعدادات
            modelBuilder.Entity<ProductionScrap>()
                .HasOne(s => s.Reason)
                .WithMany(r => r.ScrapRecords)
                .HasForeignKey(s => s.ScrapReasonId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<ScrapReason>()
                .HasIndex(r => r.Name)
                .IsUnique(); // سببين بنفس الاسم بيخلّوا التقرير يتقسم على نفسه

            // ---------- الإنتاج الفعلي للمرحلة ----------
            // Restrict مش Cascade (عكس الهالك): الرقم ده بقى مرجع تقارير
            // رسمي زي DailyProduction بالظبط — حذف مرحلة ليها سجل فيه
            // لازم يفشل بوضوح، مش يمسح الرقم بصمت ويسيب تقرير قديم بلا مصدر
            modelBuilder.Entity<ProductionStageOutput>()
                .HasOne(o => o.ProductionStage)
                .WithMany()
                .HasForeignKey(o => o.ProductionStageId)
                .OnDelete(DeleteBehavior.Restrict);

            // ---------- عامل الرص الثابت بتاع المنتج ----------
            // SetNull مش Cascade — نفس قاعدة ProductionScrap.ScrapReasonId:
            // حذف عامل الرص يفضّي الحقل، مش يمسح المنتج
            modelBuilder.Entity<Product>()
                .HasOne(p => p.RackingWorker)
                .WithMany()
                .HasForeignKey(p => p.RackingWorkerId)
                .OnDelete(DeleteBehavior.SetNull);

            // سعر اليومية بالجنيه بدقة عشرية كافية
            modelBuilder.Entity<Worker>()
                .Property(w => w.DailyWageEgp)
                .HasColumnType("decimal(10,2)");

            // ---------- HourlyWorkLog: Worker (1-to-many) ----------
            modelBuilder.Entity<HourlyWorkLog>()
                .HasOne(h => h.Worker)
                .WithMany(w => w.HourlyWorkLogs)
                .HasForeignKey(h => h.WorkerId)
                .OnDelete(DeleteBehavior.Cascade); // نفس قاعدة الحضور: حذف عامل (نادر) يحذف سجلات ساعاته

            // WorkdaysCredited رقم عشري بدقة كافية للنص واليوميات
            modelBuilder.Entity<HourlyWorkLog>()
                .Property(h => h.WorkdaysCredited)
                .HasColumnType("decimal(5,2)");

            // ---------- WageAdjustment: Worker (1-to-many) ----------
            modelBuilder.Entity<WageAdjustment>()
                .HasOne(a => a.Worker)
                .WithMany(w => w.WageAdjustments)
                .HasForeignKey(a => a.WorkerId)
                .OnDelete(DeleteBehavior.Cascade); // نفس قاعدة الجزاءات: حذف عامل (نادر) يحذف تعديلات أجره

            // المبلغ بالجنيه بدقة عشرية كافية
            modelBuilder.Entity<WageAdjustment>()
                .Property(a => a.AmountEgp)
                .HasColumnType("decimal(10,2)");

            // فهرس التاريخ لاستعلامات اليوم/الفترة (زي باقي الجداول)
            modelBuilder.Entity<WageAdjustment>()
                .HasIndex(a => a.Date);

            // ---------- AppUser: اسم المستخدم فريد (مفيش حسابين بنفس الاسم) ----------
            modelBuilder.Entity<AppUser>()
                .HasIndex(u => u.Username)
                .IsUnique();

            // حساب دخول واحد لكل حساب إداري بالحد الأقصى — SetNull: حذف
            // العامل (نادرًا) ميشيلش حساب الدخول، بس يفصله عنه بس
            modelBuilder.Entity<AppUser>()
                .HasOne(u => u.Worker)
                .WithMany()
                .HasForeignKey(u => u.WorkerId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<AppUser>()
                .HasIndex(u => u.WorkerId)
                .IsUnique()
                .HasFilter("\"WorkerId\" IS NOT NULL");

            // كلمة سر عمليات واحدة بالحد الأقصى لكل حساب دخول — Cascade:
            // حذف حساب الدخول يشيل كلمة سره كمان (مالهاش معنى لوحدها)
            modelBuilder.Entity<OperationsCredential>()
                .HasOne(c => c.AppUser)
                .WithMany()
                .HasForeignKey(c => c.AppUserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<OperationsCredential>()
                .HasIndex(c => c.AppUserId)
                .IsUnique()
                .HasFilter("\"AppUserId\" IS NOT NULL");

            // ---------- فهارس لتسريع البحث بالاسم ----------
            modelBuilder.Entity<Worker>()
                .HasIndex(w => w.FullName);

            modelBuilder.Entity<Product>()
                .HasIndex(p => p.Name);

            // ---------- نسبة الأداء المقاس ----------
            // من غير النوع ده EF بيخزّن الـ decimal في SQLite كنص، والمقارنة
            // بين النصوص أبجدية: "10.5" أقل من "9.0". النسبة دلوقتي بتتقارن
            // في الذاكرة بس، بس عمود رقمي متخزّن كنص لغم بيستنى أول استعلام
            // يرتّب أو يفلتر بيه. باقي الأعمدة العشرية متظبطة أصلاً.
            modelBuilder.Entity<WorkerSkill>()
                .Property(ws => ws.MeasuredRatio)
                .HasColumnType("decimal(5,2)");

            // ---------- قيود على مستوى قاعدة البيانات ----------
            // الشروط دي مضمونة في الخدمات أصلاً. وجودها هنا كمان مش تكرار:
            // الخدمة بتحمي المسار اللي بيعدّي عليها، والقيد بيحمي الجدول
            // نفسه — من ترحيل غلط، أو أداة خارجية، أو مسار جديد حد يكتبه
            // بعد سنين وينسى القاعدة. البيانات بتفضل صح حتى لو الكود غلط.
            modelBuilder.Entity<WorkerSkill>().ToTable(t => t.HasCheckConstraint(
                "CK_WorkerSkill_Stars", "[Stars] BETWEEN 1 AND 5"));

            // القسمة على اليومية هي أساس حساب الأجر. صفر بيرجّع صفر في
            // WorkdayMath بدل ما يرمي، بس صفر أصلاً مايوصلش للجدول
            modelBuilder.Entity<ProductionStage>().ToTable(t => t.HasCheckConstraint(
                "CK_ProductionStage_Quota", "[PiecesPerWorkday] > 0"));

            modelBuilder.Entity<DailyProduction>().ToTable(t => t.HasCheckConstraint(
                "CK_DailyProduction_Amounts",
                "[PieceCount] >= 0 AND [PiecesPerWorkdayAtEntry] > 0"));

            modelBuilder.Entity<WageAdjustment>().ToTable(t => t.HasCheckConstraint(
                "CK_WageAdjustment_Amount", "[AmountEgp] > 0"));

            modelBuilder.Entity<Worker>().ToTable(t => t.HasCheckConstraint(
                "CK_Worker_DailyWage", "[DailyWageEgp] >= 0"));
        }
    }
}
