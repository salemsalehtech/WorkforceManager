using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using WorkforceManager.Core.Enums;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// سجل حضور يومي واحد لعامل: حاضر / غائب بإذن / غائب بدون إذن.
    /// النموذج ده مستقل عن DailyProduction لأن عامل ممكن يكون حاضر
    /// لكن من غير إنتاج مسجل (يوم تدريب مثلاً)، أو العكس مش وارد
    /// أصلاً (غايب يبقى معندوش إنتاج تلقائيًا).
    ///
    /// **مفيش وقت حضور وانصراف هنا.** كان فيه عمودين TimeSpan؟
    /// اتشالوا: مسار الحفظ الوحيد (AttendanceService) كان بيكتبهم
    /// null صراحة، ومفيش شاشة ولا تقرير ولا تصدير بيقراهم. الشغل
    /// بالساعة بيتسجّل في HourlyWorkLog اللي فيه ساعة انتهاء فعلية.
    /// ومعاهم اتشال Notes اللي عمره ما اتكتب أصلاً.
    /// </summary>
    [Index(nameof(WorkerId), nameof(Date), IsUnique = true)] // يوم واحد بالظبط لكل عامل، منع تكرار التسجيل
    public class Attendance
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [ForeignKey(nameof(Worker))]
        public int WorkerId { get; set; }

        [Required]
        public DateTime Date { get; set; } = DateTime.Today;

        [Required]
        public AttendanceStatus Status { get; set; } = AttendanceStatus.Present;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // ------- العلاقات -------

        public virtual Worker Worker { get; set; } = null!;

        [NotMapped]
        public bool IsAbsence => Status != AttendanceStatus.Present;
    }
}
