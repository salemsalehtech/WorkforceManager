using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Models;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// عقل شاشة سجل العمليات: مين عمل إيه، إمتى، وليه.
    ///
    /// الشاشة دي هي الإجابة على "الشغل ده راح فين؟" — من غيرها الأحداث
    /// بتتسجّل في الداتابيز ومحدش بيقدر يقراها.
    ///
    /// الفلترة بتتم **في الذاكرة** بعد تحميل الفترة مرة واحدة: عدد
    /// الأحداث في اليوم محدود (عمليات حذف وتعديلات مالية بس)، فالتحميل
    /// المتكرر مع كل ضغطة فلتر مكانش هيضيف غير بطء.
    /// </summary>
    public partial class ActivityLogViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly CurrentUserContext _currentUser;

        /// <summary>كل أحداث الفترة المحمّلة — الفلترة بتشتغل على النسخة دي</summary>
        private List<ActivityEvent> _loaded = new();

        public ActivityLogViewModel(IServiceScopeFactory scopeFactory, CurrentUserContext currentUser)
        {
            _scopeFactory = scopeFactory;
            _currentUser = currentUser;
            _selectedEventGroup = EventGroups[0];
        }

        /// <summary>الأحداث المعروضة دلوقتي (بعد الفلترة والبحث)</summary>
        public ObservableCollection<ActivityEventRow> Events { get; } = new();

        [ObservableProperty]
        private DateTime _fromDate = DateTime.Today.AddDays(-30);

        [ObservableProperty]
        private DateTime _toDate = DateTime.Today;

        /// <summary>بحث فوري في اسم الكيان والفاعل والسبب</summary>
        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private string _summaryText = string.Empty;

        [ObservableProperty]
        private bool _isEmpty = true;

        partial void OnSearchTextChanged(string value) => ApplyFilter();

        // ------- فلتر نوع العملية -------
        // السجل بقى بيسجّل كل عملية ليها قيمة مش الحذف بس، فعدد
        // الأنواع بقى بيغرق اللي بيدوّر على نوع واحد. المجموعات هنا
        // بتجاوب الأسئلة اللي بتتسأل فعلًا: "مين مسح؟" و"مين لمس فلوس؟"

        public IReadOnlyList<EventGroupOption> EventGroups { get; } = new[]
        {
            new EventGroupOption(EventGroup.All, "كل العمليات"),
            new EventGroupOption(EventGroup.Money, "فلوس وأجور"),
            new EventGroupOption(EventGroup.Deletions, "حذف"),
            new EventGroupOption(EventGroup.Production, "إنتاج وحضور"),
            new EventGroupOption(EventGroup.Setup, "إضافة وإعدادات")
        };

        [ObservableProperty]
        private EventGroupOption? _selectedEventGroup;

        partial void OnSelectedEventGroupChanged(EventGroupOption? value) => ApplyFilter();

        /// <summary>
        /// بيقول للمستخدم إن السجل بيقصّر لوحده وقد إيه.
        ///
        /// من غير السطر ده، اللي بيدوّر على حدث من ٦ شهور ومبيلاقيهوش
        /// بيفتكر إن فيه عطل — مش إن ده إعداد هو اللي حاططه.
        /// بيتقرا من الإعدادات كل مرة الشاشة تتفتح عشان يتمشى مع أي تغيير.
        /// </summary>
        public string RetentionNote
        {
            get
            {
                var settings = Data.AppSettingsStore.Load();
                var routine = settings.ActivityLogRetentionDays;
                var money = settings.ActivityLogFinancialRetentionDays;

                if (routine <= 0 && money <= 0)
                    return "السجل مبيتمسحش منه حاجة — كل الأحداث بتتحفظ للأبد (من الإعدادات).";

                var parts = new List<string>();
                if (routine > 0) parts.Add($"أحداث الحذف بعد {routine} يوم");
                if (money > 0) parts.Add($"أحداث الفلوس بعد {money} يوم");

                return $"السجل بيتنضّف تلقائيًا: {string.Join("، و", parts)}. " +
                       "غيّرها من شاشة الإعدادات.";
            }
        }

        /// <summary>بيحمّل أحداث الفترة المختارة</summary>
        [RelayCommand]
        public async Task LoadAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var log = scope.ServiceProvider.GetRequiredService<ActivityLogService>();

            _loaded = (await log.GetByRangeAsync(FromDate, ToDate)).ToList();
            ApplyFilter();
            OnPropertyChanged(nameof(RetentionNote)); // الإعداد ممكن يكون اتغيّر من شاشة تانية

            // فتح الشاشة نفسه هو الفعل اللي بيصفّر شارة "عمليات جديدة"
            // على زرار السجل — مش لازم المستخدم يعمل حاجة تانية
            await log.MarkSeenAsync(_currentUser.AppUserId);
        }

        /// <summary>آخر 30 يوم (الافتراضي)</summary>
        [RelayCommand]
        private async Task ShowLastMonthAsync()
        {
            FromDate = DateTime.Today.AddDays(-30);
            ToDate = DateTime.Today;
            await LoadAsync();
        }

        /// <summary>النهارده بس</summary>
        [RelayCommand]
        private async Task ShowTodayAsync()
        {
            FromDate = DateTime.Today;
            ToDate = DateTime.Today;
            await LoadAsync();
        }

        /// <summary>بيطبّق البحث على الأحداث المحمّلة (من غير أي استعلام)</summary>
        private void ApplyFilter()
        {
            var query = SearchText?.Trim() ?? "";
            var group = SelectedEventGroup?.Group ?? EventGroup.All;

            var visible = _loaded
                .Where(e => EventGroups_Match(group, e.EventType))
                .Where(e => query.Length == 0
                    || (e.EntityName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                    || e.Actor.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || (e.Reason?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (e.Details?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();

            Events.Clear();
            foreach (var item in visible) Events.Add(new ActivityEventRow(item));

            IsEmpty = Events.Count == 0;
            SummaryText = _loaded.Count == 0
                ? "مفيش عمليات مسجّلة في الفترة دي"
                : $"{Events.Count} عملية من {_loaded.Count} في الفترة";
        }

        private static bool EventGroups_Match(EventGroup group, ActivityEventType type) => group switch
        {
            EventGroup.Money => type
                is ActivityEventType.WorkerWageChanged
                or ActivityEventType.ProductionPiecesEdited
                or ActivityEventType.PenaltySaved
                or ActivityEventType.PenaltyDeleted
                or ActivityEventType.WageAdjustmentSaved
                or ActivityEventType.WageAdjustmentDeleted,

            EventGroup.Deletions => type
                is ActivityEventType.ProductionDayDeleted
                or ActivityEventType.ProductionRecordDeleted
                or ActivityEventType.WorkerDeleted
                or ActivityEventType.ProductDeleted
                or ActivityEventType.StageDeleted
                or ActivityEventType.PenaltyDeleted
                or ActivityEventType.WageAdjustmentDeleted,

            EventGroup.Production => type
                is ActivityEventType.ProductionRecorded
                or ActivityEventType.AttendanceSaved
                or ActivityEventType.ProductionDayClosed
                or ActivityEventType.ProductionDayReopened
                or ActivityEventType.ScrapRecorded,

            EventGroup.Setup => type
                is ActivityEventType.WorkerCreated
                or ActivityEventType.ProductCreated
                or ActivityEventType.StageCreated
                or ActivityEventType.OperationsPasswordChanged,

            _ => true
        };
    }

    /// <summary>تقسيم العمليات لمجموعات بيتسأل عنها فعلًا</summary>
    public enum EventGroup
    {
        All,
        Money,
        Deletions,
        Production,
        Setup
    }

    public record EventGroupOption(EventGroup Group, string Display);

    /// <summary>سطر واحد في جدول السجل — بيحوّل الحدث لنص وأيقونة ولون</summary>
    public class ActivityEventRow
    {
        private readonly ActivityEvent _event;

        public ActivityEventRow(ActivityEvent source) => _event = source;

        public string When => _event.OccurredAt.ToString("yyyy/MM/dd — HH:mm");
        public string Actor => _event.Actor;
        public string EntityName => _event.EntityName ?? "—";
        public string Reason => _event.Reason ?? "";
        public string Details => _event.Details ?? "";

        /// <summary>
        /// معظم العمليات (تسجيل إنتاج، حضور...) مالهاش سبب أصلاً — بس
        /// الحذف والتعديلات الحساسة. عرض "السبب: —" على كل كارت كان
        /// بيغرق الكارت اللي فعلاً محتاج السبب وسط عشرات بلا معنى.
        /// </summary>
        public bool HasReason => !string.IsNullOrWhiteSpace(_event.Reason);
        public bool HasDetails => !string.IsNullOrWhiteSpace(_event.Details);

        /// <summary>مفيش سبب ولا تفاصيل — الكارت بيتلخّص في السطر الأول بس</summary>
        public bool HasBody => HasReason || HasDetails;

        /// <summary>وصف الحدث بالعربي — مصدر واحد للنص بدل ما كل شاشة تترجمه</summary>
        public string EventText => _event.EventType switch
        {
            ActivityEventType.ProductionDayDeleted => "حذف يوم إنتاج",
            ActivityEventType.ProductionRecordDeleted => "حذف سجل إنتاج",
            ActivityEventType.WorkerDeleted => "حذف عامل",
            ActivityEventType.ProductDeleted => "حذف منتج",
            ActivityEventType.StageDeleted => "حذف مرحلة",
            ActivityEventType.WorkerWageChanged => "تعديل أجر عامل",
            ActivityEventType.ProductionPiecesEdited => "تصحيح عدد قطع",
            ActivityEventType.PenaltySaved => "تسجيل جزاء",
            ActivityEventType.PenaltyDeleted => "حذف جزاء",
            ActivityEventType.WageAdjustmentSaved => "سلفة أو حافز",
            ActivityEventType.OperationsPasswordChanged => "تغيير كلمة سر العمليات",
            ActivityEventType.WageAdjustmentDeleted => "حذف سلفة أو حافز",
            ActivityEventType.ProductionRecorded => "تسجيل إنتاج",
            ActivityEventType.AttendanceSaved => "حفظ الحضور",
            ActivityEventType.ProductionDayClosed => "قفل يوم إنتاج",
            ActivityEventType.ProductionDayReopened => "فتح يوم مقفول",
            ActivityEventType.ScrapRecorded => "تسجيل هالك",
            ActivityEventType.WorkerCreated => "إضافة عامل",
            ActivityEventType.ProductCreated => "إضافة منتج",
            ActivityEventType.StageCreated => "إضافة مرحلة",
            _ => _event.EventType.ToString()
        };

        /// <summary>أيقونة الحدث — بتديه هوية بصرية فورية بدل ما القارئ يعتمد على النص بس</summary>
        public PackIconKind EventIcon => _event.EventType switch
        {
            ActivityEventType.ProductionDayDeleted => PackIconKind.CalendarRemoveOutline,
            ActivityEventType.ProductionRecordDeleted => PackIconKind.DeleteOutline,
            ActivityEventType.WorkerDeleted => PackIconKind.AccountRemoveOutline,
            ActivityEventType.ProductDeleted => PackIconKind.PackageVariantRemove,
            ActivityEventType.StageDeleted => PackIconKind.LayersRemove,
            ActivityEventType.WorkerWageChanged => PackIconKind.CurrencyUsd,
            ActivityEventType.ProductionPiecesEdited => PackIconKind.PencilOutline,
            ActivityEventType.PenaltySaved => PackIconKind.AlertOctagonOutline,
            ActivityEventType.PenaltyDeleted => PackIconKind.CloseCircleOutline,
            ActivityEventType.WageAdjustmentSaved => PackIconKind.CashPlus,
            ActivityEventType.OperationsPasswordChanged => PackIconKind.LockReset,
            ActivityEventType.WageAdjustmentDeleted => PackIconKind.CashRemove,
            ActivityEventType.ProductionRecorded => PackIconKind.ClipboardCheckOutline,
            ActivityEventType.AttendanceSaved => PackIconKind.CalendarCheckOutline,
            ActivityEventType.ProductionDayClosed => PackIconKind.LockOutline,
            ActivityEventType.ProductionDayReopened => PackIconKind.LockOpenOutline,
            ActivityEventType.ScrapRecorded => PackIconKind.DeleteSweepOutline,
            ActivityEventType.WorkerCreated => PackIconKind.AccountPlusOutline,
            ActivityEventType.ProductCreated => PackIconKind.PackageVariantPlus,
            ActivityEventType.StageCreated => PackIconKind.PlusBoxOutline,
            _ => PackIconKind.InformationOutline
        };

        /// <summary>الحذف بيتلوّن أحمر — أخطر نوع عملية وأول اللي المراجع بيدوّر عليه</summary>
        public bool IsDeletion => _event.EventType
            is ActivityEventType.ProductionDayDeleted
            or ActivityEventType.ProductionRecordDeleted
            or ActivityEventType.WorkerDeleted
            or ActivityEventType.ProductDeleted
            or ActivityEventType.StageDeleted
            or ActivityEventType.PenaltyDeleted
            or ActivityEventType.WageAdjustmentDeleted;

        /// <summary>
        /// حركة فلوس — بتتلوّن دهبي. المراجع بيدوّر على الاتنين دول:
        /// اللي اتمسح، واللي لمس أجر حد.
        /// </summary>
        public bool IsMoney => _event.EventType
            is ActivityEventType.WorkerWageChanged
            or ActivityEventType.ProductionPiecesEdited
            or ActivityEventType.PenaltySaved
            or ActivityEventType.WageAdjustmentSaved;

        /// <summary>مفتاح فرشاة من اللوحة — مش كود لون (شوف <see cref="ThemeBrush"/>)</summary>
        public string AccentColor =>
            IsDeletion ? "DangerBrush" : IsMoney ? "GoldDeepBrush" : "InkSoftBrush";
    }
}
