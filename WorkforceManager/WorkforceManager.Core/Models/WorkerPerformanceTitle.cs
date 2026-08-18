using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using WorkforceManager.Core.Enums;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// لقب "أحسن عامل" رسمي مسجّل — مختلف تمامًا عن
    /// WorkerWeeklySummaryDto.IsBestWorkerOfWeek (اللي بيتحسب لحظيًا كل
    /// مرة الشاشة تتفتح ومبيتخزنش). الصف ده بيتسجّل مرة واحدة بس، لما
    /// الفترة (أسبوع/شهر) تقفل فعليًا (WorkerRecognitionService،
    /// بتتنادى تلقائيًا عند بدء التشغيل) — وبيفضل ثابت على بروفايل
    /// العامل لحد ما لقب جديد من نفس النوع يتسجّل.
    /// </summary>
    [Index(nameof(TitleType), nameof(PeriodStart))]
    public class WorkerPerformanceTitle
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [ForeignKey(nameof(Worker))]
        public int WorkerId { get; set; }

        [Required]
        public PerformanceTitleType TitleType { get; set; }

        /// <summary>أول يوم في الفترة (خميس الأسبوع، أو أول يوم في الشهر)</summary>
        [Required]
        public DateTime PeriodStart { get; set; }

        /// <summary>آخر يوم في الفترة</summary>
        [Required]
        public DateTime PeriodEnd { get; set; }

        public DateTime AwardedAt { get; set; } = DateTime.Now;

        // ------- العلاقات -------

        public virtual Worker Worker { get; set; } = null!;
    }
}
