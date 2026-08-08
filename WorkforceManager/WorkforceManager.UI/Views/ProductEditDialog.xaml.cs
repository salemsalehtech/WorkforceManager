using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// نافذة إضافة/تعديل منتج. بتتحقق من الاسم بس (الإجباري الوحيد) —
    /// أي قواعد أعمق مسؤولية ProductManagementService.
    /// </summary>
    public partial class ProductEditDialog : Window
    {
        public ProductEditDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => NameBox.Focus();
        }

        // ------- القيم اللي الشاشة الأم بتقرأها بعد الحفظ -------

        public string ProductName => NameBox.Text.Trim();
        public string? ProductDescription => string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim();

        /// <summary>
        /// صورة المنتج بعد الحفظ (null = مفيش صورة أو المستخدم شالها).
        /// بتبقى مصغّرة ومضغوطة جاهزة للتخزين.
        /// </summary>
        public byte[]? ImageData { get; private set; }

        /// <summary>
        /// اتغيّرت الصورة في الجلسة دي؟ الشاشة الأم بتحفظ الصورة بس لو
        /// اتغيّرت فعلاً — عشان تعديل الاسم لوحده ميعملش كتابة زيادة
        /// للصورة كلها في قاعدة البيانات.
        /// </summary>
        public bool ImageChanged { get; private set; }

        /// <summary>تعبئة الفورم ببيانات منتج موجود (وضع التعديل)</summary>
        public void LoadProduct(string name, string? description, byte[]? imageData = null)
        {
            NameBox.Text = name;
            DescriptionBox.Text = description ?? "";

            ImageData = imageData;
            ImageChanged = false; // التحميل مش تغيير
            ShowImagePreview();
        }

        /// <summary>يعرض الصورة الحالية أو أيقونة "مفيش صورة"</summary>
        private void ShowImagePreview()
        {
            var source = StoredImageHelper.ToImageSource(ImageData);

            ImagePreview.Source = source;
            ImagePreview.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
            NoImageIcon.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
            RemoveImageButton.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void PickImage_Click(object sender, RoutedEventArgs e)
        {
            var picker = new OpenFileDialog
            {
                Title = "اختار صورة المنتج",
                Filter = StoredImageHelper.FileDialogFilter,
                CheckFileExists = true
            };

            if (picker.ShowDialog(this) != true) return;

            try
            {
                // التصغير والضغط بيحصلوا هنا — اللي بيتخزن صورة صغيرة مش الأصل
                ImageData = StoredImageHelper.LoadForStorage(picker.FileName);
                ImageChanged = true;
                ShowImagePreview();

                ErrorText.ClearError();
            }
            catch (Exception ex)
            {
                ErrorText.ShowError(ex.Message);
            }
        }

        private void RemoveImage_Click(object sender, RoutedEventArgs e)
        {
            ImageData = null;
            ImageChanged = true;
            ShowImagePreview();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                ErrorText.ShowError("اسم المنتج مطلوب");
                NameBox.Focus();
                return;
            }

            DialogResult = true;
        }

        /// <summary>النافذة بلا إطار نظام — السحب من الشريط العلوي</summary>
        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
    }
}
