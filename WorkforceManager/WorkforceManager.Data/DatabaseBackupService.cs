using System.Globalization;
using Microsoft.Data.Sqlite;

namespace WorkforceManager.Data
{
    /// <summary>
    /// النسخ الاحتياطي لقاعدة البيانات (SQLite ملف واحد، فالنسخ = نسخ الملف):
    ///
    /// - نسخة يومية تلقائية عند بدء التطبيق (قبل أي Migration) في مجلد Backups
    ///   المحلي، مع حذف الأقدم من 30 يوم تلقائيًا.
    /// - نسخة خارجية اختيارية (فلاشة / هارد تاني / مجلد شبكة): النسخة المحلية
    ///   على نفس الهارد متحميش من تلف الهارد نفسه — الخارجية بتحمي. فشلها
    ///   (فلاشة مش موصلة مثلًا) عمره ما يعطل تشغيل البرنامج.
    /// - نسخة فورية بزرار "خد نسخة دلوقتي" من شاشة الإعدادات.
    /// - استرجاع نسخة: بياخد نسخة أمان من الحالية الأول، وبعدها بيستبدلها.
    /// </summary>
    public static class DatabaseBackupService
    {
        /// <summary>
        /// عدد أيام الاحتفاظ بالنسخ قبل حذف الأقدم تلقائيًا.
        ///
        /// بقى قابل للضبط من الإعدادات بدل ما يكون ثابت: مصنع بيتنقل فيه
        /// شغل كتير عايز مدة أطول، وجهاز مساحته ضيقة عايز أقصر. القيمة
        /// بتتمرّر من اللي بينادي عشان الخدمة تفضل ساكنة (static) من غير
        /// ما تقرا ملف الإعدادات بنفسها.
        /// </summary>
        private const int FallbackRetentionDays = 14;

        /// <summary>أقل مدة احتفاظ مسموح بيها — يوم واحد يعني نسخة واحدة بس</summary>
        public const int MinRetentionDays = 3;

        /// <summary>أقصى مدة — بعد كده المجلد بيكبر من غير فايدة</summary>
        public const int MaxRetentionDays = 180;

        /// <summary>بادئة اسم ملف النسخة الاحتياطية (يتبعها التاريخ بصيغة yyyy-MM-dd)</summary>
        private const string BackupPrefix = "workforce_";

        /// <summary>
        /// النسخة اليومية التلقائية عند بدء التطبيق: مرة واحدة في اليوم مهما
        /// اتفتح البرنامج، محليًا + خارجيًا لو فيه مجلد خارجي متفعّل.
        /// </summary>
        public static void RunDailyBackup(string dbPath, string? externalFolder = null, int? retentionDays = null)
        {
            if (!File.Exists(dbPath))
                return; // أول تشغيل للتطبيق: قاعدة البيانات لسه ما اتعملتش، مفيش حاجة نعمل لها باك أب

            var backupsFolder = LocalBackupsFolder(dbPath);
            Directory.CreateDirectory(backupsFolder);

            var todayBackupPath = Path.Combine(backupsFolder, TodayBackupName());
            if (!File.Exists(todayBackupPath))
            {
                Snapshot(dbPath, todayBackupPath);
            }

            CleanupOldBackups(backupsFolder, retentionDays);
            TryCopyToExternal(todayBackupPath, externalFolder, retentionDays);
        }

        /// <summary>
        /// نسخة فورية الآن (بتحدّث نسخة اليوم لو موجودة) — لزرار
        /// "خد نسخة دلوقتي". بيرجع مساري النسختين (الخارجية null لو متوقفة)،
        /// وبيرمي استثناء واضح لو المجلد الخارجي متفعّل لكن مش متاح —
        /// المستخدم ضغط الزرار بنفسه فلازم يعرف إن الخارجية ما اتعملتش.
        /// </summary>
        public static (string LocalPath, string? ExternalPath) BackupNow(string dbPath, string? externalFolder = null, int? retentionDays = null)
        {
            if (!File.Exists(dbPath))
                throw new InvalidOperationException("ملف قاعدة البيانات غير موجود");

            var backupsFolder = LocalBackupsFolder(dbPath);
            Directory.CreateDirectory(backupsFolder);

            var localPath = Path.Combine(backupsFolder, TodayBackupName());
            Snapshot(dbPath, localPath);
            CleanupOldBackups(backupsFolder, retentionDays);

            string? externalPath = null;
            if (!string.IsNullOrWhiteSpace(externalFolder))
            {
                if (!Directory.Exists(externalFolder))
                    throw new InvalidOperationException(
                        $"المجلد الخارجي غير متاح:\n{externalFolder}\n\nوصّل الفلاشة/القرص أو راجع المسار من الإعدادات. (النسخة المحلية اتاخدت عادي)");

                externalPath = Path.Combine(externalFolder, TodayBackupName());
                File.Copy(localPath, externalPath, overwrite: true);
                CleanupOldBackups(externalFolder, retentionDays);
            }

            return (localPath, externalPath);
        }

