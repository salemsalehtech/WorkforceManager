using System;
using System.ComponentModel.DataAnnotations;
using WorkforceManager.Core.Interfaces;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// أساس أي كيان بيتشال بحذف ناعم — بيحمل حقول التدقيق الخمسة.
    ///
    /// أساس مشترك مش نسخة على كل كيان: الحقول دي بتتكرر على العامل
    /// والمنتج والمرحلة وسجل الإنتاج، ولو اتكتبت أربع مرات هييجي يوم
    /// واحد منهم يتغيّر (طول الحقل، اسمه، حقل جديد) ويفضل الباقي ورا
    /// من غير ما حد ياخد باله.
    ///
    /// الكلاس مجرّد ومفيهوش مفتاح: كل كيان بيفضل ماسك الـ Id بتاعه
    /// وتعريفه زي ما هو، وEF بيتعامل مع الخصائص دي كأنها متكتوبة عنده.
    /// </summary>
    public abstract class SoftDeletableEntity : ISoftDeletable
    {
        /// <summary>اتشال؟ الفلتر العام في AppDbContext بيعتمد على ده</summary>
        public bool IsDeleted { get; set; }

        /// <summary>لحظة الحذف (null لو لسه موجود)</summary>
        public DateTime? DeletedAt { get; set; }

        /// <summary>اسم المستخدم اللي حذف</summary>
        [MaxLength(100)]
        public string? DeletedBy { get; set; }

        /// <summary>سبب الحذف اللي كتبه المستخدم</summary>
        [MaxLength(500)]
        public string? DeletionReason { get; set; }

        /// <summary>
        /// اسم الكيان وقت الحذف — لقطة عشان السجلات التاريخية تفضل
        /// مقروءة حتى لو حد سمّى كيان جديد بنفس الاسم بعد كده.
        /// </summary>
        [MaxLength(200)]
        public string? DeletedName { get; set; }
    }
}
