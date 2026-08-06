using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// سجل شغل يوم واحد لعامل بالساعة: العامل خلص شغله الساعة كام في يوم
    /// معين، وكام يومية اتحسبتله. الشيفت بيبدأ ثابت 8 صباحًا، فوقت
    /// الانتهاء لوحده بيحدد اليوميات.
    ///
    /// اليوميات المحسوبة بتتخزن (Snapshot) وقت التسجيل — عشان لو قواعد
    /// الحساب اتغيرت بعدين، السجلات القديمة تفضل زي ما كانت وقت الشغل
    /// الفعلي (نفس مبدأ اليومية في DailyProduction).
    /// </summary>
    [Index(nameof(WorkerId), nameof(Date), IsUnique = true)] // سجل واحد لكل عامل في اليوم
    [Index(nameof(Date))] // لاستعلامات اليوم/الأسبوع
    public class HourlyWorkLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [ForeignKey(nameof(Worker))]
        public int WorkerId { get; set; }

        [Required]
        public DateTime Date { get; set; } = DateTime.Today;

        /// <summary>
        /// وقت انتهاء الشغل بنظام 24 ساعة (16 = 4 مساءً، 20 = 8 مساءً،
        /// 24 = 12 منتصف الليل). الشيفت بيبدأ 8 صباحًا ثابت.
        /// </summary>
        [Required]
        [Range(9, 24, ErrorMessage = "وقت الانتهاء لازم يكون بعد بداية الشيفت (8 صباحًا)")]
        public int EndHour24 { get; set; }

        /// <summary>عدد اليوميات المحسوبة وقت التسجيل (Snapshot ثابت)</summary>
        [Required]
        public decimal WorkdaysCredited { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // ------- العلاقات -------

        public virtual Worker Worker { get; set; } = null!;
    }
}
