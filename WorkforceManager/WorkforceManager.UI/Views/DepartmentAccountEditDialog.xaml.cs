using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using WorkforceManager.UI.ViewModels;
using HourlyRoleEnum = WorkforceManager.Core.Enums.HourlyRole;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// نافذة إضافة/تعديل حساب إداري (مدير/رئيس قسم). بتتحقق من الاسم
    /// واسم الدخول بس — أي تحقق أعمق مسؤولية WorkerManagementService/
    /// AuthService عشان القاعدة تتطبق من أي مكان مش من الشاشة دي بس.
    /// </summary>
    public partial class DepartmentAccountEditDialog : Window
    {
        private record RoleOption(HourlyRoleEnum Role, string Display);

        /// <summary>وضع التعديل: كلمة المرور بقت اختيارية (سيبها فاضية = ماتغيّرش)</summary>
        private readonly bool _isEditMode;

        /// <summary>
        /// تعديل ذاتي (رئيس قسم بيعدّل بياناته هو): المسمّى وسعر
        /// اليومية مقفولين — الاتنين قرار إداري، مش حاجة الشخص يغيّرها
        /// لنفسه (تغيير مسمّاه لنفسه = ترقية ذاتية، وتغيير سعر يوميته
        /// = زيادة راتب لنفسه). الاسم والتليفون والصورة واسم الدخول
        /// وكلمة المرور كلهم فاضيين عادي.
        /// </summary>
        private readonly bool _restrictToSelf;

        /// <summary>اسم الدخول وقت فتح النافذة — عشان نعرف لو اتغيّر فعلاً وقت الحفظ</summary>
        private string _originalUsername = "";

        public DepartmentAccountEditDialog(
            bool isEditMode = false, bool restrictToSelf = false, bool hasOperationsPassword = false)
        {
            InitializeComponent();
            _isEditMode = isEditMode;
            _restrictToSelf = restrictToSelf;

            RoleBox.ItemsSource = new[]
            {
                new RoleOption(HourlyRoleEnum.DepartmentManager, "مدير قسم"),
                new RoleOption(HourlyRoleEnum.DepartmentHead, "رئيس قسم")
            };
            RoleBox.SelectedIndex = 0;
            RoleBox.IsEnabled = !restrictToSelf;

            if (_isEditMode)
            {
                PasswordLabel.Text = "كلمة مرور جديدة";
                PasswordHint.Visibility = Visibility.Visible;

                OperationsPasswordLabel.Text = "كلمة سر عمليات جديدة (اختياري)";
                OperationsPasswordHint.Visibility = Visibility.Visible;
            }

            // تعديل ذاتي: أي تغيير في اسم الدخول أو كلمة المرور أو كلمة
            // سر العمليات لازم يتأكد بكلمة المرور/كلمة السر الحالية —
            // من غيره أي حد يقعد على جهاز مفتوح يقدر يسرق الحساب
            if (restrictToSelf)
            {
                CurrentLoginPasswordPanel.Visibility = Visibility.Visible;
                CurrentOperationsPasswordPanel.Visibility = hasOperationsPassword
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            Loaded += (_, _) => NameBox.Focus();
        }

        public string AccountName => NameBox.Text.Trim();
        public string? PhoneNumber => string.IsNullOrWhiteSpace(PhoneBox.Text) ? null : PhoneBox.Text.Trim();
        public HourlyRoleEnum Role => (HourlyRoleEnum)(RoleBox.SelectedValue ?? HourlyRoleEnum.DepartmentManager);

        /// <summary>مدير القسم مالوش سعر يومية خالص — دايمًا 0 بصرف النظر عن أي قيمة كانت متكتوبة</summary>
        public decimal DailyWageEgp => Role == HourlyRoleEnum.DepartmentManager
            ? 0m
            : decimal.TryParse(WageBox.Text.Trim(), out var w) ? w : 0m;

        public string Username => UsernameBox.Text.Trim();

        /// <summary>فاضي = مفيش تغيير مطلوب لكلمة المرور (وضع التعديل بس)</summary>
        public string Password => PasswordBox.Password;

        /// <summary>كلمة مرور الدخول الحالية — تعديل ذاتي بس، لازم تتملى لو هيغيّر اسم الدخول أو كلمة المرور</summary>
        public string CurrentLoginPassword => CurrentLoginPasswordBox.Password;

        /// <summary>فاضي = مفيش تغيير/تحديد مطلوب لكلمة سر العمليات</summary>
        public string OperationsPassword => OperationsPasswordBox.Password;

        /// <summary>كلمة سر العمليات الحالية — تعديل ذاتي بس، لازم تتملى لو هيغيّرها</summary>
        public string CurrentOperationsPassword => CurrentOperationsPasswordBox.Password;

        /// <summary>
        /// صورة الحساب بعد الحفظ (null = مفيش صورة أو المستخدم شالها).
        /// بتبقى مصغّرة ومضغوطة جاهزة للتخزين.
        /// </summary>
        public byte[]? PhotoData { get; private set; }

        /// <summary>اتغيّرت الصورة في الجلسة دي؟</summary>
        public bool PhotoChanged { get; private set; }

        /// <summary>تعبئة الفورم ببيانات حساب موجود (وضع التعديل)</summary>
        public void LoadAccount(
            string name, string? phone, HourlyRoleEnum role, decimal dailyWageEgp,
            string? username, byte[]? photoData = null)
        {
            NameBox.Text = name;
            PhoneBox.Text = phone ?? "";
            RoleBox.SelectedValue = role;
            WageBox.Text = dailyWageEgp > 0 ? dailyWageEgp.ToString("0.##") : "";
            UsernameBox.Text = username ?? "";
            _originalUsername = username ?? "";

            PhotoData = photoData;
            PhotoChanged = false;
            ShowPhotoPreview();

            ApplyRoleVisibility(role);
        }

        private void RoleBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (RoleBox.SelectedValue is HourlyRoleEnum role)
                ApplyRoleVisibility(role);
        }

        /// <summary>سعر اليومية بلا معنى لمدير القسم — خانتها بتتخفي خالص</summary>
        private void ApplyRoleVisibility(HourlyRoleEnum role)
        {
            WagePanel.Visibility = role == HourlyRoleEnum.DepartmentManager
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

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
                Title = "اختار صورة الحساب",
                Filter = StoredImageHelper.FileDialogFilter,
                CheckFileExists = true
            };

            if (picker.ShowDialog(this) != true) return;

            try
            {
                PhotoData = StoredImageHelper.LoadForStorage(picker.FileName);
                PhotoChanged = true;
                ShowPhotoPreview();

                ErrorText.ClearError();
            }
            catch (Exception ex)
            {
                ErrorText.ShowError(ex.Message);
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
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                ErrorText.ShowError("اسم الحساب مطلوب");
                NameBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(UsernameBox.Text) || UsernameBox.Text.Trim().Length < 3)
            {
                ErrorText.ShowError("اسم الدخول لازم يكون 3 حروف على الأقل");
                UsernameBox.Focus();
                return;
            }

            // إضافة: كلمة المرور إجبارية. تعديل: اختيارية (فاضية = ماتغيّرش)
            if (!_isEditMode && (string.IsNullOrEmpty(PasswordBox.Password) || PasswordBox.Password.Length < 4))
            {
                ErrorText.ShowError("كلمة المرور لازم تكون 4 حروف/أرقام على الأقل");
                PasswordBox.Focus();
                return;
            }

            if (_isEditMode && PasswordBox.Password.Length > 0 && PasswordBox.Password.Length < 4)
            {
                ErrorText.ShowError("كلمة المرور لازم تكون 4 حروف/أرقام على الأقل (أو سيبها فاضية)");
                PasswordBox.Focus();
                return;
            }

            var wageText = WageBox.Text.Trim();
            if (wageText.Length > 0 && (!decimal.TryParse(wageText, out var wage) || wage < 0))
            {
                ErrorText.ShowError("سعر اليومية لازم يكون رقم موجب (أو سيبه فاضي)");
                WageBox.Focus();
                return;
            }

            if (_restrictToSelf)
            {
                var usernameChanged = !string.Equals(
                    UsernameBox.Text.Trim(), _originalUsername, StringComparison.OrdinalIgnoreCase);

                if ((usernameChanged || PasswordBox.Password.Length > 0) && CurrentLoginPasswordBox.Password.Length == 0)
                {
                    ErrorText.ShowError("لازم تدخل كلمة مرورك الحالية عشان تغيّر اسم الدخول أو كلمة المرور");
                    CurrentLoginPasswordBox.Focus();
                    return;
                }

                if (OperationsPasswordBox.Password.Length > 0
                    && CurrentOperationsPasswordPanel.Visibility == Visibility.Visible
                    && CurrentOperationsPasswordBox.Password.Length == 0)
                {
                    ErrorText.ShowError("لازم تدخل كلمة سر العمليات الحالية عشان تغيّرها");
                    CurrentOperationsPasswordBox.Focus();
                    return;
                }
            }

            DialogResult = true;
        }

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
    }
}
