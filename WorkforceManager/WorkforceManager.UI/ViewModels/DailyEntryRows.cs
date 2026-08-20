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

using WorkforceManager.Core.Helpers;

namespace WorkforceManager.UI.ViewModels
{
    // صفوف شاشة التسجيل اليومي: المنتجات والسجلات والحضور والجزاءات والسلف.
    // كلها بتتعرض في تبويبات DailyEntryView.

    public class ProductOption
    {
        public int ProductId { get; init; }
        public string Name { get; init; } = "";
        public List<StageEntryOption> Stages { get; init; } = new();

        /// <summary>عامل الرص الثابت الافتراضي بتاع المنتج (اختياري) — بيتحط تاجه تلقائيًا على مرحلة الرص كل يوم</summary>
        public int? RackingWorkerId { get; init; }
    }

    /// <summary>مرحلة في قوائم الاختيار (النطاقات) — برقم ترتيبها في خط الإنتاج</summary>
    public class StageEntryOption
    {
        public int StageId { get; init; }
        public string StageName { get; init; } = "";
        public int PiecesPerWorkday { get; init; }
        public int DisplayOrder { get; init; }

        /// <summary>
        /// مرحلة رص إدارية — بطاقتها في شاشة الإنتاج اليومي بلا نطاق/قطع
        /// خالص، وتاجها بيسجل يومية مباشرة. شوف
        /// <see cref="WorkforceManager.Core.Models.ProductionStage.IsRackingStage"/>.
        /// </summary>
        public bool IsRackingStage { get; init; }

        /// <summary>الاسم المعروض في قوائم "من/إلى": الترتيب + الاسم</summary>
        public string Display => $"{DisplayOrder}. {StageName}";
    }

    /// <summary>سجل إنتاج واحد في تبويب "سجلات اليوم" — للمراجعة والتصحيح</summary>
    public class DayRecordRow
    {
        public int RecordId { get; init; }
        public int ProductId { get; init; }
        public string ProductName { get; init; } = "";
        public string WorkerName { get; init; } = "";
        public string StageDisplay { get; init; } = "";
        public int PieceCount { get; init; }
        public int QuotaAtEntry { get; init; }
        public decimal Workdays { get; init; }

        /// <summary>يوم السجل نفسه — مهم بس لما الفترة المعروضة أسبوع أو شهر (أكتر من يوم واحد)</summary>
        public DateTime Date { get; init; }
        public string DateText => Date.ToString("dd/MM");
    }

    /// <summary>
    /// خيار فلتر منتج في "سجلات اليوم" — null = كل المنتجات. القائمة
    /// نفسها بتتبني من منتجات الفترة المعروضة بس (شوف
    /// DailyEntryViewModel.LoadRecordsTabAsync)، مش كل منتجات البرنامج.
    /// </summary>
    public record RecordsProductOption(int? ProductId, string Display);

    /// <summary>خيار وقت انتهاء الشغل في قائمة العامل بالساعة</summary>
    /// <summary>
    /// زرار حالة واحد في سطر الحضور. الاختيار مانع للجمع (حالة واحدة
    /// لليوم — B1): تعليم واحد بيشيل التعليم عن الباقي، وتعليمه تاني
    /// بيلغي التسجيل ويرجّع السطر لـ"بدون تسجيل".
    /// </summary>
    public partial class AttendanceStatusChoice : ObservableObject
    {
        private readonly AttendanceRow _row;

        public AttendanceStatusChoice(AttendanceRow row, AttendanceStatus status)
        {
            _row = row;
            Status = status;
            Display = status.ToArabicName();
            AccentColor = AttendanceVisuals.ColorFor(status);
            Icon = AttendanceVisuals.IconFor(status);
        }

        public AttendanceStatus Status { get; }
        public string Display { get; }

