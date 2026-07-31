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
        public DbSet<ProductionBatch> ProductionBatches => Set<ProductionBatch>();
        public DbSet<ProductionDayClosure> ProductionDayClosures => Set<ProductionDayClosure>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

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

            // ---------- ProductionBatch: دفعة قطع بتمشي في خط منتج ----------
            modelBuilder.Entity<ProductionBatch>()
                .HasOne(b => b.Product)
                .WithMany()
                .HasForeignKey(b => b.ProductId)
                .OnDelete(DeleteBehavior.Restrict); // دفعة تاريخية بتمنع حذف منتجها (المنتجات بتتوقف مبتتحذفش)

            modelBuilder.Entity<ProductionBatch>()
                .HasOne(b => b.LastCompletedStage)
                .WithMany()
                .HasForeignKey(b => b.LastCompletedStageId)
                .OnDelete(DeleteBehavior.Restrict);

            // القسمة (تكميل جزئي): الدفعة الجديدة بتفضل مشيرة للأصلية للتتبع.
            // Restrict عشان حذف دفعة ميسيبش أولادها بمرجع مكسور
            modelBuilder.Entity<ProductionBatch>()
                .HasOne(b => b.SplitFromBatch)
                .WithMany()
                .HasForeignKey(b => b.SplitFromBatchId)
                .OnDelete(DeleteBehavior.Restrict);

            // سجل الإنتاج بيشاور على الدفعة اللي حرّكها.
            // SetNull مش Restrict: لو دفعة اتشالت يومًا ما، سجلات الإنتاج
            // (وأجور العمال المبنية عليها) لازم تفضل — بتفقد الربط بس
            modelBuilder.Entity<DailyProduction>()
                .HasOne(dp => dp.ProductionBatch)
                .WithMany(b => b.Productions)
                .HasForeignKey(dp => dp.ProductionBatchId)
                .OnDelete(DeleteBehavior.SetNull);

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
            modelBuilder.Entity<DailyProduction>()
                .HasIndex(dp => dp.Date);

            modelBuilder.Entity<Attendance>()
                .HasIndex(a => a.Date);

            modelBuilder.Entity<Penalty>()
                .HasIndex(p => p.Date);

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

            // ---------- فهارس لتسريع البحث بالاسم ----------
            modelBuilder.Entity<Worker>()
                .HasIndex(w => w.FullName);

            modelBuilder.Entity<Product>()
                .HasIndex(p => p.Name);
        }
    }
}
