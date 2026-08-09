using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Helpers;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Data;
using WorkforceManager.UI.Views;

namespace WorkforceManager.UI.ViewModels
{
    // صفوف وخيارات قائمة العمال: الفلاتر والترتيب وبطاقة العامل الواحد.
    // بتتقرا من WorkersViewModel وWorkersView.xaml.

    public enum WorkerFilter { All, PieceRate, Hourly, Inactive }

    /// <summary>طرق ترتيب قائمة العمال</summary>
    public enum WorkerSort { Name, NetDesc, NetAsc, AbsenceDesc, SkillsDesc }

    /// <summary>خيار ترتيب في القائمة المنسدلة</summary>
    public record WorkerSortOption(WorkerSort Value, string Display);

    // ------- خيارات الفلاتر المركّبة -------
    // كلها بتبدأ بخيار "الكل" اللي قيمته null = الفلتر مش مفعّل

    public record StageFilterOption(int? StageId, string Display);

    public record ProductFilterOption(int? ProductId, string Display);

    public record StarsFilterOption(int? MinStars, string Display);

    public record AttendanceFilterOption(AttendanceStatus? Status, string Display);

    /// <summary>بطاقة عامل واحد في القائمة: بياناته + أرقام الأسبوع الحالي + تنبيهاته</summary>
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
        public bool IsBestOfWeek { get; init; }

        /// <summary>عامل بالساعة؟ (رص/جودة/تدريب)</summary>
        public bool IsHourly { get; init; }
        public string HourlyRoleText { get; init; } = "";
        public decimal DailyWageEgp { get; init; }
        public int SkillsCount { get; init; }

        /// <summary>كل مهاراته وملاحظاته في نص واحد — للبحث اللحظي من غير حسابات</summary>
        public string SkillsSearchText { get; init; } = "";

        /// <summary>صورة العامل (null = تتعرض الحروف الأولى بدلها)</summary>
        public byte[]? PhotoData { get; init; }

        // ------- بيانات الفلترة -------

        /// <summary>المراحل اللي العامل مؤهل ليها</summary>
        public IReadOnlySet<int> StageIds { get; init; } = new HashSet<int>();

        /// <summary>المنتجات اللي عنده مهارة في أي مرحلة منها</summary>
        public IReadOnlySet<int> ProductIds { get; init; } = new HashSet<int>();

        /// <summary>متوسط نجومه على كل مهاراته (0 = مالوش مهارات)</summary>
        public decimal AverageStars { get; init; }

        /// <summary>حالة حضوره النهارده (null = مفيش تسجيل)</summary>
        public AttendanceStatus? TodayStatus { get; init; }

        /// <summary>المنتج اللي تقييمه فيه الأعلى (فاضي = مالوش مهارات)</summary>
        public string TopSkillProduct { get; init; } = "";

        public bool HasTopSkill => TopSkillProduct.Length > 0;

        /// <summary>نفس الصف بالشكل اللي قاعدة الفلترة بتفهمه</summary>
        public WorkerFilterSubject FilterSubject => new()
        {
            WorkerId = WorkerId,
            IsActive = IsActive,
            IsHourly = IsHourly,
            StageIds = StageIds,
            ProductIds = ProductIds,
            AverageStars = AverageStars,
            TodayStatus = TodayStatus
        };

        // ------- العرض -------

        public string StatusText => IsActive ? "نشط" : "موقوف";
        public string TypeText => IsHourly ? HourlyRoleText : "بالإنتاج";

        /// <summary>أول حرفين من الاسم للدايرة (نفس أسلوب شاشة الحضور)</summary>
        public string Initials => NameInitials.From(FullName);
        // سعر اليومية مقصود إنه مش معروض على الكارت — بيان حساس، بيتشاف من
        // البروفايل بس. DailyWageEgp باقي هنا للتنبيه (HasNoWage) والترتيب فقط.
        public string SkillsText => IsHourly ? "بالساعة" : $"{SkillsCount} مهارة";

        /// <summary>
        /// مفتاح فرشاة الصافي من اللوحة — مش كود لون. الصفر بيبقى باهت
        /// عشان الأرقام اللي فيها شغل هي اللي تشد العين.
        /// </summary>
        public string NetColor => NetWorkdays switch
        {
            > 0 => "GoodBrush",
            < 0 => "DangerBrush",
            _ => "InkFaintBrush"
        };

        /// <summary>
        /// نفس المنطق لأرقام الحضور والغياب على الكارت: صفر = باهت،
        /// وغير كده = اللون اللي بيعبّر عنه. رقم زيرو بلون قوي بيسحب
        /// نظر لحاجة مش موجودة أصلًا.
        /// </summary>
        public string PresentColor => PresentDays > 0 ? "GoodBrush" : "InkFaintBrush";

        public string AbsentColor => AbsentWithoutPermissionDays > 0 ? "DangerBrush" : "InkFaintBrush";

        // ------- التنبيهات -------

        /// <summary>مفيش سعر يومية — العامل ده هياخد صفر جنيه في كشف الأجور</summary>
        public bool HasNoWage => DailyWageEgp <= 0;

        /// <summary>
        /// عامل بالقطعة من غير أي مهارة مربوطة — مش هيظهر في أي رحلة إنتاج
        /// خالص (رحلة الإنتاج بتعرض المؤهلين بس)، فعمليًا مينفعش يشتغل.
        /// العامل بالساعة مستثنى لأنه أصلاً مالوش مهارات بالتصميم.
        /// </summary>
        public bool HasNoSkills => !IsHourly && SkillsCount == 0;

        public bool NeedsAttention => HasNoWage || HasNoSkills;

        /// <summary>نص التنبيه المعروض على البطاقة</summary>
        public string AttentionText => (HasNoWage, HasNoSkills) switch
        {
            (true, true) => "مفيش سعر يومية ولا مهارات",
            (true, false) => "مفيش سعر يومية — هياخد صفر في كشف الأجور",
            (false, true) => "مفيش مهارات — مش هيظهر في رحلات الإنتاج",
            _ => ""
        };
    }

    /// <summary>تفاصيل العامل المعروضة في اللوحة الجانبية (البروفايل)</summary>
}
