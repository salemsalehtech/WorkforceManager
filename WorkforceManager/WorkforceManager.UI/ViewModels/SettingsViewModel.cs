using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using WorkforceManager.Business.Services;
using WorkforceManager.Data;
using WorkforceManager.UI.Views;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// عقل شاشة الإعدادات — النسخ الاحتياطي بالكامل:
    /// - معلومات النسخ المحلي (المجلد، العدد، آخر نسخة) + فتح المجلد.
    /// - المجلد الخارجي (فلاشة/قرص تاني): تفعيل/إيقاف — النسخة المحلية على
    ///   نفس الهارد متحميش من تلف الهارد نفسه، الخارجية هي اللي بتحمي.
    /// - نسخة فورية بضغطة، واسترجاع نسخة (بيعيد تشغيل البرنامج بعدها).
    /// </summary>
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public SettingsViewModel(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        // ------- معلومات معروضة -------

        [ObservableProperty]
        private string _localFolderText = AppPaths.BackupsFolder;

        [ObservableProperty]
        private string _localStatusText = "";

        [ObservableProperty]
        private string _externalFolderText = "";

        [ObservableProperty]
        private string _externalStatusText = "";

        /// <summary>هل النسخ الخارجي مفعّل؟ (بيتحكم في ظهور زرار الإيقاف)</summary>
        [ObservableProperty]
        private bool _hasExternal;

        // ------- كلمة سر العمليات -------

        /// <summary>
        /// فيه كلمة سر عمليات متسجّلة؟ من غيرها كل عمليات الحذف
        /// والتعديلات المالية بتعدّي من غير أي تأكيد.
        /// </summary>
        [ObservableProperty]
        private bool _hasOperationsPassword;

        [ObservableProperty]
        private string _operationsStatusText = "";

        /// <summary>عنوان الزرار: "حطّ كلمة سر" أول مرة، "غيّر" بعد كده</summary>
        public string OperationsButtonText => HasOperationsPassword ? "غيّر كلمة السر" : "حطّ كلمة سر";

        partial void OnHasOperationsPasswordChanged(bool value) =>
            OnPropertyChanged(nameof(OperationsButtonText));

        /// <summary>تحديث كل المعلومات المعروضة من الملفات والإعدادات الفعلية</summary>
        public void LoadInfo()
        {
            // النسخ المحلي
            if (Directory.Exists(AppPaths.BackupsFolder))
            {
                var files = Directory.GetFiles(AppPaths.BackupsFolder, "workforce_*.db");
                LocalStatusText = files.Length == 0
                    ? "لسه مفيش نسخ محفوظة"
                    : $"{files.Length} نسخة محفوظة — آخر نسخة: {files.Max(File.GetLastWriteTime):yyyy/MM/dd HH:mm}";
            }
            else
            {
                LocalStatusText = "لسه مفيش نسخ محفوظة";
            }

            // النسخ الخارجي
            var settings = AppSettingsStore.Load();
            HasExternal = !string.IsNullOrWhiteSpace(settings.ExternalBackupFolder);

            if (!HasExternal)
            {
                ExternalFolderText = "غير مفعّل";
                ExternalStatusText = "النسخة المحلية على نفس الهارد — لو الهارد باظ بتضيع معاه. فعّل مجلد خارجي (فلاشة/قرص تاني) وهتتاخد نسخة عليه تلقائيًا كل يوم.";
            }
            else
            {
                ExternalFolderText = settings.ExternalBackupFolder!;
                if (Directory.Exists(settings.ExternalBackupFolder))
                {
                    var files = Directory.GetFiles(settings.ExternalBackupFolder!, "workforce_*.db");
                    ExternalStatusText = files.Length == 0
                        ? "المجلد متاح — لسه مفيش نسخ فيه (هتتاخد أول نسخة تلقائيًا)"
                        : $"المجلد متاح ✔ — {files.Length} نسخة، آخرها: {files.Max(File.GetLastWriteTime):yyyy/MM/dd HH:mm}";
                }
                else
                {
                    ExternalStatusText = "⚠ المجلد مش متاح دلوقتي (الفلاشة/القرص مش موصل؟) — النسخ الخارجي هيشتغل تلقائيًا أول ما يرجع.";
                }
            }
        }

        /// <summary>
        /// بيقرا حالة كلمة سر العمليات. منفصلة عن LoadInfo لأنها بتلمس
        /// قاعدة البيانات (وLoadInfo بتقرا ملفات بس).
        /// </summary>
        public async Task LoadOperationsStateAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var gate = scope.ServiceProvider.GetRequiredService<OperationsPasswordService>();

            HasOperationsPassword = await gate.IsConfiguredAsync();

            OperationsStatusText = HasOperationsPassword
                ? "مفعّلة ✔ — الحذف والتعديلات المالية بيطلبوا الكلمة دي"
                : "⚠ مش متسجّلة — أي حد يقعد على الجهاز يقدر يحذف عمال وسجلات إنتاج ويعدّل الأجور من غير أي تأكيد.";
        }

        // ------- الأوامر -------

        /// <summary>
        /// يحطّ كلمة سر العمليات أول مرة أو يغيّرها.
        ///
        /// التحقق من الكلمة القديمة بيتم في OperationsPasswordService مش
        /// هنا — الشاشة بتجمع المدخلات بس.
        /// </summary>
        [RelayCommand]
        private async Task SetOperationsPasswordAsync()
        {
            var input = OperationsPasswordDialog.Ask(
                Application.Current.MainWindow, requiresCurrent: HasOperationsPassword);

            if (input is null) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var gate = scope.ServiceProvider.GetRequiredService<OperationsPasswordService>();
                await gate.SetPasswordAsync(input.CurrentPassword, input.NewPassword);

                await LoadOperationsStateAsync();

                MessageBox.Show(
                    "كلمة سر العمليات اتسجّلت. من دلوقتي أي حذف أو تعديل مالي هيطلبها.",
                    "تم", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "مش هينفع", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        private void OpenLocalFolder()
        {
            Directory.CreateDirectory(AppPaths.BackupsFolder);
            Process.Start(new ProcessStartInfo(AppPaths.BackupsFolder) { UseShellExecute = true });
        }

        [RelayCommand]
        private void BackupNow()
        {
            try
            {
                var settings = AppSettingsStore.Load();
                var (localPath, externalPath) = DatabaseBackupService.BackupNow(AppPaths.DbPath, settings.ExternalBackupFolder);

                var externalLine = externalPath is not null
                    ? $"\n✔ نسخة خارجية: {externalPath}"
                    : "";
                MessageBox.Show($"تمت النسخة الاحتياطية بنجاح:\n✔ نسخة محلية: {localPath}{externalLine}",
                    "تم النسخ", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            LoadInfo();
        }

        [RelayCommand]
        private void ChooseExternalFolder()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "اختار مجلد النسخ الخارجي (فلاشة / قرص تاني / مجلد شبكة)"
            };
            if (dialog.ShowDialog() != true) return;

            var settings = AppSettingsStore.Load();
            settings.ExternalBackupFolder = dialog.FolderName;
            AppSettingsStore.Save(settings);

            // نسخة فورية على طول — المستخدم يشوف بعينه إن النسخ الخارجي شغال
            try
            {
                DatabaseBackupService.BackupNow(AppPaths.DbPath, dialog.FolderName);
                MessageBox.Show($"تم تفعيل النسخ الخارجي على:\n{dialog.FolderName}\n\nواتاخدت أول نسخة بنجاح ✔\nمن دلوقتي هتتاخد نسخة تلقائيًا هناك كل يوم.",
                    "تم التفعيل", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            LoadInfo();
        }

        [RelayCommand]
        private void DisableExternal()
        {
            if (MessageBox.Show("إيقاف النسخ الخارجي؟ النسخ الموجودة في المجلد الخارجي هتفضل زي ما هي.",
                    "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            var settings = AppSettingsStore.Load();
            settings.ExternalBackupFolder = null;
            AppSettingsStore.Save(settings);
            LoadInfo();
        }

        [RelayCommand]
        private void RestoreBackup()
        {
            var dialog = new OpenFileDialog
            {
                Title = "اختار النسخة الاحتياطية اللي هتسترجعها",
                Filter = "نسخ احتياطية (*.db)|*.db",
                InitialDirectory = Directory.Exists(AppPaths.BackupsFolder) ? AppPaths.BackupsFolder : AppPaths.DataFolder
            };
            if (dialog.ShowDialog() != true) return;

            // تأكيد صارم — دي عملية بتستبدل كل البيانات الحالية
            var confirm = MessageBox.Show(
                $"هتسترجع النسخة:\n{dialog.FileName}\n\n" +
                "⚠ كل البيانات الحالية هتتستبدل ببيانات النسخة دي.\n" +
                "(هناخد نسخة أمان من البيانات الحالية الأول تلقائيًا)\n\n" +
                "البرنامج هيعيد تشغيل نفسه بعد الاسترجاع. نكمل؟",
                "تأكيد الاسترجاع", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                var safetyPath = DatabaseBackupService.RestoreBackup(AppPaths.DbPath, dialog.FileName);

                MessageBox.Show(
                    $"تم الاسترجاع بنجاح ✔\nنسخة الأمان من بياناتك السابقة:\n{safetyPath}\n\nهيعاد تشغيل البرنامج دلوقتي.",
                    "تم الاسترجاع", MessageBoxButton.OK, MessageBoxImage.Information);

                // إعادة تشغيل نظيفة — عشان كل الاتصالات والشاشات تفتح على البيانات المسترجعة
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"تعذر الاسترجاع:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
