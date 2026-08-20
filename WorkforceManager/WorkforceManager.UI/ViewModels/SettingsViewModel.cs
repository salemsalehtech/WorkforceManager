using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Models;
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

        /// <summary>
        /// إصدار البرنامج — بيتقرا من الأسمبلي، ومصدره الوحيد
        /// &lt;Version&gt; في Directory.Build.props.
        ///
        /// موجود عشان أول سؤال في أي مكالمة دعم هو "إنت شغّال على أنهي
        /// نسخة؟"، ومن غيره مفيش إجابة. بيتغيّر لوحده مع كل ترقية فمحتاجش
        /// حد يفتكر يحدّثه.
        /// </summary>
        public static string AppVersionText
        {
            get
            {
                var assembly = Assembly.GetEntryAssembly();

                var informational = assembly
                    ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;

                // أدوات البناء بتلزق hash بتاع الكوميت بعد علامة + —
                // مالوش أي معنى للمستخدم
                var plus = informational?.IndexOf('+') ?? -1;
                if (plus > 0) informational = informational![..plus];

                return "الإصدار " +
                       (informational ?? assembly?.GetName().Version?.ToString(3) ?? "؟");
            }
        }

        /// <summary>
        /// مكان قاعدة البيانات على الجهاز. بيتغيّر حسب طريقة التشغيل
        /// (مثبَّت / محمول) فالأحسن يتقرا مش يتخمّن — وده كمان المجلد اللي
        /// المستخدم بياخد منه نسخة لو نقل البرنامج لجهاز تاني.
        /// </summary>
        public static string DataFolderText => AppPaths.DataFolder;

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

        // ======================= المظهر وإعدادات النسخ =======================

        /// <summary>بيمنع الحفظ وإحنا بنملا القيم من الملف</summary>
        private bool _loadingSettings;

        /// <summary>
        /// الوضع الليلي — **بيتبدّل في اللحظة**.
        ///
        /// اللوحة كلها في ملف واحد بيتبدّل مكانه، والأنماط بتشاور عليه
        /// بـ DynamicResource فالربط بيفضل حيّ.
        /// </summary>
        [ObservableProperty]
        private bool _darkMode;

        partial void OnDarkModeChanged(bool value)
        {
            if (_loadingSettings) return;

            var settings = AppSettingsStore.Load();
            settings.DarkMode = value;
            AppSettingsStore.Save(settings);

            App.ApplyTheme(value);

            Notify.Success(value ? "الوضع الليلي اشتغل" : "الوضع الفاتح رجع");
        }

        // ------- هوية التقارير المطبوعة -------

        /// <summary>
        /// اسم المصنع فوق كل تقرير مصدَّر. التقرير بيخرج من البرنامج
        /// ويروح لناس مشافوش البرنامج (محاسب، مالك، جهة خارجية) فلازم
        /// يقول هو بتاع مين من غير ما حد يشرح.
        /// </summary>
        [ObservableProperty]
        private string _factoryName = "";

        partial void OnFactoryNameChanged(string value)
        {
            if (_loadingSettings) return;

            var settings = AppSettingsStore.Load();
            settings.FactoryName = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            AppSettingsStore.Save(settings);
            RefreshMainWindowIdentity();
        }

        /// <summary>
        /// القسم اللي النسخة دي بتديره. بيتعرض تحت اسم المصنع في
        /// القايمة الجانبية وفي عنوان النافذة — المصنع الواحد فيه أكتر
        /// من قسم، واسم المصنع لوحده مبيقولش النسخة دي بتاعة مين.
        /// </summary>
        [ObservableProperty]
        private string _departmentName = "";

        partial void OnDepartmentNameChanged(string value)
        {
            if (_loadingSettings) return;

            var settings = AppSettingsStore.Load();
            settings.DepartmentName = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            AppSettingsStore.Save(settings);
            RefreshMainWindowIdentity();
        }

        /// <summary>
        /// الاسم بيتغيّر في القايمة الجانبية على طول — من غير كده
        /// المستخدم بيكتبه ومبيشوفش أثره غير لما يقفل ويفتح
        /// </summary>
        private static void RefreshMainWindowIdentity() =>
            (Application.Current.MainWindow as MainWindow)?.ShowIdentity();

        [ObservableProperty]
        private string _logoPath = "";

        public string LogoText => string.IsNullOrWhiteSpace(LogoPath)
            ? "مفيش شعار — التقارير هتطلع من غيره"
            : System.IO.Path.GetFileName(LogoPath);

        partial void OnLogoPathChanged(string value) => OnPropertyChanged(nameof(LogoText));

        [RelayCommand]
        private void ChooseLogo()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "اختار صورة الشعار",
                Filter = "صور (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
            };

            if (dialog.ShowDialog() != true) return;

            LogoPath = dialog.FileName;

            var settings = AppSettingsStore.Load();
            settings.LogoPath = dialog.FileName;
            AppSettingsStore.Save(settings);

            Notify.Success("الشعار اتحفظ — هيظهر فوق التقارير المصدَّرة");
        }

        [RelayCommand]
        private void ClearLogo()
        {
            LogoPath = "";

            var settings = AppSettingsStore.Load();
            settings.LogoPath = null;
            AppSettingsStore.Save(settings);
        }

        // ------- شعار البرنامج نفسه -------

        /// <summary>
        /// منفصل عن <see cref="LogoPath"/> (شعار التقارير) عن قصد —
        /// شوف توثيق AppSettingsStore.AppLogoPath. ده بيحل محل شعار
        /// WMS في القايمة الجانبية وشاشة الدخول وكارت العمال الفاضي
        /// وأيقونة النافذة، لحظة ما يتغيّر.
        /// </summary>
        [ObservableProperty]
        private string _appLogoPath = "";

        public string AppLogoText => string.IsNullOrWhiteSpace(AppLogoPath)
            ? "مفيش شعار — شعار WMS الافتراضي هيفضل في البرنامج"
            : System.IO.Path.GetFileName(AppLogoPath);

        partial void OnAppLogoPathChanged(string value) => OnPropertyChanged(nameof(AppLogoText));

        [RelayCommand]
        private void ChooseAppLogo()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "اختار شعار البرنامج",
                Filter = "صور (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
            };

            if (dialog.ShowDialog() != true) return;

            AppLogoPath = dialog.FileName;

            var settings = AppSettingsStore.Load();
            settings.AppLogoPath = dialog.FileName;
            AppSettingsStore.Save(settings);
            RefreshMainWindowIdentity();

            Notify.Success("شعار البرنامج اتحفظ — هيبان بدل شعار WMS في البرنامج");
        }

        [RelayCommand]
        private void ClearAppLogo()
        {
            AppLogoPath = "";

            var settings = AppSettingsStore.Load();
            settings.AppLogoPath = null;
            AppSettingsStore.Save(settings);
            RefreshMainWindowIdentity();
        }

        // ------- أسباب الهالك -------

        /// <summary>
        /// قايمة أسباب الهالك — كل مصنع وأسبابه.
        ///
        /// من غير القايمة دي المستخدم بيكتب السبب بالإيد في خانة
        /// الملاحظات كل مرة، وساعتها "الهالك راح فين؟" تبقى سؤال مالوش
        /// إجابة لأن الملاحظات مبتتجمّعش في تقرير.
        /// </summary>
        public ObservableCollection<ScrapReason> ScrapReasons { get; } = new();

        [ObservableProperty]
        private string _newScrapReason = "";

        private async Task LoadScrapReasonsAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var reasons = await scope.ServiceProvider
                .GetRequiredService<ScrapService>()
                .GetAllReasonsAsync();

            ScrapReasons.Clear();
            foreach (var reason in reasons) ScrapReasons.Add(reason);
        }

        [RelayCommand]
        private async Task AddScrapReasonAsync()
        {
            var name = NewScrapReason.Trim();
            if (name.Length == 0) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ScrapService>().AddReasonAsync(name);
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "مش هينفع");
                return;
            }

            NewScrapReason = "";
            await LoadScrapReasonsAsync();
        }

        /// <summary>
        /// بيوقف السبب أو بيرجّعه. **مفيش حذف** عن قصد: السبب المستخدم
        /// في تقارير الشهور اللي فاتت لازم يفضل معروف — الموقوف بيختفي
        /// من التسجيل الجديد بس.
        /// </summary>
        [RelayCommand]
        private async Task ToggleScrapReasonAsync(ScrapReason? reason)
        {
            if (reason is null) return;

            using (var scope = _scopeFactory.CreateScope())
                await scope.ServiceProvider
                    .GetRequiredService<ScrapService>()
                    .SetReasonActiveAsync(reason.Id, !reason.IsActive);

            await LoadScrapReasonsAsync();
        }

        /// <summary>عدد أيام الاحتفاظ بالنسخ الاحتياطية</summary>
        [ObservableProperty]
        private int _retentionDays = AppSettings.DefaultBackupRetentionDays;

        partial void OnRetentionDaysChanged(int value)
        {
            if (_loadingSettings) return;

            var clamped = Math.Clamp(value,
                DatabaseBackupService.MinRetentionDays, DatabaseBackupService.MaxRetentionDays);

            // القيمة المعروضة بتترد للحد لو المستخدم كتب رقم بره النطاق —
            // من غير كده الخانة بتقول رقم والملف بيقول رقم تاني
            if (clamped != value)
            {
                RetentionDays = clamped;
                return;
            }

            var settings = AppSettingsStore.Load();
            settings.BackupRetentionDays = clamped;
            AppSettingsStore.Save(settings);
            OnPropertyChanged(nameof(RetentionText));
        }

        public string RetentionText =>
            $"النسخ الأقدم من {RetentionDays} يوم بتتمسح تلقائيًا " +
            $"(من {DatabaseBackupService.MinRetentionDays} لـ {DatabaseBackupService.MaxRetentionDays} يوم)";

        // ------- تنظيف سجل العمليات -------
        // مدتين مش واحدة: أحداث الفلوس السؤال عليها بييجي بعد شهور،
        // وأحداث الحذف الإداري بتفقد قيمتها بسرعة.

        /// <summary>مدة الاحتفاظ بأحداث الحذف الإداري (0 = التنظيف متوقف)</summary>
        [ObservableProperty]
        private int _logRetentionDays = AppSettings.DefaultActivityLogRetentionDays;

        partial void OnLogRetentionDaysChanged(int value) =>
            SaveLogRetention(value, isFinancial: false);

        /// <summary>مدة الاحتفاظ بأحداث الفلوس (0 = التنظيف متوقف)</summary>
        [ObservableProperty]
        private int _logFinancialRetentionDays = AppSettings.DefaultActivityLogFinancialRetentionDays;

        partial void OnLogFinancialRetentionDaysChanged(int value) =>
            SaveLogRetention(value, isFinancial: true);

        /// <summary>
        /// الصفر مسموح ومعناه "بلاش تنظيف خالص". أي رقم تاني بيترد لحد
        /// أدنى — سجل بيتمسح كل أسبوع مش سجل.
        /// </summary>
        private void SaveLogRetention(int value, bool isFinancial)
        {
            if (_loadingSettings) return;

            var clamped = value <= 0 ? 0 : Math.Max(value, ActivityLogService.MinRetentionDays);
            if (clamped != value)
            {
                if (isFinancial) LogFinancialRetentionDays = clamped;
                else LogRetentionDays = clamped;
                return;
            }

            var settings = AppSettingsStore.Load();
            if (isFinancial) settings.ActivityLogFinancialRetentionDays = clamped;
            else settings.ActivityLogRetentionDays = clamped;
            AppSettingsStore.Save(settings);

            OnPropertyChanged(nameof(LogRetentionText));
        }

        public string LogRetentionText
        {
            get
            {
                var routine = LogRetentionDays > 0
                    ? $"أحداث الحذف بتتمسح بعد {LogRetentionDays} يوم"
                    : "أحداث الحذف مش بتتمسح";
                var money = LogFinancialRetentionDays > 0
                    ? $"وأحداث الفلوس بعد {LogFinancialRetentionDays} يوم"
                    : "وأحداث الفلوس مش بتتمسح";

                return $"{routine}، {money}. صفر = بلاش تنظيف. " +
                       $"التنظيف بيحصل عند فتح البرنامج، بعد ما النسخة الاحتياطية تكون اتاخدت.";
            }
        }

        /// <summary>النسخة التلقائية عند التشغيل شغالة؟</summary>
        [ObservableProperty]
        private bool _autoBackupOnStartup = true;

        partial void OnAutoBackupOnStartupChanged(bool value)
        {
            if (_loadingSettings) return;

            var settings = AppSettingsStore.Load();
            settings.AutoBackupOnStartup = value;
            AppSettingsStore.Save(settings);

            // الإيقاف قرار المستخدم، بس لازم يعرف نتيجته بدل ما يعدّي بصمت
            if (!value)
                Notify.Warn("النسخة التلقائية اتوقفت. مفيش نسخة هتتاخد لوحدها عند فتح البرنامج — " +
                    "لازم تاخدها بنفسك من زرار \"خد نسخة دلوقتي\".", "تنبيه");
        }

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

            // إعدادات المستخدم — بتتقرا من غير ما تشغّل الحفظ (الأعلام
            // بتمنع الحفظ أثناء التحميل)
            _loadingSettings = true;
            RetentionDays = settings.BackupRetentionDays;
            AutoBackupOnStartup = settings.AutoBackupOnStartup;
            DarkMode = settings.DarkMode;
            LogRetentionDays = settings.ActivityLogRetentionDays;
            LogFinancialRetentionDays = settings.ActivityLogFinancialRetentionDays;
            FactoryName = settings.FactoryName ?? "";
            DepartmentName = settings.DepartmentName ?? "";
            LogoPath = settings.LogoPath ?? "";
            AppLogoPath = settings.AppLogoPath ?? "";
            _loadingSettings = false;
            OnPropertyChanged(nameof(LogRetentionText));
            OnPropertyChanged(nameof(LogoText));
            OnPropertyChanged(nameof(AppLogoText));

            SafeAsync.Run(LoadScrapReasonsAsync);

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

                Notify.Info("كلمة سر العمليات اتسجّلت. من دلوقتي أي حذف أو تعديل مالي هيطلبها.", "تم");
            }
            catch (InvalidOperationException ex)
            {
                Notify.Warn(ex.Message, "مش هينفع");
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
                var (localPath, externalPath) = DatabaseBackupService.BackupNow(AppPaths.DbPath, settings.ExternalBackupFolder, settings.BackupRetentionDays);

                var externalLine = externalPath is not null
                    ? $"\n✔ نسخة خارجية: {externalPath}"
                    : "";
                Notify.Info($"تمت النسخة الاحتياطية بنجاح:\n✔ نسخة محلية: {localPath}{externalLine}", "تم النسخ");
            }
            catch (InvalidOperationException ex)
            {
                Notify.Warn(ex.Message, "تنبيه");
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
                DatabaseBackupService.BackupNow(AppPaths.DbPath, dialog.FolderName, AppSettingsStore.Load().BackupRetentionDays);
                Notify.Info($"تم تفعيل النسخ الخارجي على:\n{dialog.FolderName}\n\nواتاخدت أول نسخة بنجاح ✔\nمن دلوقتي هتتاخد نسخة تلقائيًا هناك كل يوم.", "تم التفعيل");
            }
            catch (InvalidOperationException ex)
            {
                Notify.Warn(ex.Message, "تنبيه");
            }

            LoadInfo();
        }

        [RelayCommand]
        private void DisableExternal()
        {
            if (!Notify.Ask("إيقاف النسخ الخارجي؟ النسخ الموجودة في المجلد الخارجي هتفضل زي ما هي.", "تأكيد"))
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

            // AskDangerous مش Ask: الافتراضي "لأ" لأن دي عملية بتستبدل
            // كل البيانات الحالية ومش بترجع
            if (!Notify.AskDangerous(
                    $"هتسترجع النسخة:\n{dialog.FileName}\n\n" +
                    "⚠ كل البيانات الحالية هتتستبدل ببيانات النسخة دي.\n" +
                    "(هناخد نسخة أمان من البيانات الحالية الأول تلقائيًا)\n\n" +
                    "البرنامج هيعيد تشغيل نفسه بعد الاسترجاع. نكمل؟",
                    "تأكيد الاسترجاع"))
                return;

            try
            {
                var safetyPath = DatabaseBackupService.RestoreBackup(AppPaths.DbPath, dialog.FileName);

                Notify.Info($"تم الاسترجاع بنجاح ✔\nنسخة الأمان من بياناتك السابقة:\n{safetyPath}\n\nهيعاد تشغيل البرنامج دلوقتي.", "تم الاسترجاع");

                // إعادة تشغيل نظيفة — عشان كل الاتصالات والشاشات تفتح على البيانات المسترجعة
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Notify.Warn($"تعذر الاسترجاع:\n{ex.Message}", "خطأ");
            }
        }
    }
}
