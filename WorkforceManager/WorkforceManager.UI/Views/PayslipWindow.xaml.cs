using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// نافذة معاينة وطباعة قسيمة أجر عامل. بتعرض القسيمة على الشاشة
    /// (معاينة) وبتطبعها على أي طابعة أو "Microsoft Print to PDF" —
    /// من غير أي مكتبة خارجية.
    /// </summary>
    public partial class PayslipWindow : Window
    {
        public PayslipWindow(PayslipData data)
        {
            InitializeComponent();
            DataContext = data;
        }

        /// <summary>
        /// يرسم بطاقة القسيمة لصورة عالية الدقة عشان نطبعها كصورة بدل ما
        /// نطبع العنصر مباشرة. السبب: الطباعة/الرسم خارج الشاشة لعنصر اتجاهه
        /// من اليمين لليسار (RTL) بتطلع الكلام معكوس أفقيًا (مقلوب زي المراية)
        /// — دي مشكلة معروفة في WPF (الشاشة بتعوّض الانعكاس، لكن الرسم خارجها لأ).
        /// فبنعكس الصورة أفقيًا مرة تانية عشان ترجع مظبوطة، وبعدها نطبعها.
        /// </summary>
        private BitmapSource RenderPayslip(double dpi)
        {
            var element = PrintArea;
            element.UpdateLayout();
            var w = element.ActualWidth;
            var h = element.ActualHeight;

            var rtb = new RenderTargetBitmap(
                (int)Math.Ceiling(w * dpi / 96.0),
                (int)Math.Ceiling(h * dpi / 96.0),
                dpi, dpi, PixelFormats.Pbgra32);
            rtb.Render(element);

            // عكس أفقي (ScaleX = -1) لإلغاء انعكاس الـ RTL في الرسم خارج الشاشة
            return new TransformedBitmap(rtb, new ScaleTransform(-1, 1));
        }

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true) return;

            // نصوّر القسيمة بدقة عالية (300 نقطة/بوصة) عشان تطلع حادة، ونطبع
            // الصورة نفسها — بكده الكلام العربي بيطلع مظبوط مش معكوس.
            var bitmap = RenderPayslip(300);

            var image = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform, // يحافظ على النسبة ويتمركز
                FlowDirection = FlowDirection.LeftToRight
            };

            const double margin = 40.0;
            var availableWidth = dialog.PrintableAreaWidth - margin * 2;
            var availableHeight = dialog.PrintableAreaHeight - margin * 2;
            image.Measure(new Size(availableWidth, availableHeight));
            image.Arrange(new Rect(margin, margin, availableWidth, availableHeight));
            image.UpdateLayout();

            dialog.PrintVisual(image, "قسيمة أجر");
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
