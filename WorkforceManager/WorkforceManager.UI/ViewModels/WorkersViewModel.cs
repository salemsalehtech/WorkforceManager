using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.UI.Views;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// عقل شاشة العمال: تحميل القائمة بإحصائيات الأسبوع الحالي، البحث
    /// الموحّد (بالاسم أو بالمهارة)، ولوحة تفاصيل العامل المحدد (مهارات +
    /// هستوري أسبوعي + جزاءات)، وأوامر الإضافة/التعديل/الإيقاف.
    ///
    /// بنعمل Scope جديد لكل عملية (بدل حقن الخدمات مباشرة) عشان الـ
    /// DbContext يفضل قصير العمر — قاعدة أساسية لتفادي مشاكل التتبع
    /// والذاكرة في تطبيقات سطح المكتب طويلة التشغيل.
    /// </summary>
    public partial class WorkersViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public WorkersViewModel(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        // ------- حالة الشاشة -------

        /// <summary>نص البحث الموحّد: اسم عامل أو اسم مرحلة/منتج</summary>
        [ObservableProperty]
        private string _searchText = string.Empty;

        /// <summary>إظهار العمال الموقوفين (Soft Deleted) في القائمة</summary>
        [ObservableProperty]
        private bool _showInactive;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private WorkerRow? _selectedWorker;

        /// <summary>تفاصيل العامل المحدد (بتتحمّل لحظة اختياره من الجدول)</summary>
        [ObservableProperty]
        private WorkerDetail? _detail;

        public ObservableCollection<WorkerRow> Workers { get; } = new();

        /// <summary>عنوان الأسبوع الحالي المعروض فوق الجدول (من الخميس للأربع)</summary>
        [ObservableProperty]
        private string _weekTitle = string.Empty;

        // لما العامل المحدد يتغير، حمّل تفاصيله في اللوحة الجانبية
        partial void OnSelectedWorkerChanged(WorkerRow? value)
        {
            // تحميل بروفايل العامل المحدد (وأي خطأ بيظهر مش بيضيع بصمت)
            SafeAsync.Run(() => LoadDetailAsync(value));
        }

        // إعادة التحميل تلقائيًا عند تفعيل/إلغاء إظهار الموقوفين
        partial void OnShowInactiveChanged(bool value)
        {
            SafeAsync.Run(LoadAsync);
        }

        // ------- تحميل القائمة -------

        [RelayCommand]
        public async Task LoadAsync()
        {
            IsLoading = true;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var workerRepo = scope.ServiceProvider.GetRequiredService<IWorkerRepository>();
                var weeklyService = scope.ServiceProvider.GetRequiredService<WeeklySummaryService>();

                var (weekStart, weekEnd) = WeeklySummaryService.GetWorkWeekRange(DateTime.Today);
                WeekTitle = $"الأسبوع الحالي: من الخميس {weekStart:yyyy/MM/dd} إلى الأربعاء {weekEnd:yyyy/MM/dd}";

                // إحصائيات الأسبوع الحالي لكل العمال (استعلام واحد مجمّع)
                var weekly = await weeklyService.GetTeamWeeklySummaryAsync(DateTime.Today);
                var weeklyByWorker = weekly.ToDictionary(w => w.WorkerId);

                // القائمة: بحث موحّد أو كل النشطين
                var query = SearchText.Trim();
                List<Core.Models.Worker> workers;
                if (string.IsNullOrEmpty(query))
                {
                    workers = (await workerRepo.GetActiveWithSkillsAsync()).ToList();
                }
                else
                {
                    // البحث بالاسم والمهارة مع بعض ودمج النتايج من غير تكرار
                    var byName = await workerRepo.SearchByNameAsync(query);
                    var bySkill = await workerRepo.SearchBySkillAsync(query);
                    workers = byName.Concat(bySkill)
                        .GroupBy(w => w.Id)
                        .Select(g => g.First())
                        .OrderBy(w => w.FullName)
                        .ToList();
                }

                // ضم الموقوفين لو المستخدم طلب كده (بيظهروا بعلامة مميزة) —
                // مع تطبيق نفس كلمة البحث عليهم (بالاسم) عشان
                // البحث ميرجعش موقوفين مالهمش علاقة بالكلمة المكتوبة
                if (ShowInactive)
                {
                    var inactive = await workerRepo.FindAsync(w => !w.IsActive);
                    workers.AddRange(inactive
                        .Where(i => workers.All(w => w.Id != i.Id))
                        .Where(i => query.Length == 0 || i.FullName.Contains(query)));
                }

                Workers.Clear();
                foreach (var w in workers)
                {
                    weeklyByWorker.TryGetValue(w.Id, out var wk);
                    Workers.Add(new WorkerRow
                    {
                        WorkerId = w.Id,
                        FullName = w.FullName,
                        IsActive = w.IsActive,
                        PresentDays = wk?.PresentDays ?? 0,
                        AbsentWithPermissionDays = wk?.AbsentWithPermissionDays ?? 0,
                        AbsentWithoutPermissionDays = wk?.AbsentWithoutPermissionDays ?? 0,
                        PenaltyDeduction = wk?.PenaltyDeduction ?? 0,
                        NetWorkdays = wk?.NetWorkdays ?? 0,
                        BestMark = wk?.IsBestWorkerOfWeek == true ? "⭐" : ""
                    });
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private Task SearchAsync() => LoadAsync();

        // ------- لوحة التفاصيل -------

        private async Task LoadDetailAsync(WorkerRow? row)
        {
            if (row is null)
            {
                Detail = null;
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var workerRepo = scope.ServiceProvider.GetRequiredService<IWorkerRepository>();
            var productRepo = scope.ServiceProvider.GetRequiredService<IProductRepository>();
            var weeklyService = scope.ServiceProvider.GetRequiredService<WeeklySummaryService>();

            var worker = await workerRepo.GetWithSkillsAsync(row.WorkerId);
            if (worker is null) return;

            // الهستوري الأسبوعي: آخر 8 أسابيع كافية للعرض السريع في البروفايل
            var history = await weeklyService.GetWorkerWeeklyHistoryAsync(
                worker.Id, DateTime.Today.AddDays(-7 * 8), DateTime.Today);

            // كل المراحل المتاحة (منتج — مرحلة) لإضافة مهارة جديدة
            var products = await productRepo.GetActiveWithStagesAsync();
            var stageOptions = products
                .SelectMany(p => p.Stages.Select(s => new StageOption
                {
                    StageId = s.Id,
                    Display = $"{p.Name} — {s.StageName}"
                }))
                .ToList();

            Detail = new WorkerDetail
            {
                WorkerId = worker.Id,
                FullName = worker.FullName,
                PhoneNumber = worker.PhoneNumber ?? "—",
                HireDateText = worker.HireDate?.ToString("yyyy/MM/dd") ?? "—",
                SkillsNotes = worker.SkillsNotes ?? "",
                IsActive = worker.IsActive,
                HourlyRole = worker.HourlyRole,
                HourlyRoleText = worker.HourlyRole?.ToArabicName() ?? "",
                DailyWageEgp = worker.DailyWageEgp,
                WageText = worker.DailyWageEgp > 0
                    ? $"سعر اليومية: {worker.DailyWageEgp:N0} جنيه"
                    : "سعر اليومية: لم يُحدد",
                Skills = new ObservableCollection<SkillItem>(worker.Skills.Select(s => new SkillItem
                {
                    StageId = s.ProductionStageId,
                    Display = $"{s.ProductionStage.Product.Name} — {s.ProductionStage.StageName}"
                })),
                WeeklyHistory = new ObservableCollection<WeekHistoryItem>(history.Select(h => new WeekHistoryItem
                {
                    WeekTitle = $"من {h.WeekStart:MM/dd} إلى {h.WeekEnd:MM/dd}",
                    Produced = h.ProducedWorkdays,
                    AbsenceDeduction = h.AbsenceDeduction,
                    PenaltyDeduction = h.PenaltyDeduction,
                    Net = h.NetWorkdays,
                    // أجر الأسبوع بالجنيه (بيظهر بس لو ليه سعر يومية)
                    WageText = h.DailyWageEgp > 0 ? $"{h.NetWageEgp:N0} ج" : "",
                    BestMark = h.IsBestWorkerOfWeek ? "⭐" : "",
                    // تفصيل المراحل اللي اشتغل عليها الأسبوع ده (بيظهر تحت السطر)
                    BreakdownText = string.Join("، ", h.Breakdown.Select(b => $"{b.ProductName}/{b.StageName}: {b.PieceCount} قطعة")),
                    // جزاءات الأسبوع بأسبابها
                    PenaltiesText = string.Join("، ", h.Penalties.Select(p => $"{p.Reason} ({p.DeductionName})"))
                })),
                StageOptions = stageOptions
            };
        }

        // ------- أوامر الإدارة -------

        [RelayCommand]
        private async Task AddWorkerAsync()
        {
            var dialog = new WorkerEditDialog { Owner = Application.Current.MainWindow };
            if (dialog.ShowDialog() != true) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mgmt = scope.ServiceProvider.GetRequiredService<WorkerManagementService>();
                await mgmt.CreateWorkerAsync(
                    dialog.WorkerName, dialog.PhoneNumber,
                    dialog.HireDate, dialog.SkillsNotes, dialog.HourlyRole, dialog.DailyWageEgp);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "خطأ في إضافة العامل", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        private async Task EditWorkerAsync()
        {
            if (SelectedWorker is null || Detail is null) return;

            var dialog = new WorkerEditDialog
            {
                Owner = Application.Current.MainWindow,
                Title = "تعديل بيانات عامل"
            };
            dialog.LoadWorker(Detail.FullName,
                Detail.PhoneNumber == "—" ? null : Detail.PhoneNumber,
                Detail.HireDateText == "—" ? null : DateTime.Parse(Detail.HireDateText),
                Detail.SkillsNotes, Detail.HourlyRole, Detail.DailyWageEgp);

            if (dialog.ShowDialog() != true) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mgmt = scope.ServiceProvider.GetRequiredService<WorkerManagementService>();
                await mgmt.UpdateWorkerAsync(
                    SelectedWorker.WorkerId, dialog.WorkerName,
                    dialog.PhoneNumber, dialog.HireDate, dialog.SkillsNotes, dialog.HourlyRole, dialog.DailyWageEgp);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "خطأ في تعديل العامل", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        private async Task ToggleActiveAsync()
        {
            if (SelectedWorker is null) return;

            // رسالة تأكيد مختلفة حسب الحالة الحالية — الإيقاف قرار أكبر من التفعيل
            var isDeactivating = SelectedWorker.IsActive;
            var message = isDeactivating
                ? $"إيقاف العامل \"{SelectedWorker.FullName}\"؟\nهيختفي من القوائم لكن كل سجلاته التاريخية هتفضل محفوظة."
                : $"إعادة تفعيل العامل \"{SelectedWorker.FullName}\"؟";

            if (MessageBox.Show(message, "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            using var scope = _scopeFactory.CreateScope();
            var mgmt = scope.ServiceProvider.GetRequiredService<WorkerManagementService>();

            if (isDeactivating)
                await mgmt.DeactivateWorkerAsync(SelectedWorker.WorkerId);
            else
                await mgmt.ReactivateWorkerAsync(SelectedWorker.WorkerId);

            await LoadAsync();
        }

        [RelayCommand]
        private async Task AddSkillAsync()
        {
            if (Detail?.SelectedStageOption is null) return;

            using var scope = _scopeFactory.CreateScope();
            var mgmt = scope.ServiceProvider.GetRequiredService<WorkerManagementService>();
            await mgmt.AssignSkillAsync(Detail.WorkerId, Detail.SelectedStageOption.StageId);

            // إعادة تحميل التفاصيل عشان المهارة الجديدة تظهر فورًا
            await LoadDetailAsync(SelectedWorker);
        }

        [RelayCommand]
        private async Task RemoveSkillAsync(SkillItem? skill)
        {
            if (skill is null || Detail is null) return;

            using var scope = _scopeFactory.CreateScope();
            var mgmt = scope.ServiceProvider.GetRequiredService<WorkerManagementService>();
            await mgmt.RemoveSkillAsync(Detail.WorkerId, skill.StageId);

            await LoadDetailAsync(SelectedWorker);
        }
    }

    // ------- نماذج العرض (خاصة بالشاشة دي بس) -------

    /// <summary>سطر واحد في جدول العمال: بيانات العامل + أرقام الأسبوع الحالي</summary>
    public class WorkerRow
    {
        public int WorkerId { get; init; }
        public string FullName { get; init; } = "";
        public bool IsActive { get; init; }
        public int PresentDays { get; init; }
        public int AbsentWithPermissionDays { get; init; }
        public int AbsentWithoutPermissionDays { get; init; }
        public decimal PenaltyDeduction { get; init; }
        public decimal NetWorkdays { get; init; }
        public string BestMark { get; init; } = "";

        /// <summary>نص الحالة المعروض في الجدول</summary>
        public string StatusText => IsActive ? "نشط" : "موقوف";
    }

    /// <summary>تفاصيل العامل المعروضة في اللوحة الجانبية (البروفايل)</summary>
    public partial class WorkerDetail : ObservableObject
    {
        public int WorkerId { get; init; }
        public string FullName { get; init; } = "";
        public string PhoneNumber { get; init; } = "";
        public string HireDateText { get; init; } = "";
        public string SkillsNotes { get; init; } = "";
        public bool IsActive { get; init; }

        /// <summary>دور العامل بالساعة (null = عامل إنتاج بالقطعة)</summary>
        public Core.Enums.HourlyRole? HourlyRole { get; init; }

        /// <summary>نص الدور بالساعة للعرض في البروفايل (فاضي لعامل الإنتاج)</summary>
        public string HourlyRoleText { get; init; } = "";

        /// <summary>سعر يومية العامل بالجنيه</summary>
        public decimal DailyWageEgp { get; init; }

        /// <summary>نص سعر اليومية للعرض في البروفايل</summary>
        public string WageText { get; init; } = "";

        /// <summary>هل هو عامل بالساعة؟ (لإظهار شارة في البروفايل)</summary>
        public bool IsHourly => HourlyRole is not null;

        public ObservableCollection<SkillItem> Skills { get; init; } = new();
        public ObservableCollection<WeekHistoryItem> WeeklyHistory { get; init; } = new();
        public List<StageOption> StageOptions { get; init; } = new();

        /// <summary>المرحلة المختارة في قائمة "إضافة مهارة"</summary>
        [ObservableProperty]
        private StageOption? _selectedStageOption;
    }

    /// <summary>مهارة واحدة معروضة في البروفايل (منتج — مرحلة)</summary>
    public class SkillItem
    {
        public int StageId { get; init; }
        public string Display { get; init; } = "";
    }

    /// <summary>اختيار مرحلة من قائمة الإضافة (منتج — مرحلة)</summary>
    public class StageOption
    {
        public int StageId { get; init; }
        public string Display { get; init; } = "";
    }

    /// <summary>ملخص أسبوع واحد في هستوري العامل</summary>
    public class WeekHistoryItem
    {
        public string WeekTitle { get; init; } = "";
        public decimal Produced { get; init; }
        public decimal AbsenceDeduction { get; init; }
        public decimal PenaltyDeduction { get; init; }
        public decimal Net { get; init; }
        public string WageText { get; init; } = "";
        public string BestMark { get; init; } = "";
        public string BreakdownText { get; init; } = "";
        public string PenaltiesText { get; init; } = "";

        /// <summary>هل فيه تفاصيل إنتاج/جزاءات تستحق العرض؟ (لإخفاء السطور الفاضية)</summary>
        public bool HasBreakdown => !string.IsNullOrEmpty(BreakdownText);
        public bool HasPenalties => !string.IsNullOrEmpty(PenaltiesText);
        public bool HasWage => !string.IsNullOrEmpty(WageText);
    }
}
