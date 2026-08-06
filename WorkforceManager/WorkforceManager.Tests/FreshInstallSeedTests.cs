using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WorkforceManager.Data;
using WorkforceManager.Data.Seed;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// أول تشغيل على جهاز جديد.
    ///
    /// دي أخطر لحظة في البرنامج: لو ربط المهارات وقع فيها، التركيب
    /// بيطلع بمنتجات وعمال بس **من غير مؤهلين** — يعني مفيش عامل ينفع
    /// يتحط على أي مرحلة، وشاشة التسجيل اليومي بتبقى مقفولة فعليًا.
    /// والعطل ده صامت: مفيش رسالة خطأ، الشاشة فاضية وبس.
    ///
    /// الربط كان بيتم بكود العامل. الكود اتشال من الداتابيز والربط بقى
    /// بالاسم، فالاختبارات دي بتثبّت إن التركيب الجديد لسه بيطلع شغّال.
    /// </summary>
    public class FreshInstallSeedTests : IAsyncLifetime
    {
        private readonly string _dbPath =
            Path.Combine(Path.GetTempPath(), $"wfm-seed-{Guid.NewGuid():N}.db");

        private AppDbContext _db = null!;

        public async Task InitializeAsync()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={_dbPath}")
                .Options;

            _db = new AppDbContext(options);
            await _db.Database.EnsureCreatedAsync();

            // نفس اللي App.OnStartup بتعمله على جهاز فاضي
            await DatabaseSeeder.SeedIfEmptyAsync(_db);
        }

        public async Task DisposeAsync()
        {
            await _db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Fact]
        public async Task A_fresh_install_comes_up_with_products_workers_and_stages()
        {
            Assert.NotEmpty(await _db.Products.ToListAsync());
            Assert.NotEmpty(await _db.Workers.ToListAsync());
            Assert.NotEmpty(await _db.ProductionStages.ToListAsync());
        }

        [Fact]
        public async Task A_fresh_install_comes_up_with_workers_actually_linked_to_stages()
        {
            // ده الاختبار اللي بيمسك العطل الصامت: من غير الروابط دي
            // محدش بيبقى مؤهل لأي مرحلة والتسجيل اليومي بيبقى مستحيل
            var links = await _db.WorkerSkills.CountAsync();

            Assert.True(links > 0, "التركيب الجديد طلع من غير أي مهارة مربوطة");
        }

        [Fact]
        public async Task The_seed_links_land_on_the_right_worker_not_just_any_worker()
        {
            // "ابوزيد عبدالله السيد عبدالله" ملاحظته "جميع مراحل صنفره
            // المحابس و اللوازم" — لازم يطلع بمهارات على GRS تحديدًا
            var worker = await _db.Workers
                .FirstAsync(w => w.FullName == "ابوزيد عبدالله السيد عبدالله");

            var stages = await _db.WorkerSkills
                .Where(ws => ws.WorkerId == worker.Id)
                .Include(ws => ws.ProductionStage).ThenInclude(s => s.Product)
                .Select(ws => ws.ProductionStage.Product!.Name)
                .Distinct()
                .ToListAsync();

            Assert.Contains("GRS", stages);
        }

        [Fact]
        public void Every_code_in_the_skills_seed_resolves_to_a_real_worker_name()
        {
            // الكود بقى معرّف داخلي للبذرة بس. لو حد ضاف رابط بكود مش
            // موجود في RealDataSeed، الرابط ده كان هيتفقد بصمت.
            var nameByCode = RealDataSeed.NameByCode();
            var seededNames = RealDataSeed.BuildWorkers().Select(w => w.FullName).ToHashSet();

            var orphans = WorkerSkillsSeed.BuildLinks().Keys
                .Where(code => !nameByCode.ContainsKey(code))
                .ToList();

            Assert.Empty(orphans);
            Assert.All(nameByCode.Values, name => Assert.Contains(name, seededNames));
        }

        [Fact]
        public void Seeded_worker_names_are_unique_because_the_link_is_by_name_now()
        {
            // الربط بالاسم صح طول ما الأسماء فريدة. لو اتكرر اسم في
            // البذرة، الرابط بتاعه هيتسكّب (الازدواج بيتشال من القايمة).
            var duplicates = RealDataSeed.BuildWorkers()
                .GroupBy(w => w.FullName)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Assert.Empty(duplicates);
        }

        [Fact]
        public async Task Running_the_seed_again_adds_nothing_twice()
        {
            var before = await _db.WorkerSkills.CountAsync();

            await DatabaseSeeder.SeedWorkerSkillLinksAsync(_db);

            Assert.Equal(before, await _db.WorkerSkills.CountAsync());
        }
    }
}