        /// <summary>
        /// استرجاع نسخة احتياطية: بياخد نسخة أمان من قاعدة البيانات الحالية
        /// الأول (workforce_before_restore_...) وبعدها بيستبدلها بالنسخة
        /// المختارة. بيرجع مسار نسخة الأمان. البرنامج لازم يعيد التشغيل بعدها.
        /// </summary>
        public static string RestoreBackup(string dbPath, string backupFilePath)
        {
            if (!File.Exists(backupFilePath))
                throw new InvalidOperationException("ملف النسخة الاحتياطية المختار غير موجود");

            var backupsFolder = LocalBackupsFolder(dbPath);
            Directory.CreateDirectory(backupsFolder);

            // نسخة أمان بختم وقت كامل — اسمها مش بصيغة التاريخ اليومية عمدًا
            // عشان التنظيف التلقائي ميمسحهاش (TryParseExact بيتخطاها).
            // بتتاخد **قبل** قفل الاتصالات: اللقطة محتاجة تقرا من القاعدة
            if (File.Exists(dbPath))
                Snapshot(dbPath, Path.Combine(backupsFolder,
                    BackupPrefix + "before_restore_"
                    + DateTime.Now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture) + ".db"));

            var safetyPath = Directory
                .GetFiles(backupsFolder, $"{BackupPrefix}before_restore_*.db")
                .OrderByDescending(File.GetCreationTimeUtc)
                .First();

            // **هنا بس** القفل العام له لزوم: إحنا هنستبدل ملف قاعدة
            // البيانات نفسه، فأي اتصال مفتوح عليه لازم يقفل الأول.
            // والبرنامج بيعيد تشغيل نفسه بعد الاسترجاع على أي حال.
            SqliteConnection.ClearAllPools();

            File.Copy(backupFilePath, dbPath, overwrite: true);

            // **ملفات الـ WAL بتتشال مع القاعدة القديمة.**
            // القاعدة شغالة بوضع WAL: الكتابات بتقعد في ملف "-wal" جنب
            // القاعدة لحد ما تترحّل. لو سبناه بعد ما بدّلنا الملف
            // الأساسي، بيبقى فيه "-wal" بتاع قاعدة والقاعدة بتاعة نسخة
            // تانية خالص — والاتنين مش ليهم أي علاقة ببعض.
            //
            // SQLite بيتعرّف على ده من التوقيع اللي جوه الملف وبيتجاهله،
            // فمفيش تلف. بس سيبان ملف بحجم ميجات مالوش معنى جنب قاعدة
            // البيانات هو بالظبط الحاجة اللي بتخلي اللي بيفحص المجلد
            // بعد سنتين يقعد يخمّن. وشيله آمن: اللي كان فيه اتحفظ في
            // نسخة الأمان فوق (اللقطة بتقرا من خلال SQLite فبتشمل الـ WAL).
            //
            // **الفشل هنا بيتتجاهل**: ده تنظيف مش شرط صحة. لو فيه اتصال
            // لسه ماسك الملف، الاسترجاع نفسه خلص خلاص والبرنامج هيعيد
            // التشغيل — ومينفعش تنظيف يكسر آخر خط دفاع في البرنامج.
            foreach (var sidecar in new[] { dbPath + "-wal", dbPath + "-shm" })
            {
                try
                {
                    if (File.Exists(sidecar)) File.Delete(sidecar);
                }
                catch (IOException)
                {
                }
            }

            return safetyPath;
        }

        // ------- تفاصيل داخلية -------

