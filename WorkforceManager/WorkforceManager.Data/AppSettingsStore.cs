using System.Text.Json;

namespace WorkforceManager.Data
{
    /// <summary>إعدادات البرنامج القابلة للتغيير من شاشة الإعدادات</summary>
    public class AppSettings
    {
        /// <summary>
        /// مجلد النسخ الاحتياطي الخارجي (فلاشة / هارد تاني / مجلد شبكة).
        /// null = النسخ الخارجي متوقف. الفكرة: النسخة المحلية على نفس
        /// الهارد متحميش من تلف الهارد نفسه — النسخة الخارجية بتحمي.
        /// </summary>
        public string? ExternalBackupFolder { get; set; }

        /// <summary>
        /// آخر مرة المدير راجع تقييمات مهارات العمال.
        ///
        /// البرنامج بيفكّره كل شهر — من غير التاريخ ده التذكير هيظهر كل
        /// يوم ويتحوّل لضوضاء المستخدم بيتعلّم يتجاهلها.
        /// null = عمره ما راجع (بيتعرض التذكير أول ما يبقى فيه اقتراحات).
        /// </summary>
        public DateTime? LastSkillReviewAt { get; set; }

        // ------- إعدادات النسخ الاحتياطي -------

        /// <summary>
        /// عدد أيام الاحتفاظ بالنسخ القديمة. النسخة بتتاخد كل تشغيل،
        /// فمن غير حد أقصى المجلد بيكبر للأبد.
        /// </summary>
        public int BackupRetentionDays { get; set; } = DefaultBackupRetentionDays;

        /// <summary>
        /// النسخة التلقائية عند التشغيل شغالة؟
        ///
        /// إيقافها قرار المستخدم، بس هو بيتحمّل نتيجته — عشان كده
        /// الشاشة بتحذّر لما يقفلها بدل ما تعدّيها بصمت.
        /// </summary>
        public bool AutoBackupOnStartup { get; set; } = true;

        // ------- المظهر -------

        /// <summary>
        /// الوضع الليلي شغال؟ بيتطبّق عند فتح البرنامج.
        ///
        /// **التبديل محتاج إعادة تشغيل**: كل الشاشات بتقرا الألوان بـ
        /// StaticResource (بتتحل مرة واحدة وقت التحميل)، وتحويلها كلها
        /// لـ DynamicResource تغيير ميكانيكي في كل ملف XAML — وده هيتعمل
        /// مع إعادة تصميم الواجهة اللي هتلمس نفس الملفات أصلاً.
        /// </summary>
        public bool DarkMode { get; set; }

        /// <summary>الافتراضي: أسبوعين — كفاية للرجوع من غلطة، ومش بيكبّر المجلد</summary>
        public const int DefaultBackupRetentionDays = 14;
    }

    /// <summary>
    /// قراءة وحفظ إعدادات البرنامج من ملف JSON بسيط جنب قاعدة البيانات.
    /// أي خطأ في القراءة (ملف تالف/ناقص) بيرجع إعدادات افتراضية بدل
    /// ما يكسر تشغيل البرنامج.
    /// </summary>
    public static class AppSettingsStore
    {
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        /// <summary>يقرأ الإعدادات (المسار الافتراضي أو مسار مخصص للاختبارات)</summary>
        public static AppSettings Load(string? settingsPath = null)
        {
            var path = settingsPath ?? AppPaths.SettingsPath;
            if (!File.Exists(path)) return new AppSettings();

            try
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
            }
            catch
            {
                // ملف إعدادات تالف عمره ما يمنع البرنامج من الفتح — بنرجع للافتراضي
                return new AppSettings();
            }
        }

        /// <summary>يحفظ الإعدادات (بينشئ المجلد لو مش موجود)</summary>
        public static void Save(AppSettings settings, string? settingsPath = null)
        {
            var path = settingsPath ?? AppPaths.SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, WriteOptions));
        }
    }
}
