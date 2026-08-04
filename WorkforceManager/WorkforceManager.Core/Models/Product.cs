using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// يمثل منتج داخل القسم (مثال: قميص رجالي، حقيبة، ... إلخ).
    /// كل منتج له مجموعة مراحل تصنيع خاصة به (ProductionStage)،
    /// وكل مرحلة داخل هذا المنتج لها سعر مستقل حتى لو تكرر اسم
    /// نفس المرحلة في منتج آخر بسعر مختلف.
    /// </summary>
    public class Product : SoftDeletableEntity
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "اسم المنتج مطلوب")]
        [MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        /// <summary>كود اختياري للمنتج لتسهيل الفرز والتقارير</summary>
        [MaxLength(30)]
        public string? ProductCode { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        /// <summary>
        /// صورة المنتج (اختيارية) مخزّنة جوه قاعدة البيانات نفسها.
        ///
        /// ليه جوه القاعدة مش ملف على الجنب؟ عشان النسخ الاحتياطي الموجود
        /// بينسخ ملف الـ db بس — فلو الصور كانت ملفات منفصلة كانت هتضيع مع
        /// أي استرجاع أو نقل للبرنامج لجهاز تاني.
        ///
        /// الصورة بتتصغّر وتتضغط قبل التخزين (شوف ProductImageHelper في
        /// طبقة الواجهة) فحجمها بيفضل عشرات الكيلوبايتات مش ميجات — مهم
        /// لأن حجم النسخة الاحتياطية بيكبر بيها.
        /// </summary>
        public byte[]? ImageData { get; set; }

        /// <summary>
        /// يسمح بإخفاء منتج توقف إنتاجه دون حذف بياناته التاريخية
        /// أو المراحل والأسعار المرتبطة به.
        /// </summary>
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // ------- العلاقات -------

        /// <summary>كل مراحل التصنيع الخاصة بهذا المنتج تحديدًا (بأسعارها المستقلة)</summary>
        public virtual ICollection<ProductionStage> Stages { get; set; } = new List<ProductionStage>();
    }
}
