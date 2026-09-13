using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// كلمة سر العمليات — **صف واحد لكل حساب دخول** (AppUser)، مش
    /// مشتركة للبرنامج كله.
    ///
    /// دي كلمة سر منفصلة تمامًا عن كلمة سر الدخول: الدخول بيقول "مين
    /// انت"، ودي بتقول "انت مصرّح لك تعمل الحاجات الخطيرة". لما حد يحذف
    /// أو يحفظ عملية حساسة، البرنامج بيطلب كلمة سر العمليات **بتاعة
    /// الحساب المسجّل دخول بيه هو نفسه** — مصدر "مين الداخل دلوقتي" هو
    /// CurrentUserContext.
    ///
    /// كانت صف واحد بس لكل البرنامج (نظام صلاحيات مش موجود وقتها)، وده
    /// بالظبط الامتداد اللي كان متوقّع (شوف الكومنت التاريخي اللي كان
    /// هنا): "لو اتضاف نظام صلاحيات بعدين، الجدول ده ياخد عمود UserId".
    ///
    /// التشفير: نفس طريقة كلمة سر الدخول (PBKDF2-SHA256 بملح عشوائي) —
    /// القاعدة متكتوبة مرة واحدة في PasswordHasher والاتنين بينادوها.
    /// </summary>
    public class OperationsCredential
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// حساب الدخول بتاع كلمة السر دي. null مؤقتًا بس لصفوف قديمة
        /// من قبل الميزة دي — الهجرة (SeedDefaultDepartmentManagerAsync)
        /// بتنسبها لأول حساب مدير قسم افتراضي. أي صف جديد من دلوقتي
        /// لازم يبقى ليه AppUserId من لحظة إنشائه.
        /// </summary>
        [ForeignKey(nameof(AppUser))]
        public int? AppUserId { get; set; }

        public virtual AppUser? AppUser { get; set; }

        /// <summary>ناتج التشفير (Base64) — مش الكلمة نفسها</summary>
        [Required]
        [MaxLength(200)]
        public string PasswordHash { get; set; } = string.Empty;

        /// <summary>ملح عشوائي (Base64)</summary>
        [Required]
        [MaxLength(200)]
        public string PasswordSalt { get; set; } = string.Empty;

        /// <summary>
        /// عدد المحاولات الغلط المتتالية. بيترجع صفر مع أول محاولة صح.
        /// </summary>
        public int FailedAttempts { get; set; }

        /// <summary>
        /// مقفولة لحد اللحظة دي بسبب محاولات غلط كتير (null = مفتوحة).
        ///
        /// القفل بالوقت مش نهائي عن قصد: البرنامج على جهاز محلي ومفيش
        /// حد يفكّ القفل غير اللي واقف عليه، فقفل دائم كان هيحبس المصنع
        /// برّه شغله.
        /// </summary>
        public DateTime? LockedUntil { get; set; }

        /// <summary>آخر مرة الكلمة اتغيّرت (للتدقيق)</summary>
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
