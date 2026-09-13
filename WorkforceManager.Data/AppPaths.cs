namespace WorkforceManager.Data
{
    /// <summary>
    /// المسارات الثابتة لملفات البرنامج على الجهاز — كلها في مكان واحد
    /// عشان أي شاشة أو خدمة تستخدم نفس المسار من غير تكرار أو اختلاف.
    ///
    /// وضعان:
    /// - عادي (تطوير/مثبت): البيانات في %ProgramData%\WorkforceManager — برّه
    ///   مجلد البرنامج تمامًا، فأي تحديث أو إلغاء تثبيت بيلمس ملفات البرنامج
    ///   بس والبيانات تفضل. ومشتركة لكل مستخدمي الجهاز، فالمدير والمحاسب لو
    ///   بيدخلوا بحسابين ويندوز مختلفين بيشوفوا نفس البيانات.
    /// - محمول (Portable): لو فيه ملف "portable.marker" جنب الـ exe، البيانات
    ///   بتبقى في مجلد "Data" جنب البرنامج نفسه — فالمجلد كامل ومستقل، تنقله
    ///   لأي جهاز أو فلاشة يشتغل ببياناته من غير تثبيت.
    ///
    /// ملاحظة على الصلاحيات: مجلد ProgramData بيتعمل من ملف التثبيت بصلاحية
    /// كتابة لكل المستخدمين. SQLite محتاج يكتب في المجلد نفسه مش في الملف بس
    /// (ملفات -wal و -shm)، فصلاحية على الملف لوحده مش كفاية.
    /// </summary>
    public static class AppPaths
    {
        /// <summary>اسم ملف قاعدة البيانات — مستخدم في النقلة كمان</summary>
        private const string DbFileName = "workforce.db";

        private static readonly Lazy<string> _dataFolder = new(ResolveDataFolder);

        /// <summary>
        /// مجلد البيانات المشترك في الوضع المثبَّت — %ProgramData%\WorkforceManager
        /// </summary>
        public static string SharedDataRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "WorkforceManager");

        /// <summary>
        /// المسار اللي النسخ القديمة (قبل ملف التثبيت) كانت بتحط البيانات فيه —
        /// %LocalAppData%\WorkforceManager. مصدر النقلة لمرة واحدة.
        /// </summary>
        public static string LegacyDataRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkforceManager");

        /// <summary>
        /// القرار نفسه، معزول عن الجهاز عشان يتغطّى باختبار من غير ما الاختبار
        /// يلمس مجلدات حقيقية: فيه portable.marker → البيانات جنب البرنامج،
        /// غير كده → المجلد المشترك.
        /// </summary>
        public static string ResolveDataFolderFor(string exeDir, string sharedRoot) =>
            File.Exists(Path.Combine(exeDir, "portable.marker"))
                ? Path.Combine(exeDir, "Data")
                : sharedRoot;

        private static string ResolveDataFolder()
        {
            var folder = ResolveDataFolderFor(AppContext.BaseDirectory, SharedDataRoot);

            // الوضع المحمول مالوش علاقة بالنقلة: بياناته جنبه أصلاً
            if (folder != SharedDataRoot) return folder;

            // النسخ اللي قبل ملف التثبيت كانت بتحط البيانات في مجلد المستخدم.
            // من غير النقلة دي الجهاز ده هيفتح النسخة الجديدة ويلاقيها فاضية.
            // فشلها مايمنعش البرنامج من الفتح — أسوأ حالة إن المستخدم يسترجع
            // نسخة احتياطية بنفسه.
            try
            {
                MigrateLegacyData(LegacyDataRoot, folder);
            }
            catch
            {
                // مقصود: النقلة مساعدة، مش شرط للتشغيل
            }

            return folder;
        }

        /// <summary>
        /// بتنسخ بيانات مسار قديم لمسار جديد **مرة واحدة بس**: لو المسار الجديد
        /// فيه قاعدة بيانات خلاص مبتعملش حاجة، فالتحديث عمره ما يدوس على شغل
        /// المستخدم.
        ///
        /// نسخ مش نقل عن قصد — القديم يفضل موجود كخط رجوع لو حصل أي لخبطة.
        ///
        /// وبتنسخ ملفات -wal و -shm مع القاعدة: SQLite بيشتغل في وضع WAL،
        /// يعني آخر عمليات مكتوبة ممكن تكون لسه في ملف -wal لوحده. نسخ الـ .db
        /// بس بيسيبها ورا. (نفس السبب اللي خلّى النسخ الاحتياطي يستخدم
        /// VACUUM INTO بدل File.Copy.)
        /// </summary>
        /// <returns>true لو النقلة حصلت فعلًا</returns>
        public static bool MigrateLegacyData(string legacyFolder, string targetFolder)
        {
            if (File.Exists(Path.Combine(targetFolder, DbFileName))) return false;
            if (!File.Exists(Path.Combine(legacyFolder, DbFileName))) return false;

            Directory.CreateDirectory(targetFolder);

            foreach (var name in new[]
                     {
                         DbFileName,
                         DbFileName + "-wal",
                         DbFileName + "-shm",
                         "settings.json"
                     })
            {
                var source = Path.Combine(legacyFolder, name);
                if (File.Exists(source))
                    File.Copy(source, Path.Combine(targetFolder, name), overwrite: true);
            }

            return true;
        }

        /// <summary>مجلد بيانات البرنامج (يتحدد حسب الوضع: عادي أو محمول)</summary>
        public static string DataFolder => _dataFolder.Value;

        /// <summary>ملف قاعدة البيانات الرئيسي</summary>
        public static string DbPath => Path.Combine(DataFolder, DbFileName);

        /// <summary>مجلد النسخ الاحتياطية المحلية (جنب قاعدة البيانات)</summary>
        public static string BackupsFolder => Path.Combine(DataFolder, "Backups");

        /// <summary>ملف إعدادات البرنامج (JSON)</summary>
        public static string SettingsPath => Path.Combine(DataFolder, "settings.json");
    }
}
