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

    /// <summary>الفترة المعروضة في الشاشة كلها — بتتحكم في يوميات/حضور كل عامل وكارت أحسن 3</summary>
    public enum WorkersPeriodGrain { Week, Month, Custom }

    /// <summary>طرق ترتيب قائمة العمال</summary>
    public enum WorkerSort { Custom, Name, NetDesc, NetAsc, AbsenceDesc, SkillsDesc }

    /// <summary>خيار ترتيب في القائمة المنسدلة</summary>
    public record WorkerSortOption(WorkerSort Value, string Display);

    // ------- خيارات الفلاتر المركّبة -------
    // كلها بتبدأ بخيار "الكل" اللي قيمته null = الفلتر مش مفعّل

    public record StageFilterOption(int? StageId, string Display);

    public record ProductFilterOption(int? ProductId, string Display);

    public record StarsFilterOption(int? MinStars, string Display);

    public record AttendanceFilterOption(AttendanceStatus? Status, string Display);

    /// <summary>
    /// بطاقة عامل واحد في القائمة: بياناته + تنبيهاته + أرقام الفترة
    /// المعروضة حاليًا (WorkersViewModel.PeriodGrain — أسبوع/شهر/مدة
    /// مخصوصة). الأسماء لسه فيها "Week" بس القيمة بقت لأي فترة مختارة.
    /// </summary>
    public class WorkerRow
    {
        public int WorkerId { get; init; }
        public string FullName { get; init; } = "";
        public bool IsActive { get; init; }

        /// <summary>ترتيب العامل المخصص (شاشة "ترتيب العمال") — شوف Worker.SortOrder</summary>
        public int SortOrder { get; init; }
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

        /// <summary>
        /// نسبة % تلخّص تقييم العامل بالنجوم = (متوسط النجوم ÷ 5) × 100.
        /// من رأي المدير (AverageStars) مباشرة — مش من إنتاج فعلي، فبتظهر
        /// لأي عامل عنده مهارة، مش بس اللي "اتقاسوا" من الإنتاج.
        /// </summary>
        public bool HasStarsPercent => AverageStars > 0;
        public string StarsPercentText => HasStarsPercent ? $"{Math.Round(AverageStars / 5m * 100m):0}%" : "";

        // ------- ألقاب "أحسن عامل" الرسمية (تفضل ثابتة لحد ما حد ياخدها) -------

        /// <summary>"🏆 أحسن عامل الأسبوع (من 10 أغسطس)" — فاضي لو مالوش لقب أسبوعي حاليًا</summary>
        public string CurrentWeeklyTitleText { get; init; } = "";
        public bool HasCurrentWeeklyTitle => CurrentWeeklyTitleText.Length > 0;

        /// <summary>"🏆 أحسن عامل الشهر (أغسطس)" — فاضي لو مالوش لقب شهري حاليًا</summary>
        public string CurrentMonthlyTitleText { get; init; } = "";
        public bool HasCurrentMonthlyTitle => CurrentMonthlyTitleText.Length > 0;

        /// <summary>إنتاج النهارده قلّ بشكل ملحوظ عن متوسط آخر أيام شغله (ProductionTrendService)</summary>
        public bool NeedsProductionReview { get; init; }
        public string ProductionReviewText { get; init; } = "";

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

    /// <summary>
    /// بطاقة فايز واحد في كارت "أحسن 3 عمال الأسبوع" — بيانات عرض بس
    /// (صورة/اسم/ترتيبه)، مبنية من WorkerWeeklySummaryDto لأسبوع معيّن +
    /// صورة العامل من WorkerRow المحمّلة أصلًا (الصورة ثابتة مش بتتغيّر
    /// بالأسبوع، فمحتاجناش نجيبها تاني عند تصفح أسبوع فات).
    /// </summary>
    public class BestWorkerCardRow
    {
        public int WorkerId { get; init; }
        public string FullName { get; init; } = "";
        public byte[]? PhotoData { get; init; }
        public int Rank { get; init; }

        public string Initials => NameInitials.From(FullName);
    }

    /// <summary>تفاصيل العامل المعروضة في اللوحة الجانبية (البروفايل)</summary>
}
