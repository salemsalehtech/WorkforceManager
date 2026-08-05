using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// إقفال إنتاج يوم. المستخدم بيراجع أرقام اليوم ويوافق عليها، فاليوم
    /// بيتقفل ومبيبقاش ينفع يتسجل عليه إنتاج جديد.
    ///
    /// الصف ده بيسجّل إن **المستخدم شاف الأرقام ووافق عليها**، وبيدّي
    /// التقرير اليومي حالة نهائية: "اليوم ده اتقفل بالأرقام دي" مش "اليوم
    /// ده لسه ممكن يتغير".
    /// </summary>
    [Index(nameof(Date), IsUnique = true)] // إقفال واحد لكل يوم
    public class ProductionDayClosure
    {
        [Key]
        public int Id { get; set; }

        /// <summary>اليوم المقفول (بدون وقت)</summary>
        public DateTime Date { get; set; }

        /// <summary>لحظة الإقفال الفعلية (للتدقيق)</summary>
        public DateTime ClosedAt { get; set; }

        /// <summary>قطع خلصت آخر مرحلة في اليوم ده (لقطة وقت الإقفال)</summary>
        public int CompletedPieces { get; set; }

        /// <summary>
        /// قطع دخلت أول مرحلة في اليوم ده (لقطة وقت الإقفال).
        ///
        /// اللقطة دي مش بتتحسب من صفوف تانية وقت العرض عن قصد: الرقم ده
        /// اتعرض للمستخدم ووافق عليه، فلازم يفضل زي ما هو حتى لو حد صحّح
        /// سجل إنتاج قديم بعد كده.
        /// </summary>
        public int StartedPieces { get; set; }

        [MaxLength(300)]
        public string? Notes { get; set; }
    }
}
