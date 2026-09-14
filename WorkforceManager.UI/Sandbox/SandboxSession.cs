using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.Services;
using WorkforceManager.Data;
using WorkforceManager.UI;

namespace WorkforceManager.UI.Sandbox
{
    /// <summary>
    /// جلسة تجربة معزولة تمامًا عن بيانات المصنع الحقيقية: ملف SQLite مؤقت،
    /// بيانات وهمية واضحة (<see cref="SandboxDemoSeeder"/>)، ونفس تسجيلات
    /// DI الحقيقية (<see cref="AppServiceRegistration.AddWorkforceManagerCore"/>)
    /// — فمفيش أي ViewModel/Service محتاج يعرف إنه شغال جوّه تجربة، بالظبط
    /// زي WorkforceManager.Tests\TestDatabase.cs بس لمستخدم حقيقي بيتفرّج
    /// على شاشته بدل اختبار آلي.
    ///
    /// Scope جذري واحد بيفضل مفتوح طول عمر جلسة التجربة (زي
    /// MainWindow._session بالظبط) — مش CreateScope() لكل عملية زي
    /// TestDatabase، عشان الخدمات Scoped (DailyEntryViewModel مثلًا)
    /// تتصرف "زي Singleton جوّه الجلسة" هنا برضو، نفس سلوك الجلسة
    /// الحقيقية بالظبط.
    /// </summary>
    public sealed class SandboxSession : IDisposable
    {
        private readonly string _dbPath;
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _rootScope;

        /// <summary>بيتحل منه أي View/ViewModel — نفس استخدام MainWindow._session بالظبط</summary>
        public IServiceProvider Services => _rootScope.ServiceProvider;

        private SandboxSession(string dbPath, ServiceProvider provider)
        {
            _dbPath = dbPath;
            _provider = provider;
            _rootScope = provider.CreateScope();
        }

        public static async Task<SandboxSession> CreateAsync()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"wfm-sandbox-{Guid.NewGuid():N}.db");

            var services = new ServiceCollection();
            services.AddWorkforceManagerCore($"Data Source={dbPath};Default Timeout=30");
            var provider = services.BuildServiceProvider();

            var session = new SandboxSession(dbPath, provider);

            var db = session.Services.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            await SandboxDemoSeeder.SeedAsync(db);

            // خدمات كتير (سجل العمليات، كلمة سر العمليات) بتعتمد على وجود
            // مستخدم مسجّل دخول — نفس فكرة TestDatabase.SignInTestUserAsync
            session.Services.GetRequiredService<CurrentUserContext>()
                .SignIn("sandbox", "وضع التجربة", SandboxDemoSeeder.DemoUserId);

            return session;
        }

        public void Dispose()
        {
            _rootScope.Dispose();
            _provider.Dispose();

            // ClearPool مش ClearAllPools — بيفضّي تجمّع سلسلة الاتصال بتاعة
            // الملف المؤقت ده بس، من غير ما يأثّر على قاعدة بيانات حقيقية
            // شغالة في نفس العملية (نفس سبب TestDatabase.cs بالظبط)
            using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
                SqliteConnection.ClearPool(connection);

            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); }
            catch (IOException) { }
        }
    }
}
