using System.Windows;
using System.Windows.Input;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// نافذة إضافة/تعديل مرحلة تصنيع. بتتحقق من الاسم واليومية (رقم موجب)
    /// قبل الإغلاق — قواعد التفرد داخل المنتج مسؤولية ProductManagementService.
    /// </summary>
    public partial class StageEditDialog : Window
    {
        public StageEditDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => NameBox.Focus();
        }

        // ------- القيم اللي الشاشة الأم بتقرأها بعد الحفظ -------

        public string StageName => NameBox.Text.Trim();

        /// <summary>اليومية بتتحقق في Save_Click فمضمون إنها رقم موجب هنا</summary>
        public int PiecesPerWorkday => int.Parse(QuotaBox.Text.Trim());

        /// <summary>null = ترتيب تلقائي (آخر ترتيب + 1)</summary>
        public int? SortOrder =>
            int.TryParse(SortOrderBox.Text.Trim(), out var order) ? order : null;

        /// <summary>تعبئة الفورم ببيانات مرحلة موجودة (وضع التعديل)</summary>
        public void LoadStage(string name, int piecesPerWorkday, int sortOrder)
        {
            NameBox.Text = name;
            QuotaBox.Text = piecesPerWorkday.ToString();
            SortOrderBox.Text = sortOrder.ToString();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                ErrorText.ShowError("اسم المرحلة مطلوب");
                NameBox.Focus();
                return;
            }

            if (!int.TryParse(QuotaBox.Text.Trim(), out var quota) || quota <= 0)
            {
                ErrorText.ShowError("اليومية لازم تكون رقم صحيح موجب (مثال: 5000)");
                QuotaBox.Focus();
                return;
            }

            // الترتيب اختياري، لكن لو اتكتب لازم يكون رقم صحيح
            if (!string.IsNullOrWhiteSpace(SortOrderBox.Text) &&
                !int.TryParse(SortOrderBox.Text.Trim(), out _))
            {
                ErrorText.ShowError("الترتيب لازم يكون رقم صحيح (أو سيبه فاضي للترتيب التلقائي)");
                SortOrderBox.Focus();
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
