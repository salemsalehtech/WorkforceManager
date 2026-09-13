using System.Globalization;

namespace WorkforceManager.Data
{
    /// <summary>
    /// ثقافة البرنامج — ثابتة، مش بتتاخد من إعدادات ويندوز.
    ///
    /// كل تواريخ قاعدة البيانات ميلادية، وكل التقارير والقسايم والنسخ
    /// الاحتياطية بتتكتب على أساس كده. لو المستخدم (أو صورة ويندوز مثبتة
    /// على جهاز تاني) ظابط التقويم على هجري، نفس الكود بيبدأ يكتب سنة
    /// 1448 مكان 2026 — التواريخ على الشاشة تخالف اللي في الداتا، والأخطر
    /// إن اسم ملف النسخة الاحتياطية بيتقرا غلط فالنسخ بتتمسح وهي لسه جديدة.
    ///
    /// التثبيت هنا بيقفل الباب ده كله: عربي مصري بالتقويم الميلادي —
    /// أسماء الشهور والأيام بتفضل عربي، والأرقام بتفضل هي هي على أي جهاز
    /// وفي أي سنة.
    ///
    /// ثقافة ar-EG الافتراضية جايبة معاها فاصلة عشرية "٫" وفاصلة آلاف "٬"
    /// (مش النقطة والفاصلة المعروفين)، وعلامة السالب فيها حرف اتجاه مخفي
    /// (U+061C) قبل الشرطة — الأرقام نفسها بتفضل ٠-٩ عادية كسلسلة حروف، بس
    /// الفواصل دي بتبان غريبة/متكسرة في أي رقم فيه كسور (الصافي، أيام
    /// العمل) أو آلاف (الأجور)، وفي أي مكان بيتكتب فيه رقم بـ $"{...}" أو
    /// .ToString() من غير تنسيق واضح — يعني كل شاشة تقريبًا. الأسطر تحت
    /// بترجّع الفواصل للمعتاد من غير ما تلمس التقويم ولا أسماء الشهور.
    ///
    /// ده منفصل عن مشكلة تانية: DigitSubstitution الافتراضي لـ ar-EG هو
    /// Context — يعني شكل الرقم *وقت الرسم* (مش محتوى السلسلة) بيتغيّر
    /// حسب النص المحيط، وكل نص في التطبيق عربي RTL. فحتى مع فواصل سليمة،
    /// WPF كان برضه بيرسم "4" كـ"٤" بصريًا. None يجبر الشكل الأوروبي دايمًا
    /// بصرف النظر عن السياق.
    /// </summary>
    public static class AppCulture
    {
        /// <summary>الثقافة نفسها — للاستخدام في أي تنسيق يدوي صريح</summary>
        public static CultureInfo Build()
        {
            var culture = new CultureInfo("ar-EG");
            culture.DateTimeFormat.Calendar = new GregorianCalendar();

            culture.NumberFormat.NumberDecimalSeparator = ".";
            culture.NumberFormat.NumberGroupSeparator = ",";
            culture.NumberFormat.NegativeSign = "-";
            culture.NumberFormat.PercentDecimalSeparator = ".";
            culture.NumberFormat.PercentGroupSeparator = ",";
            culture.NumberFormat.CurrencyDecimalSeparator = ".";
            culture.NumberFormat.CurrencyGroupSeparator = ",";
            culture.NumberFormat.DigitSubstitution = DigitShapes.None;

            return culture;
        }

        /// <summary>بتثبّت الثقافة دي على الثريد الحالي وكل ثريد جديد</summary>
        public static void Pin()
        {
            var culture = Build();

            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
    }
}
