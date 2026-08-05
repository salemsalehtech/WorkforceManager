using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// يمثل عامل في القسم.
    /// كل عامل مرتبط بمجموعة من المهارات (WorkerSkills) توضح المراحل
    /// التي يستطيع تنفيذها، ومرتبط بسجلات الإنتاج اليومي الخاصة به.
    /// </summary>
    public class Worker : SoftDeletableEntity
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "اسم العامل مطلوب")]
        [MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        /// <summary>
        /// كود العامل — **مش معروض في أي شاشة**، اتشال من الواجهة كلها
        /// لأنه مكانش بيضيف حاجة للمستخدم (الاسم كافي للبحث والتمييز).
        ///
        /// العمود نفسه فضل موجود عن قصد لأنه المفتاح اللي بتترابط بيه
        /// مهارات العمال وقت التهيئة الأولى
        /// (<c>DatabaseSeeder.SeedWorkerSkillLinksAsync</c> بتطابق
        /// <c>WorkerSkillsSeed</c> بالكود مش بالاسم). لو اتشال، أي تركيب
        /// جديد هيطلع من غير أي مهارات مربوطة، ومحدش هيبقى مؤهل لأي مرحلة.
        /// </summary>
        [MaxLength(30)]
        public string? EmployeeCode { get; set; }

        [MaxLength(20)]
        public string? PhoneNumber { get; set; }

        /// <summary>
        /// صورة العامل (اختيارية) مخزّنة جوه قاعدة البيانات نفسها — نفس
        /// أسلوب <see cref="Product.ImageData"/> بالظبط وللسبب نفسه:
        /// النسخ الاحتياطي بينسخ ملف الـ db بس، فالصور كملفات على الجنب
        /// كانت هتضيع مع أي استرجاع أو نقل لجهاز تاني.
        ///
        /// بتتصغّر وتتضغط قبل التخزين (ProfileImageHelper في طبقة الواجهة)
        /// فحجمها عشرات الكيلوبايتات مش ميجات.
        /// </summary>
        public byte[]? PhotoData { get; set; }

        /// <summary>
        /// وصف حر لمهارات العامل (منسوخ من الملاحظات الأصلية عند
        /// الاستيراد).
        ///
        /// **اتشال من واجهة الإضافة/التعديل** واتبدّل بكروت التقييم
        /// المحسوبة من WorkerSkill (نظام النجوم). العمود نفسه فاضل
        /// مخزّن عن قصد لأن حاجتين لسه بيقروه:
        ///   • <c>DatabaseSeeder.SeedHourlyRolesAsync</c> بيحدد منه عمال
        ///     الرص/الجودة/التدريب في أي تركيب جديد
        ///   • البحث في شاشة العمال بيدوّر جواه
        /// يعني مفيش حد بيكتب فيه من دلوقتي، بس اللي متكتب قبل كده شغّال.
        /// </summary>
        [MaxLength(1000)]
        public string? SkillsNotes { get; set; }

        /// <summary>تاريخ التحاق العامل بالقسم</summary>
        public DateTime? HireDate { get; set; }

        /// <summary>
        /// دور العامل بالساعة (رص/جودة/تدريب/أخرى) — لو مُحدد فالعامل
        /// بيتحاسب بعدد الساعات مش بالإنتاج، وبيظهر في تبويب "العمال
        /// بالساعة" بدل رحلة الإنتاج. null = عامل إنتاج عادي (بالقطعة).
        /// </summary>
        public Enums.HourlyRole? HourlyRole { get; set; }

        /// <summary>هل هذا العامل بيتحاسب بالساعة؟ (له دور بالساعة)</summary>
        [NotMapped]
        public bool IsHourly => HourlyRole is not null;

        /// <summary>
        /// سعر اليومية بالجنيه المصري لهذا العامل. الأجر = صافي اليوميات
        /// × السعر ده. السعر الحالي بيُطبّق على كل الفترات (مش Snapshot)،
        /// فتغييره بيعيد حساب كل الأجور بالسعر الجديد. الافتراضي 0 (لحد
        /// ما مدير القسم يحدد سعر العامل).
        /// </summary>
        public decimal DailyWageEgp { get; set; }

        /// <summary>
        /// يسمح بإلغاء تفعيل عامل (ترك العمل) دون حذف بياناته وسجل إنتاجه القديم.
        /// أفضل من الحذف الفعلي (Soft Delete) للحفاظ على سلامة السجلات التاريخية.
        /// </summary>
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // ------- العلاقات (Navigation Properties) -------

        /// <summary>كل المهارات (المراحل) التي يجيد هذا العامل تنفيذها</summary>
        public virtual ICollection<WorkerSkill> Skills { get; set; } = new List<WorkerSkill>();

        /// <summary>كل سجلات الإنتاج اليومي المسجّلة لهذا العامل عبر الزمن</summary>
        public virtual ICollection<DailyProduction> ProductionRecords { get; set; } = new List<DailyProduction>();

        /// <summary>كل سجلات الحضور والغياب الخاصة بهذا العامل عبر الزمن</summary>
        public virtual ICollection<Attendance> AttendanceRecords { get; set; } = new List<Attendance>();

        /// <summary>كل الجزاءات المسجّلة على هذا العامل عبر الزمن (بتتخصم من يومياته الأسبوعية)</summary>
        public virtual ICollection<Penalty> Penalties { get; set; } = new List<Penalty>();

        /// <summary>سجلات الشغل بالساعة (للعمال بالساعة فقط)</summary>
        public virtual ICollection<HourlyWorkLog> HourlyWorkLogs { get; set; } = new List<HourlyWorkLog>();

        /// <summary>تعديلات الأجر بالجنيه (سلف وحوافز) — تدخل في صافي أجر الفترة</summary>
        public virtual ICollection<WageAdjustment> WageAdjustments { get; set; } = new List<WageAdjustment>();

    }
}
