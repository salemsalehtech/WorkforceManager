using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// تجهيز أي صورة بتتخزن جوه قاعدة البيانات — صور المنتجات وصور
    /// العمال. المكان الوحيد اللي بيصغّر ويضغط ويحوّل للعرض.
    ///
    /// الصورة اللي المستخدم بيختارها ممكن تكون 5 ميجا من موبايل — وهي
    /// معروضة في مربع 44 بكسل. عشان كده بنصغّرها ونضغطها قبل ما تتخزن:
    /// الصورة بتتحفظ جوه قاعدة البيانات، وقاعدة البيانات بتتنسخ احتياطيًا
    /// يوميًا، فكل ميجا زيادة بتتضاعف في كل نسخة.
    ///
    /// بنستخدم تشفير WPF الأصلي (BitmapFrame / JpegBitmapEncoder) —
    /// مفيش أي مكتبة صور خارجية اتضافت للمشروع.
    /// </summary>
    public static class StoredImageHelper
    {
        /// <summary>
        /// أقصى بُعد للصورة المخزّنة بالبكسل. 256 كفاية بكتير لمربع 44
        /// بكسل (حتى على شاشة عالية الدقة)، وبتخلي الملف عشرات الكيلو.
        /// </summary>
        private const int MaxDimension = 256;

        /// <summary>جودة الضغط — 85 اتفاق متوازن بين الوضوح والحجم</summary>
        private const int JpegQuality = 85;

        /// <summary>امتدادات الصور المقبولة في نافذة الاختيار</summary>
        public const string FileDialogFilter =
            "ملفات الصور|*.jpg;*.jpeg;*.png;*.bmp;*.webp|كل الملفات|*.*";

        /// <summary>
        /// يقرا صورة من ملف، يصغّرها ويضغطها، ويرجّعها بايتات جاهزة للتخزين.
        /// بيرمي استثناء برسالة عربية لو الملف مش صورة صالحة.
        /// </summary>
        public static byte[] LoadForStorage(string filePath)
        {
            BitmapFrame source;
            try
            {
                using var stream = File.OpenRead(filePath);
                source = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "مقدرتش أقرا الصورة دي — اتأكد إنها ملف صورة سليم (jpg / png).", ex);
            }

            // التصغير بيحافظ على النسبة، ومبيكبّرش الصور الصغيرة أصلاً
            var scale = Math.Min(
                (double)MaxDimension / source.PixelWidth,
                (double)MaxDimension / source.PixelHeight);

            BitmapSource prepared = scale < 1
                ? new TransformedBitmap(source, new ScaleTransform(scale, scale))
                : source;

            var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
            encoder.Frames.Add(BitmapFrame.Create(prepared));

            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }

        /// <summary>
        /// يحوّل البايتات المخزّنة لصورة جاهزة للعرض في الواجهة.
        /// بيرجّع null لو مفيش صورة أو البيانات تالفة — والشاشة ساعتها
        /// بتعرض دايرة الحروف الأولى بدلها.
        /// </summary>
        public static ImageSource? ToImageSource(byte[]? data)
        {
            if (data is null || data.Length == 0) return null;

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = new MemoryStream(data);
                // OnLoad بيقفل الـ stream فورًا بعد التحميل — من غيره الملف
                // بيفضل مقفول والذاكرة بتتراكم مع كل إعادة تحميل للقائمة
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze(); // آمن للاستخدام من أي خيط وأسرع في العرض
                return image;
            }
            catch
            {
                // بيانات صورة تالفة مش سبب كافي إن الشاشة كلها تقع
                return null;
            }
        }
    }
}
