using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// النسخة الاحتياطية لازم تبقى **قاعدة بيانات شغالة**، مش ملف بحجم
    /// مظبوط.
    ///
    /// النسخ كان File.Copy على ملف SQLite. ده بيفترض إن مفيش كتابة
    /// شغالة ومفيش journal ناقص من إغلاق مفاجئ — افتراض بيفضل صح لحد
    /// أول انقطاع كهربا، واليوم ده بالذات هو اللي المستخدم بيحتاج فيه
    /// النسخة. بقى VACUUM INTO: SQLite هو اللي بيكتب النسخة، وهو اللي
    /// بيقرر إمتى المحتوى متسق.
    ///
    /// الاختبارات دي بتفتح النسخة وتقرا منها فعلًا — ملف بيتكتب "بنجاح"
    /// وبعدين ميفتحش هو أسوأ حاجة ممكنة في نسخة احتياطية.
    /// </summary>
    [Collection("Backup")]
    public class BackupIntegrityTests : IDisposable
    {
        private readonly List<string> _folders = new();

        public void Dispose()
        {
            // مفيش ClearAllPools هنا: الاستدعاء ده بيلمس كل قواعد
            // البيانات في العملية مش بتاعتنا بس (شوف TestDatabase.Dispose)
            foreach (var folder in _folders)
                try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { /* مؤقت */ }
        }

        /// <summary>يعمل قاعدة بيانات حقيقية فيها بيانات ويرجّع مسارها</summary>
        private async Task<string> NewDatabaseAsync()
        {
            var folder = Path.Combine(Path.GetTempPath(), $"wm-snap-{Guid.NewGuid():N}");
            Directory.CreateDirectory(folder);
            _folders.Add(folder);

            var path = Path.Combine(folder, "workforce.db");

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={path}").Options;

            await using (var db = new AppDbContext(options))
            {
                await db.Database.MigrateAsync();
                await DatabaseSeeder.SeedIfEmptyAsync(db);
            }

            ClearPoolFor(path);
            return path;
        }

        /// <summary>يفضّي تجمّع اتصالات قاعدة واحدة بس — مش كل القواعد</summary>
        private static void ClearPoolFor(string dbPath)
        {
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            SqliteConnection.ClearPool(connection);
        }

        private static async Task<int> CountWorkersAsync(string dbPath)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={dbPath}").Options;

            await using var db = new AppDbContext(options);
            return await db.Workers.CountAsync();
        }

        [Fact]
        public async Task TheDailyBackupOpensAsARealDatabase_WithTheSameRows()
        {
            var dbPath = await NewDatabaseAsync();
            var expected = await CountWorkersAsync(dbPath);
            ClearPoolFor(dbPath);

            DatabaseBackupService.RunDailyBackup(dbPath, externalFolder: null, retentionDays: 14);

            var backup = Directory
                .GetFiles(Path.Combine(Path.GetDirectoryName(dbPath)!, "Backups"), "*.db")
                .Single();

            Assert.Equal(expected, await CountWorkersAsync(backup));
        }

        [Fact]
        public async Task TheBackupPassesSqliteOwnIntegrityCheck()
        {
            var dbPath = await NewDatabaseAsync();

            var (localPath, _) = DatabaseBackupService.BackupNow(dbPath, null, 14);

            await using var conn = new SqliteConnection($"Data Source={localPath}");
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check";

            Assert.Equal("ok", (string?)await cmd.ExecuteScalarAsync());
        }

        [Fact]
        public async Task TakingABackupTwiceInTheSameDay_ReplacesItInsteadOfFailing()
        {
            // VACUUM INTO بيرفض يكتب على ملف موجود — لو الخدمة مامسحتش
            // القديم الأول، تاني ضغطة على "خد نسخة دلوقتي" كانت هتقع
            var dbPath = await NewDatabaseAsync();

            DatabaseBackupService.BackupNow(dbPath, null, 14);
            var (second, _) = DatabaseBackupService.BackupNow(dbPath, null, 14);

            Assert.True(File.Exists(second));
            Assert.True(await CountWorkersAsync(second) > 0);
        }

        [Fact]
        public async Task RestoringPutsTheDataBack_AndKeepsASafetyCopy()
        {
            // الاسترجاع آخر خط دفاع: لازم يشتغل، ولازم يسيب طريق رجوع
            var dbPath = await NewDatabaseAsync();
            var expected = await CountWorkersAsync(dbPath);

            var (backupPath, _) = DatabaseBackupService.BackupNow(dbPath, null, 14);

            // نخرب الحالية عشان نتأكد إن الاسترجاع فعلًا بيبدّلها
            ClearPoolFor(dbPath);
            File.WriteAllText(dbPath, "بيانات تالفة");

            var safety = DatabaseBackupService.RestoreBackup(dbPath, backupPath);

            Assert.Equal(expected, await CountWorkersAsync(dbPath));
            Assert.True(File.Exists(safety), "نسخة الأمان من البيانات القديمة لازم تفضل موجودة");
        }
    }
}
