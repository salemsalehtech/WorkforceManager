using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// أيقونة النافذة (شريط العنوان والـ Taskbar) من شعار البرنامج
    /// المرفوع في الإعدادات (AppSettingsStore.AppLogoPath) — منفصل عن
    /// شعار التقارير (LogoPath)، شوف توثيقهم هناك.
    ///
    /// من غير شعار مرفوع، أو لو الملف اتشال/اتنقل بعد الرفع، النافذة
    /// بترجع لأيقونتها الافتراضية (Assets/app.ico) بدل ما تكسر أو تفضل
    /// عالقة على آخر شعار كان متظبّط.
    ///
    /// **الكلاس ده هو المكان الوحيد اللي بيحطّ أيقونة نافذة.** الـ XAML
    /// مابقاش فيه <c>Icon="..."</c> خالص (كان في LoginWindow وMainWindow):
    /// السمة دي بتتحمّل جوّه InitializeComponent من غير أي معالجة أخطاء،
    /// وأي فشل لحظي في قراءة المورد بيرمي XamlParseException **بيقتل بناء
    /// النافذة كلها** — وده اللي حصل فعلًا (crash.txt: فشل تحميل
    /// assets/app.ico وشاشة الدخول مافتحتش أصلًا). وكمان كان تحميل مكرّر
    /// بلا فايدة: ApplyTo بتكتب فوقها على طول بعد InitializeComponent.
    /// أيقونة الـ exe نفسها (ApplicationIcon في الـ csproj) حاجة تانية
    /// خالص على مستوى Win32 ومش متأثرة بده.
    /// </summary>
    public static class AppIcon
    {
        /// <summary>
        /// الأيقونة الافتراضية — بتتحمّل مرة واحدة عند أول استخدام.
        ///
        /// <see cref="Lazy{T}"/> مش حقل static عادي عن قصد: الحقل العادي
        /// بيتحمّل في مُهيّئ النوع، وأي استثناء هناك بيتلفّ في
        /// TypeInitializationException بيفضل يترمي **كل مرة** يتلمس فيها
        /// الكلاس بعد كده — يعني فشل لحظي واحد بيقفل الأيقونات للأبد في
        /// الجلسة دي. والـ null نتيجة مقبولة: نافذة من غير أيقونة أحسن من
        /// نافذة مابتفتحش.
        /// </summary>
        private static readonly Lazy<BitmapImage?> DefaultIcon = new(() =>
        {
            try
            {
                var icon = new BitmapImage(new Uri("pack://application:,,,/Assets/app.ico"));
                icon.Freeze();
                return icon;
            }
            catch
            {
                return null;
            }
        });

        public static void ApplyTo(Window window)
        {
            var path = Data.AppSettingsStore.Load().AppLogoPath;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                window.Icon = DefaultIcon.Value;
                return;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();

                window.Icon = bitmap;
            }
            catch
            {
                window.Icon = DefaultIcon.Value;
            }
        }
    }
}
