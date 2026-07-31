using System;
using System.Text;

namespace WorkforceManager.Core.Helpers
{
    /// <summary>
    /// تطبيع النص العربي قبل المطابقة في البحث. مدخل البيانات بيكتب بسرعة
    /// وبيهملش الهمزات، فـ "احمد" لازم تلاقي "أحمد" و"لفه" تلاقي "لفة" —
    /// من غير كده البحث بيرجع فاضي والمستخدم يفتكر إن العامل مش موجود.
    ///
    /// بتوحّد: ا/أ/إ/آ/ٱ، ه/ة، ي/ى/ئ، و/ؤ — وبتشيل التشكيل والتطويل.
    /// المقارنة نفسها OrdinalIgnoreCase عشان الأسماء اللاتينية لو وُجدت.
    ///
    /// عايشة في Core (مش في الواجهة) عشان تفضل قاعدة واحدة لو أي طبقة تانية
    /// احتاجت تدوّر بالاسم، ولأنها منطق خالص ينفع يتغطى بتستات.
    /// </summary>
    public static class ArabicSearch
    {
        /// <summary>أول وآخر حرف في نطاق علامات التشكيل (فتحة/ضمة/كسرة/شدة/سكون/تنوين)</summary>
        private const char FirstDiacritic = 'ً';
        private const char LastDiacritic = 'ْ';

        /// <summary>الكشيدة — بتتكتب للتمديد البصري ومالهاش أي معنى في المطابقة</summary>
        private const char Tatweel = 'ـ';

        public static string Normalize(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var sb = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                if (ch >= FirstDiacritic && ch <= LastDiacritic) continue;
                if (ch == Tatweel) continue;

                sb.Append(ch switch
                {
                    'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                    'ة' => 'ه',
                    'ى' or 'ئ' => 'ي',
                    'ؤ' => 'و',
                    _ => ch
                });
            }

            return sb.ToString();
        }

        /// <summary>هل <paramref name="text"/> يحتوي <paramref name="query"/> بعد تطبيع الاتنين؟</summary>
        public static bool Contains(string? text, string? query) =>
            Normalize(text).Contains(Normalize(query), StringComparison.OrdinalIgnoreCase);
    }
}
