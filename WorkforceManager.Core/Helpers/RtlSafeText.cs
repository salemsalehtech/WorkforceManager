namespace WorkforceManager.Core.Helpers
{
    /// <summary>
    /// نجوم التقييم "★★★☆☆" ونسب زي "3 / 5" بيتألفوا من رموز/أرقام بس —
    /// من غير أي حرف عربي قوي الاتجاه جواهم. النص ده "ضعيف" بمقاييس
    /// Unicode Bidi، فلما يتعرض جوه شاشة عربية (RTL) بيتقلب بصريًا:
    /// "★★★★☆" بيبان "☆★★★★"، و"11 / 12" بيبان "12 / 11" — نفس
    /// الترتيب المكتوب، بس معكوس على الشاشة.
    ///
    /// الحل: عزل الجزء ده باتجاه إجباري LTR (Unicode Isolate)، فبيتعرض
    /// بترتيبه المكتوب مهما كان اتجاه الشاشة حواليه. العلامتين دول بعرض
    /// صفر — مفيش حرف زيادة ظاهر ولا بيأثر على نسخ/تصدير النص.
    ///
    /// كانت النجوم بالذات مكتوبة في خمس أماكن مختلفة بنفس التركيب
    /// (new string('★', ..) + new string('☆', ..))، فتعديل واحد هنا
    /// بيغطيهم كلهم.
    /// </summary>
    public static class RtlSafeText
    {
        private const char LeftToRightIsolate = '⁦';
        private const char PopDirectionalIsolate = '⁩';

        /// <summary>نجوم التقييم: "filled" مليانة و"total - filled" فاضية</summary>
        public static string Stars(int filled, int total = 5) =>
            Isolate(new string('★', filled) + new string('☆', total - filled));

        /// <summary>نسبة "X / Y" — تغطية، عدّاد، أو أي كسر بيتعرض للمستخدم</summary>
        public static string Ratio(int numerator, int denominator) =>
            Isolate($"{numerator} / {denominator}");

        /// <summary>يعزل أي نص LTR (أرقام/رموز) عشان مايتقلبش جوه سياق عربي</summary>
        public static string Isolate(string text) =>
            LeftToRightIsolate + text + PopDirectionalIsolate;
    }
}
