using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using WorkforceManager.UI.ViewModels;
// alias صريح للـ enum عشان اسم الخاصية HourlyRole في الكلاس ميحجبش النوع
using HourlyRoleEnum = WorkforceManager.Core.Enums.HourlyRole;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// نافذة إضافة/تعديل عامل. بتتحقق من الاسم بس (الإجباري الوحيد) —
    /// أي تحقق أعمق مسؤولية WorkerManagementService عشان القاعدة تتطبق
    /// من أي مكان مش من الشاشة دي بس.
    /// </summary>
    public partial class WorkerEditDialog : Window
    {
        /// <summary>خيار نوع الحساب في القائمة (Role == null = عامل إنتاج بالقطعة)</summary>
        private record HourlyRoleOption(HourlyRoleEnum? Role, string Display);

        public WorkerEditDialog()
        {
            InitializeComponent();

            HourlyRoleBox.ItemsSource = new[]
            {
                new HourlyRoleOption(null, "إنتاج (بالقطعة)"),
                new HourlyRoleOption(HourlyRoleEnum.Training, "تحت التدريب (بالساعة)"),
                new HourlyRoleOption(HourlyRoleEnum.Racking, "رص (بالساعة)"),
                new HourlyRoleOption(HourlyRoleEnum.Quality, "جودة (بالساعة)"),
                new HourlyRoleOption(HourlyRoleEnum.Other, "دور آخر (بالساعة)")
            };
            HourlyRoleBox.SelectedIndex = 0; // الافتراضي: عامل إنتاج

            // التركيز على خانة الاسم فورًا — أسرع في الإدخال المتكرر
            Loaded += (_, _) => NameBox.Focus();
        }

        // ------- القيم اللي الشاشة الأم بتقرأها بعد الحفظ -------

        public string WorkerName => NameBox.Text.Trim();
        public string? PhoneNumber => string.IsNullOrWhiteSpace(PhoneBox.Text) ? null : PhoneBox.Text.Trim();
        public DateTime? HireDate => HireDatePicker.SelectedDate;
        public HourlyRoleEnum? HourlyRole => HourlyRoleBox.SelectedValue as HourlyRoleEnum?;

        /// <summary>سعر اليومية بالجنيه (مضمون رقم غير سالب بعد Save_Click)</summary>
        public decimal DailyWageEgp =>
            decimal.TryParse(WageBox.Text.Trim(), out var w) ? w : 0m;

        /// <summary>
        /// صورة العامل بعد الحفظ (null = مفيش صورة أو المستخدم شالها).
        /// بتبقى مصغّرة ومضغوطة جاهزة للتخزين.
        /// </summary>
        public byte[]? PhotoData { get; private set; }

        /// <summary>
        /// اتغيّرت الصورة في الجلسة دي؟ الشاشة الأم بتحفظ الصورة بس لو
        /// اتغيّرت فعلاً — عشان تعديل الاسم لوحده ميعملش كتابة زيادة
        /// للصورة كلها في قاعدة البيانات.
        /// </summary>
        public bool PhotoChanged { get; private set; }

        /// <summary>تعبئة الفورم ببيانات عامل موجود (وضع التعديل)</summary>
        public void LoadWorker(string fullName, string? phone, DateTime? hireDate,
            HourlyRoleEnum? hourlyRole, decimal dailyWageEgp, byte[]? photoData = null)
        {
            NameBox.Text = fullName;
            PhoneBox.Text = phone ?? "";
            HireDatePicker.SelectedDate = hireDate;
            HourlyRoleBox.SelectedValue = hourlyRole;
            WageBox.Text = dailyWageEgp > 0 ? dailyWageEgp.ToString("0.##") : "";

            PhotoData = photoData;
            PhotoChanged = false; // التحميل مش تغيير
            ShowPhotoPreview();
        }

        /// <summary>يعرض الصورة الحالية أو أيقونة "مفيش صورة"</summary>
        private void ShowPhotoPreview()
        {
            var source = StoredImageHelper.ToImageSource(PhotoData);

            PhotoPreview.Source = source;
            PhotoPreview.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
            NoPhotoIcon.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
            RemovePhotoButton.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void PickPhoto_Click(object sender, RoutedEventArgs e)
        {
            var picker = new OpenFileDialog
            {
                Title = "اختار صورة العامل",
                Filter = StoredImageHelper.FileDialogFilter,
                CheckFileExists = true
            };

            if (picker.ShowDialog(this) != true) return;

            try
            {
                // التصغير والضغط بيحصلوا هنا — اللي بيتخزن صورة صغيرة مش الأصل
                PhotoData = StoredImageHelper.LoadForStorage(picker.FileName);
                PhotoChanged = true;
                ShowPhotoPreview();

                ErrorText.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                ErrorText.Text = ex.Message;
                ErrorText.Visibility = Visibility.Visible;
            }
        }

        private void RemovePhoto_Click(object sender, RoutedEventArgs e)
        {
            PhotoData = null;
            PhotoChanged = true;
            ShowPhotoPreview();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // الاسم هو الحقل الإجباري الوحيد — من غيره مفيش حفظ
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                ErrorText.Text = "اسم العامل مطلوب";
                ErrorText.Visibility = Visibility.Visible;
                NameBox.Focus();
                return;
            }

            // سعر اليومية لو متكتب لازم يكون رقم غير سالب
            var wageText = WageBox.Text.Trim();
            if (wageText.Length > 0 && (!decimal.TryParse(wageText, out var wage) || wage < 0))
            {
                ErrorText.Text = "سعر اليومية لازم يكون رقم موجب (أو سيبه فاضي)";
                ErrorText.Visibility = Visibility.Visible;
                WageBox.Focus();
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
