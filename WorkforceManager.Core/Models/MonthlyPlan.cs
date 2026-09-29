using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// الكمية المخططة لمنتج واحد، لشهر واحد — القاعدة "مرة واحدة لكل
    /// منتج لكل شهر" (شوف الفهرس الفريد في AppDbContext على
    /// Product+Year+Month).
    ///
    /// **مفيش رقم "خطة عيلة" مخزّن في أي مكان** — خطة العيلة دايمًا مجموع
    /// خطط منتجاتها، محسوبة وقت العرض (MonthlyPlanService)، عشان تحل
    /// التضارب اللي كان في الشيت اليدوي القديم (مجموع الأجزاء مايطابقش
    /// رقم العيلة أحيانًا). لا تضيف عمود خطة على ProductFamily.
    /// </summary>
    public class MonthlyPlan
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey(nameof(Product))]
        public int ProductId { get; set; }

        public int Year { get; set; }

        /// <summary>1-12 — شوف HasCheckConstraint في AppDbContext</summary>
        public int Month { get; set; }

        /// <summary>
        /// 0 = الشهر كامل (الافتراضي، السلوك القديم زي ما هو). غير كده =
        /// Id حقيقي في MonthlyPlanSubPeriod — **من غير FK constraint** لأن
        /// 0 مش صف حقيقي في الجدول، التحقق بيحصل في الـservice مش الـDB.
        /// جزء من الفهرس الفريد بدل (ProductId,Year,Month) لوحدهم، عشان
        /// أكتر من فترة فرعية في نفس الشهر تقدر تحمل هدف مستقل لنفس المنتج.
        /// </summary>
        public int SubPeriodId { get; set; } = 0;

        public int PlannedQuantity { get; set; }

        /// <summary>
        /// "الخطة اليومية" — هدف يومي **يدوي** يكتبه المستخدم لهذا المنتج
        /// (زي عمود "انتاج اليوم" في شيت المصنع القديم)، اختياري. مختلف
        /// تمامًا عن MonthlyPlanTrackingDto.RequiredDailyOutput المحسوب
        /// ديناميكيًا (بيقل كل ما ينتج أكتر أو الأيام تنقص) — الرقم ده
        /// ثابت طول الشهر لحد ما المستخدم يغيّره بنفسه، مرجع/هدف مش حساب.
        /// </summary>
        public int? DailyTargetQuantity { get; set; }

        public virtual Product Product { get; set; } = null!;
    }
}
