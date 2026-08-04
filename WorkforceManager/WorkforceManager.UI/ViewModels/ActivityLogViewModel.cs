using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

        /// <summary>كل أحداث الفترة المحمّلة — الفلترة بتشتغل على النسخة دي</summary>
        private List<ActivityEvent> _loaded = new();

        public ActivityLogViewModel(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
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

        /// <summary>بيحمّل أحداث الفترة المختارة</summary>
        [RelayCommand]
        public async Task LoadAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var log = scope.ServiceProvider.GetRequiredService<ActivityLogService>();

            _loaded = (await log.GetByRangeAsync(FromDate, ToDate)).ToList();
            ApplyFilter();
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

            var visible = query.Length == 0
                ? _loaded
                : _loaded.Where(e =>
                    (e.EntityName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    e.Actor.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (e.Reason?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                    .ToList();

            Events.Clear();
            foreach (var item in visible) Events.Add(new ActivityEventRow(item));

            IsEmpty = Events.Count == 0;
            SummaryText = _loaded.Count == 0
                ? "مفيش عمليات مسجّلة في الفترة دي"
                : $"{Events.Count} عملية من {_loaded.Count} في الفترة";
        }
    }

    /// <summary>سطر واحد في جدول السجل — بيحوّل الحدث لنص وأيقونة ولون</summary>
    public class ActivityEventRow
    {
        private readonly ActivityEvent _event;

        public ActivityEventRow(ActivityEvent source) => _event = source;

        public string When => _event.OccurredAt.ToString("yyyy/MM/dd — HH:mm");
        public string Actor => _event.Actor;
        public string EntityName => _event.EntityName ?? "—";
        public string Reason => _event.Reason ?? "—";
        public string Details => _event.Details ?? "";
        public bool HasDetails => !string.IsNullOrWhiteSpace(_event.Details);

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
            _ => _event.EventType.ToString()
        };

        /// <summary>الحذف بيتلوّن أحمر — أخطر نوع عملية وأول اللي المراجع بيدوّر عليه</summary>
        public bool IsDeletion => _event.EventType
            is ActivityEventType.ProductionDayDeleted
            or ActivityEventType.ProductionRecordDeleted
            or ActivityEventType.WorkerDeleted
            or ActivityEventType.ProductDeleted
            or ActivityEventType.StageDeleted
            or ActivityEventType.PenaltyDeleted;
    }
}
