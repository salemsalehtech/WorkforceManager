namespace WorkforceManager.Core.Helpers
{
    /// <summary>
    /// أول جزئين من اسم العامل — للعرض المختصر على كارت شاشة العمال
    /// (الاسم الكامل بيتشاف كـToolTip أو على الوش التاني من الكارت).
    ///
    /// "جزء" هنا مش بالضرورة كلمة واحدة: "عبد"/"أبو"/"ابو" بتتلحق
    /// بالكلمة اللي بعدها كجزء واحد ("عبد الله محمد" مش "عبد" لوحدها —
    /// نص مالوش معنى لو اتقطع في النص).
    /// </summary>
    public static class ShortName
    {
        /// <summary>كلمات لازم تتلحق بالكلمة اللي بعدها عشان تبقى جزء اسم واحد له معنى</summary>
        private static readonly string[] CompoundPrefixes = { "عبد", "أبو", "ابو" };

        public static string From(string? fullName)
        {
            var tokens = (fullName ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length == 0) return string.Empty;

            var parts = new List<string>(2);
            var i = 0;
            while (i < tokens.Length && parts.Count < 2)
            {
                // بادئة مركّبة ومعاها كلمة بعدها فعلاً؟ يتلحقوا كجزء واحد.
                // لو البادئة آخر كلمة في الاسم (مفيش بعدها حاجة تتلحق بيها)،
                // بتاخد جزء لوحدها زي أي كلمة عادية.
                if (Array.IndexOf(CompoundPrefixes, tokens[i]) >= 0 && i + 1 < tokens.Length)
                {
                    parts.Add($"{tokens[i]} {tokens[i + 1]}");
                    i += 2;
                }
                else
                {
                    parts.Add(tokens[i]);
                    i += 1;
                }
            }

            return string.Join(" ", parts);
        }
    }
}
