using System.Windows;
using System.Windows.Input;
// alias صريح للـ enum عشان اسم الخاصية HourlyRole في الكلاس ميحجبش النوع
using HourlyRoleEnum = WorkforceManager.Core.Enums.HourlyRole;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// نافذة إضافة/تعديل عامل. بتتحقق من الاسم بس (الإجباري الوحيد) —
    /// التحقق الأعمق (زي تفرد الكود الوظيفي) مسؤولية WorkerManagementService
    /// عشان القاعدة تتطبق من أي مكان مش من الشاشة دي بس.
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
        public string? EmployeeCode => string.IsNullOrWhiteSpace(CodeBox.Text) ? null : CodeBox.Text.Trim();
        public string? PhoneNumber => string.IsNullOrWhiteSpace(PhoneBox.Text) ? null : PhoneBox.Text.Trim();
        public DateTime? HireDate => HireDatePicker.SelectedDate;
        public string? SkillsNotes => string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();
        public HourlyRoleEnum? HourlyRole => HourlyRoleBox.SelectedValue as HourlyRoleEnum?;

        /// <summary>سعر اليومية بالجنيه (مضمون رقم غير سالب بعد Save_Click)</summary>
        public decimal DailyWageEgp =>
            decimal.TryParse(WageBox.Text.Trim(), out var w) ? w : 0m;

        /// <summary>تعبئة الفورم ببيانات عامل موجود (وضع التعديل)</summary>
        public void LoadWorker(string fullName, string? code, string? phone, DateTime? hireDate,
            string? notes, HourlyRoleEnum? hourlyRole, decimal dailyWageEgp)
        {
            NameBox.Text = fullName;
            CodeBox.Text = code ?? "";
            PhoneBox.Text = phone ?? "";
            HireDatePicker.SelectedDate = hireDate;
            NotesBox.Text = notes ?? "";
            HourlyRoleBox.SelectedValue = hourlyRole;
            WageBox.Text = dailyWageEgp > 0 ? dailyWageEgp.ToString("0.##") : "";
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