        /// <summary>
        /// لون الحالة لما تتعلّم (أخضر حاضر / أصفر بإذن / أحمر بدون إذن).
        /// نص hex زي <c>AdjustmentRow.TypeColor</c> — WPF بيحوّله لفرشاة لوحده.
        /// </summary>
        public string AccentColor { get; }

        /// <summary>أيقونة MaterialDesign اللي بتوصف الحالة (بتتقري أسرع من النص)</summary>
        public string Icon { get; }

        /// <summary>بيوقف إبلاغ السطر وإحنا بنعدّل الاختيار برمجيًا (منع تكرار لا نهائي)</summary>
        private bool _suppressNotify;

        [ObservableProperty]
        private bool _isSelected;

        partial void OnIsSelectedChanged(bool value)
        {
            if (_suppressNotify) return;
            _row.OnChoiceToggled(this, value);
        }

        /// <summary>تعليم/إلغاء من غير ما نرجّع نداء للسطر (بيستخدمه السطر وهو بيوحّد الاختيار)</summary>
        internal void SetSelectedSilently(bool selected)
        {
            _suppressNotify = true;
            try { IsSelected = selected; }
            finally { _suppressNotify = false; }
        }
    }

    /// <summary>
    /// اختصار شيفت للعامل بالساعة (شيفت عادي / لحد 8م / لحد 12).
    /// نفس منطق الاختيار المانع للجمع بتاع الحالات — شيفت واحد بس في اليوم.
    /// </summary>
    public partial class ShiftChoice : ObservableObject
    {
        private readonly AttendanceRow _row;

        public ShiftChoice(AttendanceRow row, int endHour24, string label)
        {
            _row = row;
            EndHour24 = endHour24;
            Display = $"{label} ({HourlyWorkdayService.ComputeWorkdays(endHour24)} يومية)";
        }

        public int EndHour24 { get; }
        public string Display { get; }

        private bool _suppressNotify;

        [ObservableProperty]
        private bool _isSelected;

        partial void OnIsSelectedChanged(bool value)
        {
            if (_suppressNotify) return;
            _row.OnShiftToggled(this, value);
        }

        internal void SetSelectedSilently(bool selected)
        {
            _suppressNotify = true;
            try { IsSelected = selected; }
            finally { _suppressNotify = false; }
        }
    }

    /// <summary>
    /// سطر حضور لعامل واحد في القائمة الموحّدة (بالقطعة أو بالساعة).
    ///
    /// الحالات المعروضة بتيجي من <see cref="AttendanceStatusCatalog"/>
    /// حسب نوع العامل — مش مكتوبة هنا. والعامل بالساعة بيزود عليه
    /// اختصارات الشيفت عشان يتسجل شغله من نفس الشاشة من غير تبويب منفصل.
    /// </summary>
    public partial class AttendanceRow : ObservableObject
    {
        /// <summary>بيمنع التكرار اللانهائي وإحنا بنلغي تعليم باقي الاختيارات</summary>
        private bool _syncing;

        public AttendanceRow(int workerId, string fullName, bool isHourly, string roleText)
        {
            WorkerId = workerId;
            FullName = fullName;
            IsHourly = isHourly;
            RoleText = roleText;

            StatusChoices = AttendanceStatusCatalog.ForWorker(isHourly)
                .Select(status => new AttendanceStatusChoice(this, status))
                .ToList();

            ShiftChoices = isHourly
                ? HourlyWorkdayService.ShiftPresets
                    .Select(preset => new ShiftChoice(this, preset.EndHour24, preset.Label))
                    .ToList()
                : new List<ShiftChoice>();
        }

        public int WorkerId { get; }
        public string FullName { get; }

        /// <summary>عامل بالساعة؟ (بيحدد الحالات المعروضة وظهور اختصارات الشيفت)</summary>
        public bool IsHourly { get; }

        /// <summary>نوع العامل للعرض: "بالقطعة" أو دوره بالساعة (رص/جودة/تدريب)</summary>
        public string RoleText { get; }

