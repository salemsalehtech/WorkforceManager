using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// لوجو WMS. مرسوم بـ XAML عشان يفضل حاد في أي مقاس ويتلوّن مع
    /// الثيم لوحده — حطه في أي مكان وحدّد له Width/Height وبس.
    ///
    /// لو المستخدم رفع شعار مصنعه من الإعدادات (AppSettingsStore.LogoPath)،
    /// الشعار ده بيحل محل حروف "WMS" جوّه نفس الإطار الأسود بالحدّ
    /// الدهبي — مش إطار جديد، ومش شعارين مع بعض.
    /// </summary>
    public partial class WmsLogo : UserControl
    {
        public WmsLogo()
        {
            InitializeComponent();
            Loaded += (_, _) => Refresh();
        }

        /// <summary>
        /// بيعيد قراءة شعار الإعدادات ويبدّل بيه محل "WMS". من غير شعار
        /// مرفوع، أو لو الملف اتشال/اتنقل من مكانه بعد الرفع، بيرجع
        /// لـ"WMS" تلقائيًا بدل ما يكسر الشاشة.
        /// </summary>
        public void Refresh()
        {
            var path = Data.AppSettingsStore.Load().AppLogoPath;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                ShowDefault();
                return;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new System.Uri(path, System.UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();

                CustomMark.Source = bitmap;
                CustomMark.Visibility = Visibility.Visible;
                DefaultMark.Visibility = Visibility.Collapsed;
            }
            catch
            {
                // ملف تالف أو مش صورة فعلاً
                ShowDefault();
            }
        }

        private void ShowDefault()
        {
            CustomMark.Visibility = Visibility.Collapsed;
            DefaultMark.Visibility = Visibility.Visible;
        }
    }
}
