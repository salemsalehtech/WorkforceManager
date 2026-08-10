using System.Globalization;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// النسخ الاحتياطي لازم يشتغل صح مهما كان تقويم ويندوز.
    ///
    /// العطل اللي الاختبارات دي بتقفله: اسم ملف النسخة كان بيتكتب
    /// بتقويم النظام (سلسلة نصية مُنسّقة بالثقافة الحالية) وبيتقرا تاني
    /// بالتقويم الميلادي وقت التنظيف. على ويندوز متظبط على تقويم هجري
    /// النسخة كانت بتتسمّى "workforce_1448-02-28.db"، والتنظيف بيقراها
    /// **سنة ميلادية 1448** — أقدم من أي مدة احتفاظ — فيمسحها بعد
    /// ثواني من أخدها.
    ///
    /// النتيجة كانت أسوأ من "مفيش تنظيف": مفيش **نسخ** خالص، والمستخدم
    /// مش هيكتشف ده غير يوم ما يحتاج يرجّع بيانات.
    /// </summary>
    public class BackupCultureTests : IDisposable
    {
        private readonly CultureInfo _original = CultureInfo.CurrentCulture;
        private readonly List<string> _folders = new();

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _original;

            foreach (var folder in _folders)
                try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { /* مؤقت */ }
        }

        private string NewFolder()
        {
            var folder = Path.Combine(Path.GetTempPath(), $"wm-backup-{Guid.NewGuid():N}");
            Directory.CreateDirectory(folder);
            _folders.Add(folder);
            return folder;
        }

        /// <summary>يعمل قاعدة بيانات وهمية ويرجّع مسارها</summary>
        private string NewDatabase(string folder)
        {
            var path = Path.Combine(folder, "workforce.db");
            File.WriteAllText(path, "not-a-real-database");
            return path;
        }

        [Theory]
        [InlineData("ar-EG")]  // ميلادي — الحالة العادية
        [InlineData("ar-SA")]  // أم القرى — التقويم الهجري
        public void TheDailyBackupSurvivesCleanup_WhateverTheWindowsCalendar(string cultureName)
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);

            var folder = NewFolder();
            var dbPath = NewDatabase(folder);

            // النسخة بتتاخد، وبعدها التنظيف بيمشي على نفس المجلد
            DatabaseBackupService.RunDailyBackup(dbPath, externalFolder: null, retentionDays: 14);

            var backups = Directory.GetFiles(Path.Combine(folder, "Backups"), "*.db");

            Assert.Single(backups);
        }

        [Fact]
        public void TheBackupFileNameIsGregorian_NotTheSystemCalendar()
        {
            // الاسم معرّف للماكينة مش نص للعرض: التنظيف بيقراه تاني،
            // فلازم يبقى بنفس التقويم اللي بيتقرا بيه
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            var folder = NewFolder();
            var dbPath = NewDatabase(folder);

            DatabaseBackupService.RunDailyBackup(dbPath, externalFolder: null, retentionDays: 14);

            var name = Path.GetFileNameWithoutExtension(
                Directory.GetFiles(Path.Combine(folder, "Backups"), "*.db").Single());

            var datePart = name["workforce_".Length..];

            Assert.True(
                DateTime.TryParseExact(datePart, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed),
                $"اسم النسخة \"{name}\" مش بصيغة تاريخ ميلادي");

            Assert.Equal(DateTime.Today, parsed);
        }

        [Fact]
        public void ABackupFromLastYear_IsStillCleanedUp()
        {
            // التنظيف نفسه لازم يفضل شغال — الإصلاح مش المفروض يوقفه
            var folder = NewFolder();
            var dbPath = NewDatabase(folder);

            var backupsFolder = Path.Combine(folder, "Backups");
            Directory.CreateDirectory(backupsFolder);

            var old = Path.Combine(backupsFolder,
                "workforce_" + DateTime.Today.AddDays(-400).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".db");
            File.WriteAllText(old, "قديمة");

            DatabaseBackupService.RunDailyBackup(dbPath, externalFolder: null, retentionDays: 14);

            Assert.False(File.Exists(old));
            Assert.Single(Directory.GetFiles(backupsFolder, "*.db")); // نسخة النهارده بس
        }

        [Fact]
        public void TheSafetyCopyBeforeARestore_IsNeverCleanedUp()
        {
            // نسخة الأمان دي آخر خط رجوع بعد استرجاع غلط — اسمها مش
            // بصيغة التاريخ اليومية عن قصد عشان التنظيف يتخطاها
            var folder = NewFolder();
            var dbPath = NewDatabase(folder);

            var backupsFolder = Path.Combine(folder, "Backups");
            Directory.CreateDirectory(backupsFolder);

            var safety = Path.Combine(backupsFolder, "workforce_before_restore_2020-01-01_101010.db");
            File.WriteAllText(safety, "أمان");

            DatabaseBackupService.RunDailyBackup(dbPath, externalFolder: null, retentionDays: 14);

            Assert.True(File.Exists(safety));
        }
    }
}
