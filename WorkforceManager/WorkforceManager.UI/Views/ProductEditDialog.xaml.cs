using System.Windows;
using System.Windows.Input;

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
        public string? ProductCode => string.IsNullOrWhiteSpace(CodeBox.Text) ? null : CodeBox.Text.Trim();
        public string? ProductDescription => string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim();

        /// <summary>تعبئة الفورم ببيانات منتج موجود (وضع التعديل)</summary>
        public void LoadProduct(string name, string? code, string? description)
        {
            NameBox.Text = name;
            CodeBox.Text = code ?? "";
            DescriptionBox.Text = description ?? "";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                ErrorText.Text = "اسم المنتج مطلوب";
                ErrorText.Visibility = Visibility.Visible;
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