        /// <summary>أول حرفين من الاسم — بيتعرضوا في الدايرة جنب كل عامل بدل صورة</summary>
        public string Initials => NameInitials.From(FullName);
        /// <summary>لون الشريط الجانبي للبطاقة = لون الحالة المختارة (رمادي لو لسه مفيش)</summary>
        public string StatusColor => AttendanceVisuals.ColorFor(SelectedStatus);

        public IReadOnlyList<AttendanceStatusChoice> StatusChoices { get; }
        public IReadOnlyList<ShiftChoice> ShiftChoices { get; }

        /// <summary>الحالة المختارة دلوقتي (null = بدون تسجيل)</summary>
        public AttendanceStatus? SelectedStatus =>
            StatusChoices.FirstOrDefault(c => c.IsSelected)?.Status;

        /// <summary>وقت انتهاء الشيفت المختار للعامل بالساعة (null = مفيش شغل ساعة النهارده)</summary>
        public int? SelectedEndHour =>
            ShiftChoices.FirstOrDefault(c => c.IsSelected)?.EndHour24;

        // ------- الحضور التلقائي من الشغل المسجّل -------

        /// <summary>
        /// العامل ده له شغل مسجّل النهارده (إنتاج على مراحل أو شغل ساعة).
        /// بيتحدد من القاعدة المشتركة في AttendanceAutomationService.
        /// </summary>
        public bool HasLoggedWork { get; init; }

        /// <summary>الشرح اللي بيظهر جنب العامل اللي اتعلّم "حاضر" تلقائيًا</summary>
        public string WorkNote => HasLoggedWork ? "له شغل مسجّل النهارده" : "";

        /// <summary>الحالة المحفوظة فعليًا في قاعدة البيانات (لمعرفة إذا كان فيه تعديل غير محفوظ)</summary>
        public AttendanceStatus? SavedStatus { get; init; }

        /// <summary>
        /// الشيفت المحفوظ فعليًا للعامل بالساعة (null = مفيش شغل ساعة
        /// محفوظ في اليوم ده). لازم زي <see cref="SavedStatus"/> بالظبط:
        /// من غيره الحفظ مش هيعرف إن العامل بالساعة اتغيّر شيفته.
        /// </summary>
        public int? SavedEndHour { get; init; }

        // ------- منطق الاختيار المانع للجمع -------

        internal void OnChoiceToggled(AttendanceStatusChoice toggled, bool isSelected)
        {
            if (_syncing) return;

            _syncing = true;
            try
            {
                // حالة واحدة بس في اليوم: تعليم واحدة بيشيل الباقي
                if (isSelected)
                    foreach (var other in StatusChoices.Where(c => !ReferenceEquals(c, toggled)))
                        other.SetSelectedSilently(false);
            }
            finally
            {
                _syncing = false;
            }

            RaiseStatusVisualsChanged();
        }

        internal void OnShiftToggled(ShiftChoice toggled, bool isSelected)
        {
            if (_syncing) return;

            _syncing = true;
            try
            {
                if (isSelected)
                {
                    foreach (var other in ShiftChoices.Where(c => !ReferenceEquals(c, toggled)))
                        other.SetSelectedSilently(false);

                    // اختيار شيفت معناه العامل اشتغل — يبقى حاضر بالبديهة
                    SelectStatusSilently(AttendanceStatus.Present);
                }
            }
            finally
            {
                _syncing = false;
            }

            OnPropertyChanged(nameof(SelectedEndHour));
            OnPropertyChanged(nameof(HasUnsavedChange));
            RaiseStatusVisualsChanged();
        }

        /// <summary>يحدد حالة من غير ما يشغّل منطق المزامنة (للتحميل الأولي والحضور التلقائي)</summary>
        public void SelectStatusSilently(AttendanceStatus? status)
        {
            foreach (var choice in StatusChoices)
                choice.SetSelectedSilently(choice.Status == status);

            RaiseStatusVisualsChanged();
        }

