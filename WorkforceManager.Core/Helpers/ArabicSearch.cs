using System;
using System.Text;

namespace WorkforceManager.Core.Helpers
{
    /// <summary>
    /// تطبيع النص العربي قبل المطابقة في البحث. مدخل البيانات بيكتب بسرعة
    /// وبيهملش الهمزات، فـ "احمد" لازم تلاقي "أحمد" و"لفه" تلاقي "لفة" —
    /// من غير كده البحث بيرجع فاضي والمستخدم يفتكر إن العامل مش موجود.
    ///
    /// بتوحّد: ا/أ/إ/آ/ٱ، ه/ة، ي/ى/ئ، و/ؤ — وبتشيل التشكيل والتطويل، وبتجمّع
    /// أي تتابع مسافات (زيادة بين كلمتين، أو في البداية/النهاية) لمسافة واحدة.
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

                if (char.IsWhiteSpace(ch))
                {
                    // مسافة واحدة بس لكل تتابع، ومفيش مسافة قبل أول حرف حقيقي
                    if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
                    continue;
                }

                sb.Append(ch switch
                {
                    'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                    'ة' => 'ه',
                    'ى' or 'ئ' => 'ي',
                    'ؤ' => 'و',
                    _ => ch
                });
            }

            // مسافة آخر النص (لو أصله انتهى بمسافات) مش لازمة
            if (sb.Length > 0 && sb[^1] == ' ') sb.Length--;

            return sb.ToString();
        }

        /// <summary>هل <paramref name="text"/> يحتوي <paramref name="query"/> بعد تطبيع الاتنين؟</summary>
        public static bool Contains(string? text, string? query) =>
            Normalize(text).Contains(Normalize(query), StringComparison.OrdinalIgnoreCase);
    }
}
