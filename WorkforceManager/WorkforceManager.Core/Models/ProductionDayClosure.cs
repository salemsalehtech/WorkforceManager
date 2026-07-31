using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// إقفال إنتاج يوم. المستخدم بيراجع الدفعات الواقفة ويوافق على ترحيلها
    /// لبكرة، فاليوم بيتقفل ومبيبقاش ينفع يتسجل عليه إنتاج جديد.
    ///
    /// الترحيل نفسه مش محتاج الصف ده — الدفعة المفتوحة بتفضل مفتوحة لوحدها.
    /// الصف ده بيسجّل إن **المستخدم شاف الواقف ووافق عليه**، وبيدّي التقرير
    /// اليومي حالة نهائية: "اليوم ده اتقفل بالأرقام دي" مش "اليوم ده لسه
    /// ممكن يتغير".
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

        /// <summary>عدد الدفعات اللي كانت واقفة وقت الإقفال (لقطة للتقرير)</summary>
        public int CarriedBatchCount { get; set; }

        /// <summary>إجمالي القطع المرحّلة وقت الإقفال (لقطة للتقرير)</summary>
        public int CarriedPieces { get; set; }

        [MaxLength(300)]
        public string? Notes { get; set; }
    }
}
