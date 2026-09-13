using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// سبب من أسباب الهالك (عيب خامة، غلط تشغيل، عطل مكنة…).
    ///
    /// قايمة يقدر المستخدم يعدّلها من الإعدادات: كل مصنع وأسبابه، ومن
    /// غير كده هيلاقي نفسه بيكتب نفس السبب بالإيد كل مرة في خانة
    /// الملاحظات — وساعتها "الهالك راح فين؟" تبقى سؤال مالوش إجابة
    /// لأن الملاحظات مش بتتجمّع.
    ///
    /// السبب **مبيتحذفش لما يبقى مستخدم** — بيتوقف بس (IsActive)، عشان
    /// تقارير الشهور اللي فاتت تفضل تعرف السبب اللي اتسجّل وقتها.
    /// </summary>
    public class ScrapReason
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "اسم السبب مطلوب")]
        [MaxLength(80)]
        public string Name { get; set; } = string.Empty;

        /// <summary>الترتيب في القايمة المنسدلة — الأكتر استخدامًا فوق</summary>
        public int SortOrder { get; set; }

        /// <summary>الموقوف مبيظهرش في التسجيل الجديد، بس تقاريره القديمة بتفضل</summary>
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public virtual ICollection<ProductionScrap> ScrapRecords { get; set; } = new List<ProductionScrap>();
    }
}
