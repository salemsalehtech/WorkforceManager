using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// مستخدم للبرنامج (تسجيل الدخول). كلمة المرور مش بتتخزن أبدًا كنص
    /// صريح — بيتخزن الـ Hash بتاعها + الـ Salt (عشوائي لكل مستخدم)،
    /// والتحقق بيتم بإعادة الحساب والمقارنة (PBKDF2 في AuthService).
    /// </summary>
    public class AppUser
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "اسم المستخدم مطلوب")]
        [MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        /// <summary>ناتج تشفير كلمة المرور (Base64) — مش كلمة المرور نفسها</summary>
        [Required]
        [MaxLength(200)]
        public string PasswordHash { get; set; } = string.Empty;

        /// <summary>ملح عشوائي خاص بالمستخدم (Base64) — بيمنع جداول التخمين الجاهزة</summary>
        [Required]
        [MaxLength(200)]
        public string PasswordSalt { get; set; } = string.Empty;

        /// <summary>الاسم المعروض في الترحيب بعد الدخول (اختياري)</summary>
        [MaxLength(100)]
        public string? DisplayName { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// آخر مرة فتح فيها الحساب ده شاشة سجل العمليات — أساس شارة
        /// "عمليات جديدة" على زرار السجل (شوف ActivityLogService.GetUnseenCountAsync).
        /// null يعني عمره ما فتحها؛ ساعتها الحساب بيتحسب "شافه أول مرة
        /// النهارده" عند إنشائه (CreatedAt)، مش كل تاريخ السجل من قبل ما الحساب يتعمل.
        /// </summary>
        public DateTime? LastSeenActivityLogAt { get; set; }

        /// <summary>
        /// الحساب الإداري (مدير/رئيس قسم) اللي حساب الدخول ده بتاعه —
        /// null لحساب دخول عادي (لسه) مش مربوط بحساب إداري. كل حساب
        /// إداري بقى حساب دخول فعلي بيوزر وباسورد خاصين بيه، فيه علاقة
        /// واحد لواحد (شوف AppDbContext.OnModelCreating للفهرس الفريد).
        /// </summary>
        [ForeignKey(nameof(Worker))]
        public int? WorkerId { get; set; }

        public virtual Worker? Worker { get; set; }
    }
}
