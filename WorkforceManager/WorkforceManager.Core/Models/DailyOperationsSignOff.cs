using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// توقيع نهاية اليوم — علم واحد **عالمي** لكل تاريخ تقويمي (مش لكل
    /// مستخدم لوحده)، بيقول "المستخدم راجع كل حاجة حصلت في البرنامج
    /// اليوم ده ووافق عليها بباسورد عملياته".
    ///
    /// ده بديل طلب باسورد العمليات في كل حفظ/تعديل/حذف على حدة (Tier B) —
    /// شوف SensitiveAction.DailySignOff و DailyOperationsSignOffService.
    /// الأفعال الخطيرة فعلاً (حذف عامل، تعديل الأجر، إعدادات النظام،
    /// الحركات المالية) لسه بتاخد باسورد فوري زي ما هي (Tier A)، ومش
    /// بتتأثر بالصف ده خالص.
    ///
    /// **عالمي مش لكل مستخدم**: أي حساب دخول (AppUser) بباسورد عملياته
    /// هو يقدر يوقّع، والتوقيع بيقفل اليوم للتطبيق كله — قرار اتأكد مع
    /// المستخدم عشان الباسورد أصلاً مرتبط بكل حساب لوحده
    /// (OperationsPasswordService)، فربط التوقيع بحساب واحد كان هيعقّد
    /// منطق منع الإغلاق ولحاق بدء التشغيل من غير فايدة حقيقية.
    ///
    /// **الفرق بين توقيع "في نفس اليوم" و"لاحق" (بعد ما التطبيق قفل
    /// فجأة قبل ما يتوقّع) مش محتاج علم منفصل** — بيتحدد بمقارنة
    /// SignedOffAt.Date بـ Date نفسها: لو مختلفين يبقى توقيع لاحق. نفس
    /// فلسفة المشروع: القيمة المشتقة أولى من التخزين لما تبقى رخيصة الحساب.
    /// </summary>
    [Index(nameof(Date), IsUnique = true)] // توقيع واحد لكل يوم
    public class DailyOperationsSignOff
    {
        [Key]
        public int Id { get; set; }

        /// <summary>اليوم الموقّع (بدون وقت)</summary>
        public DateTime Date { get; set; }

        /// <summary>لحظة التوقيع الفعلية (للتدقيق، وللتفرقة بين توقيع في وقته وتوقيع لاحق)</summary>
        public DateTime SignedOffAt { get; set; }
    }
}
