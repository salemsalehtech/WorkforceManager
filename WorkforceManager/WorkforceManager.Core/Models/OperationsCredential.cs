using System;
using System.ComponentModel.DataAnnotations;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// كلمة سر العمليات — **صف واحد بس** في الجدول ده.
    ///
    /// دي كلمة سر منفصلة تمامًا عن كلمة سر الدخول: الدخول بيقول "مين
    /// انت"، ودي بتقول "انت مصرّح لك تعمل الحاجات الخطيرة". اللي بيسجّل
    /// الإنتاج اليومي ممكن يبقى معاه كلمة الدخول من غير دي.
    ///
    /// مشتركة لكل المستخدمين مش لكل مستخدم: البرنامج بيشتغل على جهاز
    /// واحد في المصنع بحساب واحد، فكلمة سر لكل مستخدم كانت هتبقى نفس
    /// الكلمة متكررة. لو اتضاف نظام صلاحيات بعدين، الجدول ده ياخد عمود
    /// UserId ويبقى صف لكل مستخدم من غير ما أي كود تاني يتغيّر.
    ///
    /// التشفير: نفس طريقة كلمة سر الدخول (PBKDF2-SHA256 بملح عشوائي) —
    /// القاعدة متكتوبة مرة واحدة في PasswordHasher والاتنين بينادوها.
    /// </summary>
    public class OperationsCredential
    {
        [Key]
        public int Id { get; set; }

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
