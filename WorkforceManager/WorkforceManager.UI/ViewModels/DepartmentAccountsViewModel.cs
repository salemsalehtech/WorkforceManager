using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.UI.Views;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// عقل شاشة "الحسابات الإدارية" — قايمة منفصلة تمامًا عن شاشة العمال
    /// والمهارات. الحساب الإداري في الأساس Worker بـ HourlyRole مخصوص
    /// (DepartmentManager/DepartmentHead) **ومربوط بحساب دخول فعلي**
    /// (AppUser.WorkerId) — بيوزر وباسورد خاصين بيه من دلوقتي، مش
    /// حساب مشترك للبرنامج كله.
    ///
    /// فرز الوصول: مدير القسم (CurrentUserContext.IsDepartmentManager)
    /// بس هو اللي يشوف القايمة كاملة وله كل أوامر الإدارة (إضافة/
    /// تعديل/إيقاف/حذف). أي حساب تاني (رئيس قسم) بيشوف بروفايله هو
    /// بس — مفيش حساب تاني ولا أي تفصيلة عنه بتظهر له، ومفيش أي أمر
    /// إدارة متاح له خالص (شوف <see cref="IsReadOnlyProfile"/>).
    /// </summary>
    public partial class DepartmentAccountsViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly CurrentUserContext _currentUser;

        public DepartmentAccountsViewModel(IServiceScopeFactory scopeFactory, CurrentUserContext currentUser)
        {
            _scopeFactory = scopeFactory;
            _currentUser = currentUser;
        }

        public ObservableCollection<DepartmentAccountRow> Accounts { get; } = new();

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private bool _hasAccounts;

        /// <summary>
        /// مدير القسم بس — بيتحكم في ظهور زرار "إضافة حساب" وأوامر
        /// الإدارة على كل كارت. أي حساب تاني بيشوف كارت واحد للقراءة بس.
        /// </summary>
        public bool CanManage => _currentUser.IsDepartmentManager;

        public bool IsReadOnlyProfile => !CanManage;

        public async Task InitializeAsync() => await LoadAsync();

        private async Task LoadAsync()
        {
            IsLoading = true;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var workerRepo = scope.ServiceProvider.GetRequiredService<IWorkerRepository>();
                var auth = scope.ServiceProvider.GetRequiredService<AuthService>();

                var workers = await workerRepo.GetDepartmentAccountsAsync();

                // فرز الوصول: مدير القسم يشوف الكل، وأي حساب تاني بروفايله
                // بس — مفيش حساب تاني ولا أي تفصيلة عنه بتظهر له
                if (!CanManage)
                    workers = workers.Where(w => w.Id == _currentUser.WorkerId).ToList();

                Accounts.Clear();
                foreach (var w in workers)
                {
                    var user = await auth.GetUserByWorkerIdAsync(w.Id);
                    Accounts.Add(new DepartmentAccountRow
                    {
                        WorkerId = w.Id,
                        FullName = w.FullName,
                        PhoneNumber = w.PhoneNumber,
                        Role = w.HourlyRole!.Value,
                        DailyWageEgp = w.DailyWageEgp,
                        IsActive = w.IsActive,
                        PhotoData = w.PhotoData,
                        Username = user?.Username
                    });
                }

                HasAccounts = Accounts.Count > 0;
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void OpenProfile(DepartmentAccountRow? row)
        {
            if (row is null) return;

            new DepartmentAccountProfileDialog(_scopeFactory, row, CanManage)
            {
                Owner = Application.Current.MainWindow
            }.ShowDialog();
        }

        [RelayCommand]
        private async Task AddAccountAsync()
        {
            if (!CanManage) return; // دفاع إضافي — الزرار نفسه مخفي عن غير المدير

            var dialog = new DepartmentAccountEditDialog { Owner = Application.Current.MainWindow };
            if (dialog.ShowDialog() != true) return;

            using var scope = _scopeFactory.CreateScope();
            var mgmt = scope.ServiceProvider.GetRequiredService<WorkerManagementService>();
            var auth = scope.ServiceProvider.GetRequiredService<AuthService>();

            try
            {
                var worker = await mgmt.CreateWorkerAsync(
                    dialog.AccountName, dialog.PhoneNumber,
                    hourlyRole: dialog.Role, dailyWageEgp: dialog.DailyWageEgp);

                if (dialog.PhotoChanged)
                    await mgmt.SetWorkerPhotoAsync(worker.Id, dialog.PhotoData);

                var user = await auth.AddUserForWorkerAsync(worker.Id, dialog.Username, dialog.Password, dialog.AccountName);

                if (dialog.OperationsPassword.Length > 0)
                    await scope.ServiceProvider.GetRequiredService<OperationsPasswordService>()
                        .SetPasswordForUserAsync(user.Id, dialog.OperationsPassword);

                await LoadAsync();
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "خطأ في الإضافة");
            }
        }

        /// <summary>
        /// المدير يعدّل أي حساب. أي حساب تاني (رئيس قسم) يعدّل بياناته
        /// هو بس — <see cref="restrictFields"/> بتقفل المسمّى وسعر
        /// اليومية وقتها (قرارات إدارية مش حاجة الشخص يغيّرها لنفسه)،
        /// وتغيير اسم الدخول/كلمة المرور/كلمة سر العمليات بيتطلب
        /// الكلمة الحالية بدل التصحيح الإداري المباشر (شوف
        /// DepartmentAccountEditDialog.CurrentLoginPassword/
        /// CurrentOperationsPassword).
        /// </summary>
        [RelayCommand]
        private async Task EditAccountAsync(DepartmentAccountRow? row)
        {
            if (row is null) return;

            var isOwnRow = row.WorkerId == _currentUser.WorkerId;
            if (!CanManage && !isOwnRow) return; // دفاع إضافي — الزرار نفسه مخفي عن أي حساب تاني غير حسابه هو

            var restrictFields = !CanManage;

            using var scope = _scopeFactory.CreateScope();
            var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
            var opsGate = scope.ServiceProvider.GetRequiredService<OperationsPasswordService>();

            var hasOpsPassword = restrictFields && await opsGate.IsConfiguredAsync();

            var dialog = new DepartmentAccountEditDialog(
                isEditMode: true, restrictToSelf: restrictFields, hasOperationsPassword: hasOpsPassword)
            {
                Owner = Application.Current.MainWindow,
                Title = restrictFields ? "تعديل بياناتي" : "تعديل حساب إداري"
            };
            dialog.LoadAccount(row.FullName, row.PhoneNumber, row.Role, row.DailyWageEgp, row.Username, row.PhotoData);
            if (dialog.ShowDialog() != true) return;

            var mgmt = scope.ServiceProvider.GetRequiredService<WorkerManagementService>();

            try
            {
                await mgmt.UpdateWorkerAsync(
                    row.WorkerId, dialog.AccountName, dialog.PhoneNumber,
                    hourlyRole: dialog.Role, dailyWageEgp: dialog.DailyWageEgp);

                if (dialog.PhotoChanged)
                    await mgmt.SetWorkerPhotoAsync(row.WorkerId, dialog.PhotoData);

                var user = await auth.GetUserByWorkerIdAsync(row.WorkerId);
                if (user is null)
                {
                    // حساب إداري قديم مالوش حساب دخول لسه (نادر) — بيتضاف له واحد
                    user = await auth.AddUserForWorkerAsync(row.WorkerId, dialog.Username, dialog.Password, dialog.AccountName);
                }
                else
                {
                    var usernameChanged = !string.Equals(user.Username, dialog.Username, StringComparison.OrdinalIgnoreCase);
                    var effectiveUsername = user.Username;

                    if (restrictFields)
                    {
                        // تعديل ذاتي: لازم كلمة المرور الحالية عشان يتغيّر
                        // اسم الدخول أو كلمة المرور — مفيش تصحيح إداري هنا
                        if (usernameChanged)
                        {
                            user = await auth.ChangeUsernameAsync(user.Username, dialog.CurrentLoginPassword, dialog.Username);
                            effectiveUsername = user.Username;
                        }

                        if (dialog.Password.Length > 0)
                            await auth.ChangePasswordAsync(effectiveUsername, dialog.CurrentLoginPassword, dialog.Password);
                    }
                    else
                    {
                        if (usernameChanged)
                            await auth.SetUsernameForUserAsync(user.Id, dialog.Username);

                        if (dialog.Password.Length > 0)
                            await auth.SetPasswordForUserAsync(user.Id, dialog.Password);
                    }
                }

                if (dialog.OperationsPassword.Length > 0)
                {
                    if (restrictFields)
                        await opsGate.SetPasswordAsync(
                            dialog.CurrentOperationsPassword.Length > 0 ? dialog.CurrentOperationsPassword : null,
                            dialog.OperationsPassword);
                    else
                        await opsGate.SetPasswordForUserAsync(user.Id, dialog.OperationsPassword);
                }

                await LoadAsync();
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "خطأ في التعديل");
            }
        }

        [RelayCommand]
        private async Task ToggleActiveAsync(DepartmentAccountRow? row)
        {
            if (!CanManage || row is null) return;

            var message = row.IsActive
                ? $"إيقاف الحساب \"{row.FullName}\"؟\nهيختفي من القايمة لكن سجلاته التاريخية هتفضل محفوظة."
                : $"إعادة تفعيل الحساب \"{row.FullName}\"؟";

            if (!Notify.Ask(message, "تأكيد")) return;

            using var scope = _scopeFactory.CreateScope();
            var mgmt = scope.ServiceProvider.GetRequiredService<WorkerManagementService>();

            if (row.IsActive)
                await mgmt.DeactivateWorkerAsync(row.WorkerId);
            else
                await mgmt.ReactivateWorkerAsync(row.WorkerId);

            await LoadAsync();
        }

        /// <summary>
        /// يشيل الحساب نهائيًا — نفس بوابة كلمة السر وسجل العمليات اللي
        /// حذف عامل عادي بيمر عليها (شوف WorkersViewModel.DeleteWorkerAsync).
        /// حذف الحساب بيشيل حساب دخوله معاه (Cascade على AppUser.WorkerId → SetNull
        /// عمره ما بيسيب حساب دخول بلا حساب إداري يقدر يستخدمه حد تاني بالغلط —
        /// شوف WorkerManagementService.DeleteWorkerAsync لقاعدة الحذف نفسها).
        /// </summary>
        [RelayCommand]
        private async Task DeleteAccountAsync(DepartmentAccountRow? row)
        {
            if (!CanManage || row is null) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var gate = scope.ServiceProvider.GetRequiredService<OperationsPasswordService>();

                var input = SensitiveActionDialog.Ask(
                    Application.Current.MainWindow,
                    "حذف حساب إداري",
                    $"{row.FullName} — هيختفي من كل القوايم. سجلاته القديمة هتفضل محفوظة ومقروءة.",
                    SensitiveActionKind.Delete,
                    await gate.IsConfiguredAsync());

                if (input is null) return;

                var mgmt = scope.ServiceProvider.GetRequiredService<WorkerManagementService>();
                var result = await mgmt.DeleteWorkerAsync(row.WorkerId, input.Password, input.Reason);

                if (!result.IsDeleted)
                {
                    Notify.Warn(result.Message, "مش هينفع");
                    return;
                }

                // حذف نهائي فعلي = مفيش حساب إداري نهائي، فمفيش لزوم لحساب
                // دخول معلّق بيوزر وباسورد مفيدين لحد. لو كان إيقاف بس
                // (سجلات قديمة)، الحساب فاضل موجود وميقدرش يدخل بيه أصلاً
                // (AuthService.ValidateLoginAsync بترفض الحسابات الموقوفة)
                if (result.WasPermanent)
                    await scope.ServiceProvider.GetRequiredService<AuthService>()
                        .DeleteUserForWorkerAsync(row.WorkerId);

                await LoadAsync();
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "خطأ في الحذف");
            }
        }
    }
}
