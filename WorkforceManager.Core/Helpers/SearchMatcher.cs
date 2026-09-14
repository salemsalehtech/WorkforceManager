using System;

namespace WorkforceManager.Core.Helpers
{
    /// <summary>نوع المطابقة اللي رجّعت النتيجة دي — كل ما كانت أدق كل ما كان Score أعلى.</summary>
    public enum SearchMatchKind
    {
        /// <summary>النص كله (بعد التطبيع) يساوي الاستعلام بالظبط.</summary>
        Exact,

        /// <summary>كلمة داخل النص تبدأ بالاستعلام.</summary>
        Prefix,

        /// <summary>النص يحتوي الاستعلام في أي مكان (نفس <see cref="ArabicSearch.Contains"/>).</summary>
        Substring,

        /// <summary>مفيش تطابق دقيق، لكن كلمة داخل النص "على بعد خطأ إملائي واحد أو اتنين" من الاستعلام.</summary>
        Fuzzy
    }

    /// <summary>نتيجة مطابقة حقل نصي واحد. Score كل ما زاد كان التطابق أحسن.</summary>
    public readonly record struct SearchMatchResult(SearchMatchKind Kind, int Score);

    /// <summary>
    /// مطابقة نص متدرّجة (تطابق دقيق ← بادئة ← احتواء ← تسامح مع خطأ إملائي)
    /// فوق تطبيع <see cref="ArabicSearch"/> — عشان بحث "ذكي" بس محلي بالكامل،
    /// من غير أي استدعاء خارجي أو نموذج لغوي.
    ///
    /// **الطبقة الأخيرة (Fuzzy) هي الوحيدة المكلفة** (مسافة تحرير محدودة)،
    /// وبتتشغّل بس لو الطبقات الأرخص فشلت كلها — فمعظم عمليات البحث
    /// الحقيقية (احتواء عادي) سريعة زي البحث الحالي بالظبط، والتكلفة
    /// الإضافية بتتحمّل بس وقت الحاجة الفعلية ليها (خطأ إملائي حقيقي).
    ///
    /// عايشة في Core جنب <see cref="ArabicSearch"/> مباشرة (بدون أي اعتمادية
    /// DB/UI زيها بالظبط) عشان تفضل قاعدة مطابقة واحدة أي طبقة تانية تقدر
    /// تستخدمها، ولأنها منطق خالص ينفع يتغطى بتستات من غير قاعدة بيانات.
    /// </summary>
    public static class SearchMatcher
    {
        /// <summary>
        /// تحت الطول ده، الفَزّي بيرجّع نتايج عشوائية مالهاش معنى فعلي —
        /// كلمة من حرفين بتبقى "على بعد حرف واحد" من عشرات الكلمات المختلفة
        /// تمامًا، فبيبقى نويز مش بحث.
        /// </summary>
        public const int MinQueryLengthForFuzzy = 3;

        /// <summary>الحد الفاصل بين اعتبار الاستعلام "قصير" أو "طويل" لحساب أقصى مسافة تحرير مسموحة.</summary>
        public const int FuzzyShortQueryLength = 6;

        /// <summary>استعلام قصير (≤ <see cref="FuzzyShortQueryLength"/>): خطأ إملائي واحد بس مسموح.</summary>
        public const int MaxFuzzyDistanceShort = 1;

        /// <summary>
        /// استعلام أطول: خطأين مسموحين — نسبة الخطأ لطول الكلمة أهم من عدد
        /// الحروف المطلق (خطأ واحد في كلمة من 4 حروف أوضح من خطأين في كلمة من 12).
        /// </summary>
        public const int MaxFuzzyDistanceLong = 2;

        private const int ExactScore = 1000;
        private const int PrefixScore = 800;
        private const int SubstringScore = 600;

        /// <summary>أساس درجة الفَزّي — بينقص 50 نقطة عن كل خطوة مسافة تحرير، فخطأ واحد يفضل غالبًا فوق خطأين.</summary>
        private const int FuzzyBaseScore = 400;
        private const int FuzzyScorePerDistance = 50;

        /// <summary>
        /// بيطابق حقل نصي واحد مقابل استعلام المستخدم. بيرجّع null لو مفيش
        /// تطابق من أي نوع (استعلام فاضي، نص فاضي، أو بعيد جدًا حتى عن
        /// الفَزّي). الطبقات بترتيب التكلفة، وأول طبقة تنجح توقف الباقي.
        /// </summary>
        public static SearchMatchResult? Match(string? query, string? text)
        {
            var normalizedQuery = ArabicSearch.Normalize(query);
            var normalizedText = ArabicSearch.Normalize(text);

            if (normalizedQuery.Length == 0 || normalizedText.Length == 0) return null;

            if (normalizedText.Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                return new SearchMatchResult(SearchMatchKind.Exact, ExactScore);

            var words = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (var word in words)
            {
                if (word.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                    return new SearchMatchResult(SearchMatchKind.Prefix, PrefixScore);
            }

            if (normalizedText.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                return new SearchMatchResult(SearchMatchKind.Substring, SubstringScore);

            if (normalizedQuery.Length < MinQueryLengthForFuzzy) return null;

            var maxDistance = normalizedQuery.Length <= FuzzyShortQueryLength
                ? MaxFuzzyDistanceShort
                : MaxFuzzyDistanceLong;

            var bestDistance = int.MaxValue;
            foreach (var word in words)
            {
                var distance = BoundedEditDistance(normalizedQuery, word, maxDistance);
                if (distance.HasValue && distance.Value < bestDistance) bestDistance = distance.Value;
            }

            if (bestDistance == int.MaxValue) return null;

            return new SearchMatchResult(SearchMatchKind.Fuzzy, FuzzyBaseScore - bestDistance * FuzzyScorePerDistance);
        }

        /// <summary>
        /// مسافة تحرير Damerau-Levenshtein (بما فيها تبديل موضع حرفين متجاورين
        /// كخطوة واحدة، مش اتنين) بحد أقصى <paramref name="maxDistance"/>.
        /// بترجّع null فورًا لو المسافة أصلًا مستحيل تكون تحت الحد (فرق الطول)
        /// أو لو أي صف في الحساب عدّى الحد (early exit) — عشان الطبقة دي
        /// تفضل رخيصة كفاية إنها تتشغّل على آلاف الصفوف من غير ما تبطّئ الكتابة.
        /// </summary>
        public static int? BoundedEditDistance(string a, string b, int maxDistance)
        {
            if (Math.Abs(a.Length - b.Length) > maxDistance) return null;

            var cols = b.Length + 1;
            var twoBack = new int[cols];
            var oneBack = new int[cols];
            var current = new int[cols];

            for (var j = 0; j < cols; j++) oneBack[j] = j;

            for (var i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                var rowMin = current[0];

                for (var j = 1; j < cols; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    var value = Math.Min(Math.Min(current[j - 1] + 1, oneBack[j] + 1), oneBack[j - 1] + cost);

                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                        value = Math.Min(value, twoBack[j - 2] + 1);

                    current[j] = value;
                    if (value < rowMin) rowMin = value;
                }

                if (rowMin > maxDistance) return null;

                (twoBack, oneBack, current) = (oneBack, current, twoBack);
            }

            var result = oneBack[cols - 1];
            return result <= maxDistance ? result : null;
        }
    }
}