        /// <summary>يحدد شيفت من غير منطق المزامنة (للتحميل الأولي)</summary>
        public void SelectShiftSilently(int? endHour24)
        {
            foreach (var choice in ShiftChoices)
                choice.SetSelectedSilently(choice.EndHour24 == endHour24);

            OnPropertyChanged(nameof(SelectedEndHour));
            OnPropertyChanged(nameof(HasUnsavedChange));
        }

        /// <summary>
        /// فيه تعديل لسه متحفظش؟ بيتحكم في تلوين السطر **وفي اللي
        /// بيتبعت للحفظ**.
        ///
        /// الشيفت داخل في الحساب: العامل بالساعة ممكن يفضل "حاضر" زي
        /// ما هو وشيفته يتغيّر من 4 لـ 6، وده تعديل حقيقي بيغيّر
        /// يومياته وأجره.
        /// </summary>
        public bool HasUnsavedChange =>
            SelectedStatus != SavedStatus || SelectedEndHour != SavedEndHour;

        /// <summary>
        /// بيبلّغ كل الخصائص اللي بتتغير مع الحالة (اللون + التعديل غير
        /// المحفوظ). مكان واحد عشان أي تغيير للحالة يحدّث البطاقة كاملة،
        /// ومنسناش خاصية.
        /// </summary>
        private void RaiseStatusVisualsChanged()
        {
            OnPropertyChanged(nameof(SelectedStatus));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(HasUnsavedChange));
            StatusChanged?.Invoke();
        }

        /// <summary>بيتنادى مع أي تغيير حالة — الشاشة الأم بتحدّث عدّادات الملخص</summary>
        public event Action? StatusChanged;
    }

    /// <summary>خيار خصم جزاء في القائمة المنسدلة</summary>
    public class DeductionOption
    {
        public DeductionOption(PenaltyDeduction value) => Value = value;
        public PenaltyDeduction Value { get; }
        public string Display => Value.ToArabicName();
    }

    /// <summary>جزاء واحد في قائمة جزاءات اليوم</summary>
    public class PenaltyRow
    {
        public int PenaltyId { get; init; }
        public string WorkerName { get; init; } = "";
        public string Reason { get; init; } = "";
        public string DeductionName { get; init; } = "";
    }

    /// <summary>خيار نوع الحركة (سلفة/حافز) في القائمة المنسدلة</summary>
    public class AdjustmentTypeOption
    {
        public AdjustmentTypeOption(WageAdjustmentType value) => Value = value;
        public WageAdjustmentType Value { get; }
        public string Display => Value.ToArabicName();
    }

    /// <summary>حركة سلفة/حافز واحدة في قائمة اليوم</summary>
    public class AdjustmentRow
    {
        public int AdjustmentId { get; init; }
        public string WorkerName { get; init; } = "";
        public string TypeName { get; init; } = "";
        /// <summary>مفتاح فرشاة من اللوحة — مش كود لون (شوف <see cref="ThemeBrush"/>)</summary>
        public string TypeColor { get; init; } = "InkBrush";
        public string AmountText { get; init; } = "";
        public string Note { get; init; } = "";
    }

    /// <summary>
    /// أقسام شريط ملخص الحضور. كل عدّاد في الشريط بيفتح القسم بتاعه،
    /// و<see cref="AttendanceFilter.All"/> معناها الشريط مقفول والكل ظاهر.
    /// </summary>
    /// <summary>شرايح قايمة الحضور. كلها بتشتغل على نفس القايمة الموحّدة.</summary>
    public enum AttendanceFilter
    {
        All,
        Present,
        Excused,
        Unexcused,

        /// <summary>لسه محدش سجّل ليهم حضور النهارده</summary>
        Unset,

        /// <summary>العمال بالساعة (رص/جودة/تدريب) — شريحة مش حالة حضور</summary>
        Hourly
    }
}
