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
    /// </summary>
    public static class AppIcon
    {
        private static readonly BitmapImage DefaultIcon =
            new(new Uri("pack://application:,,,/Assets/app.ico"));

        public static void ApplyTo(Window window)
        {
            var path = Data.AppSettingsStore.Load().AppLogoPath;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                window.Icon = DefaultIcon;
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
                window.Icon = DefaultIcon;
            }
        }
    }
}