        /// <summary>
        /// لقطة من قاعدة البيانات — **بطريقة SQLite نفسها، مش نسخ ملف**.
        ///
        /// VACUUM INTO بيطلب من SQLite يكتب نسخة متسقة من المحتوى، فهو
        /// اللي بيقرر إمتى الملف في حالة سليمة. نسخ الملف بإيدنا بيفترض
        /// إن مفيش كتابة شغالة ومفيش journal ناقص من إغلاق مفاجئ — وده
        /// افتراض بيفضل صح لحد أول انقطاع كهربا، واليوم ده بالذات هو
        /// اللي المستخدم بيحتاج فيه النسخة.
        ///
        /// النسخة كمان بتطلع أصغر (مفيش صفحات فاضية): 110 ميجا بدل 118
        /// على قاعدة 30 سنة — يعني مجلد النسخ كله بيخف.
        ///
        /// **الفشل بيرجع لنسخ الملف مش بيرمي**: نسخة مأخوذة بطريقة أقل
        /// ضمانًا أحسن بكتير من مفيش نسخة. لو القاعدة نفسها تالفة،
        /// VACUUM بيرفض — وساعتها نسخ الملف بيحفظ اللي فيها للفحص بدل
        /// ما البرنامج يقف.
        /// </summary>
        private static void Snapshot(string dbPath, string destinationPath)
        {
            if (File.Exists(destinationPath)) File.Delete(destinationPath);

            try
            {
                // **مفيش ClearAllPools هنا عن قصد.** VACUUM INTO بيقرا
                // من خلال SQLite نفسه، فمش محتاج الملف يكون ساكن —
                // والاستدعاء ده **عام على العملية كلها**: بيقفل الاتصالات
                // المتجمّعة لكل قاعدة بيانات مفتوحة في البرنامج، مش
                // بتاعتنا بس. يعني ضغطة "خد نسخة دلوقتي" كانت بتقطع
                // اتصالات الشاشات المفتوحة كلها.
                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();

                using var command = connection.CreateCommand();
                // الاقتباس المزدوج للعلامة: مسار فيه apostrophe بيكسر الأمر
                command.CommandText = $"VACUUM INTO '{destinationPath.Replace("'", "''")}'";
                command.ExecuteNonQuery();
            }
            catch
            {
                if (File.Exists(destinationPath)) File.Delete(destinationPath);
                File.Copy(dbPath, destinationPath, overwrite: true);
            }

        }

        private static string LocalBackupsFolder(string dbPath) =>
            Path.Combine(Path.GetDirectoryName(dbPath)!, "Backups");

        /// <summary>
        /// اسم ملف نسخة النهارده.
        ///
        /// **التاريخ بيتكتب بالتقويم الميلادي صراحةً، مش بتقويم ويندوز.**
        /// الاسم ده معرّف للماكينة مش نص للعرض: التنظيف بيقراه تاني بـ
        /// InvariantCulture عشان يعرف عمر النسخة.
        ///
        /// من غير التثبيت ده، ويندوز متظبط على تقويم هجري كان بيكتب
        /// "workforce_1448-02-28.db"، والتنظيف بيقراها **سنة ميلادية
        /// 1448** — أقدم من أي مدة احتفاظ — فبيمسح النسخة بعد ثواني من
        /// أخدها. النتيجة: المستخدم فاكر إن عنده نسخ يومية وهو مش عنده
        /// ولا واحدة، ومش هيكتشف ده غير يوم ما يحتاجها.
        /// </summary>
        private static string TodayBackupName() =>
            BackupPrefix + DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".db";

        /// <summary>
        /// النسخ الخارجي التلقائي (عند بدء التشغيل): أي فشل بيتتجاهل بصمت —
        /// فلاشة مش موصلة الصبح مينفعش تمنع البرنامج من الفتح. النسخ اليدوي
        /// من الزرار (BackupNow) هو اللي بيبلّغ عن الفشل بوضوح.
        /// </summary>
        private static void TryCopyToExternal(string localBackupPath, string? externalFolder, int? retentionDays)
        {
            if (string.IsNullOrWhiteSpace(externalFolder)) return;

            try
            {
                if (!Directory.Exists(externalFolder)) return;

                var target = Path.Combine(externalFolder, Path.GetFileName(localBackupPath));
                File.Copy(localBackupPath, target, overwrite: true);
                CleanupOldBackups(externalFolder, retentionDays);
            }
            catch
            {
                // النسخ الخارجي "أفضل جهد" — فشله عمره ما يكسر بدء التشغيل
            }
        }

        /// <summary>
        /// بيمسح أي نسخة احتياطية أقدم من فترة الاحتفاظ المحددة (RetentionDays).
        /// بيعتمد على التاريخ المكتوب في اسم الملف نفسه (workforce_yyyy-MM-dd.db)
        /// مش على File.GetCreationTime — لأن تاريخ إنشاء الملف على ويندوز غير
        /// موثوق (بيتغير عند النسخ/الاستعادة، وفيه ظاهرة File System Tunneling
        /// اللي بتخلي ملف جديد يورث تاريخ ملف قديم بنفس الاسم).
        /// </summary>
        private static void CleanupOldBackups(string backupsFolder, int? retentionDays)
        {
            var days = Math.Clamp(retentionDays ?? FallbackRetentionDays, MinRetentionDays, MaxRetentionDays);
            var cutoffDate = DateTime.Today.AddDays(-days);

            foreach (var file in Directory.GetFiles(backupsFolder, $"{BackupPrefix}*.db"))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var datePart = fileName.Substring(BackupPrefix.Length); // الجزء بعد البادئة = التاريخ

                // أي ملف اسمه مش متطابق مع الصيغة المتوقعة بنسيبه (مش بنمسحه) احتياطًا
                // — ده بيشمل نسخ الأمان before_restore بالقصد
                if (DateTime.TryParseExact(datePart, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var backupDate)
                    && backupDate < cutoffDate)
                {
                    File.Delete(file);
                }
            }
        }
    }
}
