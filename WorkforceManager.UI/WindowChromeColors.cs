using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WorkforceManager.UI
{
    /// <summary>
    /// بيلوّن **شريط عنوان النافذة نفسه** (اللي ويندوز بيرسمه) بألوان
    /// الثيم: خلفية بلون القايمة الجانبية والاسم بالدهبي.
    ///
    /// الشريط ده برّه WPF خالص — مش عنصر في الشجرة ومفيش Brush بيوصله،
    /// فكان بيفضل أبيض فوق برنامج كله أسود في الوضع الليلي. تغييره
    /// بيتم بـ DwmSetWindowAttribute، ودي بتاعة ويندوز مش بتاعة WPF.
    ///
    /// **الفشل هنا مقصود إنه صامت**: الخصائص دي موجودة من ويندوز 11
    /// (بناء 22000) وويندوز 10 بياخد الوضع الغامق بس. برنامج بيرفض
    /// يفتح عشان شريط العنوان مش بيتلوّن هيبقى تصرّف أسوأ من شريط
    /// أبيض.
    /// </summary>
    public static class WindowChromeColors
    {
        // الوضع الغامق لشريط العنوان — ويندوز 10 1809 فما فوق
        private const int UseImmersiveDarkMode = 20;

        // لون خلفية الشريط ولون نصه — ويندوز 11 (22000) فما فوق
        private const int CaptionColor = 35;
        private const int CaptionTextColor = 36;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>
        /// بيطبّق ألوان الثيم الحالي على شريط عنوان النافذة. بيتنادى
        /// أول ما الـ Handle يتعمل، وتاني مع كل تبديل ثيم.
        /// </summary>
        public static void Apply(Window window)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            var dark = IsDark(window);

            Set(handle, UseImmersiveDarkMode, dark ? 1 : 0);

            // الشريط بلون القايمة الجانبية عشان يبان امتداد للبرنامج
            // مش شريط غريب فوقه، والاسم بالدهبي زي هوية البرنامج
            if (Find(window, "SidebarColor") is { } caption)
                Set(handle, CaptionColor, ToBgr(caption));

            if (Find(window, "GoldColor") is { } text)
                Set(handle, CaptionTextColor, ToBgr(text));
        }

        /// <summary>
        /// الثيم غامق ولا فاتح — بيتقرا من لون الأرضية نفسه مش من
        /// الإعدادات، فمفيش مصدرين للحقيقة يفترقوا.
        /// </summary>
        private static bool IsDark(Window window)
        {
            if (Find(window, "GroundColor") is not { } ground) return false;

            // الإضاءة المدركة: الأخضر بيوزن أكتر من الأحمر والأزرق
            var luminance = (0.299 * ground.R + 0.587 * ground.G + 0.114 * ground.B) / 255;
            return luminance < 0.5;
        }

        private static Color? Find(Window window, string key) =>
            window.TryFindResource(key) is Color color ? color : null;

        /// <summary>DWM بيقرا اللون BGR مقلوب مش RGB</summary>
        private static int ToBgr(Color color) =>
            color.R | (color.G << 8) | (color.B << 16);

        private static void Set(IntPtr handle, int attribute, int value)
        {
            try
            {
                DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
            }
            catch (DllNotFoundException)
            {
                // ويندوز من غير dwmapi — شريط عنوان بلون النظام وخلاص
            }
            catch (EntryPointNotFoundException)
            {
                // نسخة أقدم من اللي فيها الخاصية دي
            }
        }
    }
}
