using WorkforceManager.Core.Enums;

namespace WorkforceManager.Business.DTOs
{
    /// <summary>الحالة اللونية لتقدّم منتج مقابل خطته — نفس منطق tint/ink الموجود، بدون ألوان خام جديدة</summary>
    public enum PlanPaceStatus { Behind, OnTrack, Ahead }

    /// <summary>
    /// تتبّع منتج واحد لشهر معيّن حتى تاريخ asOfDate — كل رقم هنا مبني فوق
    /// خدمات موجودة (DailyProductionReportService للمحقق، MonthlyPlanCorrection
    /// للتصليحات اليدوية، MonthlyPlan للخطة)، مفيش حساب مزدوج.
    /// </summary>
    public record MonthlyPlanTrackingDto(
        int ProductId,
        string ProductName,
        int? FamilyId,
        string? FamilyName,
        bool IsComplete,
        decimal? PieceWeightGrams,
        Material? Material,

        int PlannedQuantity,

        /// <summary>"الخطة اليومية" — هدف يومي يدوي (MonthlyPlan.DailyTargetQuantity)، مختلف عن RequiredDailyOutput المحسوب</summary>
        int? DailyTargetQuantity,

        int AchievedToDate,       // من DailyProductionReportService — من أول الشهر لحد asOfDate
        int CorrectionsToDate,    // مجموع تصليحات المستخدم اليدوية لنفس المدى
        int TodayCompleted,       // إنتاج النهارده بس (رقم واحد، مش عمودين زي الشيت القديم)

        /// <summary>محقق فعلي = AchievedToDate + CorrectionsToDate — الرقم اللي كل الحسابات التانية بتتبني عليه</summary>
        int EffectiveAchieved,

        /// <summary>الخطة بالتناسب مع أيام الشغل المنقضية — أساس نسبة المحقق</summary>
        decimal ProRatedPlan,

        /// <summary>EffectiveAchieved ÷ ProRatedPlan — null لو ProRatedPlan صفر (خطة صفر أو أول يوم في شهر بلا خطة)</summary>
        decimal? AchievedPercent,

        PlanPaceStatus Status,

        /// <summary>الكمية المطلوبة يوميًا في كل الأيام الباقية عشان توصل الخطة كاملة — null لو مفيش أيام باقية</summary>
        int? RequiredDailyOutput,

        /// <summary>توقّع نهاية الشهر بالإيقاع الحالي — null لو لسه مفيش أيام شغل منقضية</summary>
        int? ForecastEndOfMonth,

        /// <summary>محقق نفس التاريخ من الشهر اللي فات — للمقارنة (البند 7)</summary>
        int? SameDayPreviousMonth,

        /// <summary>عنده إنتاج في الشهر ده بس مفيش خطة مسجّلة — "إنتاج خارج الخطة"، مش 0% مضلّلة</summary>
        bool IsOutsidePlan)
    {
        /// <summary>وزن المحقق الفعلي = وزن القطعة × EffectiveAchieved — null لو المنتج ماله وزن مسجّل</summary>
        public decimal? TotalWeightGrams => PieceWeightGrams is { } w ? w * EffectiveAchieved : null;
    }
}
