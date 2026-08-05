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
