namespace WorkforceManager.Business.DTOs
{
    /// <summary>نوع العمود — بيحدد التنسيق في Excel والمحاذاة في الشاشة</summary>
    public enum ReportValueKind
    {
        Text = 0,
        Whole = 1,
        Fraction = 2,
        Money = 3
    }

    /// <summary>عمود واحد في التقرير</summary>
    public class ReportColumn
    {
        /// <summary>
        /// معرّف ثابت للعمود — **مش الاسم المعروض**.
        ///
        /// لازم يكون منفصل عن <see cref="Header"/> لأن المستخدم بيقدر
        /// يغيّر اسم أي عمود، والقالب المحفوظ بيشاور على الأعمدة بعد
        /// شهور. لو التخطيط اتحفظ بالاسم المعروض، أول ما المستخدم
        /// يسمّي عمود باسمه بيبوظ كل قالب قديم بيستخدمه.
        /// </summary>
        public required string Key { get; init; }

        public required string Header { get; init; }
        public ReportValueKind Kind { get; init; } = ReportValueKind.Text;

        /// <summary>
        /// عمود بيتجمّع في سطر الإجماليات.
        ///
        /// مش كل رقم ينفع يتجمع: مجموع "سعر اليومية" لكل العمال رقم
        /// مالوش أي معنى، ومجموع "متوسط النجوم" أسوأ. الأعمدة دي
        /// بتتساب فاضية في سطر الإجمالي بدل ما تعرض رقم غلط.
        /// </summary>
        public bool Sums { get; init; }
    }

    /// <summary>سطر واحد — القيم بترتيب الأعمدة</summary>
    public class ReportRow
    {
        public required string Label { get; init; }
        public List<decimal?> Values { get; init; } = new();

        /// <summary>قيم نصية لما العمود مش رقمي (زي النجوم)</summary>
        public List<string?> Texts { get; init; } = new();
    }

    /// <summary>
    /// اختيار المستخدم لعمود واحد: يظهر ولا لأ، وباسم إيه.
    ///
    /// الترتيب في القايمة **هو** ترتيب العمود في التقرير — مفيش رقم
    /// ترتيب منفصل يتنسى يتحدّث ويختلف عن الواقع.
    /// </summary>
    public class ReportColumnChoice
    {
        public required string Key { get; init; }
        public bool Visible { get; init; } = true;

        /// <summary>اسم من عند المستخدم — null يعني سيب الاسم الأصلي</summary>
        public string? Header { get; init; }

        /// <summary>
        /// العمود ده يتجمّع في سطر الإجمالي؟
        ///
        /// null = سيب قرار البرنامج (<see cref="ReportColumn.Sums"/>).
        /// المستخدم بيقدر يلغي جمع عمود البرنامج شايفه بيتجمع، أو
        /// يجمّع عمود البرنامج مقفّله — لأن معنى "الإجمالي" بيختلف حسب
        /// التقرير: مجموع أيام الحضور لكل العمال رقم ليه معنى، ومجموع
        /// متوسط النجوم مالوش.
        /// </summary>
        public bool? Sums { get; init; }
    }

    /// <summary>
    /// نتيجة أي تقرير في البرنامج — جدول عام مش نوع لكل تقرير.
    ///
    /// ده أهم قرار في التصميم كله: طالما كل التقارير بتطلع بالشكل ده،
    /// يبقى فيه **مُصدِّر Excel واحد وشبكة عرض واحدة** يخدموا الستة
    /// مواضيع وكل تجميعاتهم. البديل كان ٦ مُصدِّرات و٦ شبكات — نفس
    /// التكرار اللي البرنامج اتنضّف منه.
    ///
    /// والمكسب التاني: أي تحسين في التصدير (تنسيق، ألوان، صفحة عنوان)
    /// بيوصل لكل التقارير مرة واحدة.
    /// </summary>
    public class ReportTable
    {
        public required string Title { get; init; }

        /// <summary>وصف المدة كنص — بيتكتب فوق الجدول وفي الملف</summary>
        public string PeriodText { get; init; } = "";

        /// <summary>اسم أول عمود (اللي فيه اسم العامل أو المنتج أو اليوم)</summary>
        public string LabelHeader { get; init; } = "";

        public List<ReportColumn> Columns { get; init; } = new();
        public List<ReportRow> Rows { get; init; } = new();

        /// <summary>
        /// سطر الإجماليات. بيتبني هنا مش في كل تقرير على حدة، وبيجمع
        /// الأعمدة اللي عليها <see cref="ReportColumn.Sums"/> بس.
        /// </summary>
        public ReportRow? Totals { get; private set; }

        public bool IsEmpty => Rows.Count == 0;

        /// <summary>مفتاح عمود الاسم (أول عمود) — للترتيب بالاسم</summary>
        public const string LabelColumnKey = "label";

        /// <summary>
        /// بيرتّب الصفوف ويقصّها على أعلى/أقل N.
        ///
        /// بيتنفّذ **قبل** <see cref="ApplyLayout"/> عن قصد: كده المستخدم
        /// يقدر يرتّب بعمود هو مخفيه ("رتّب بالأجر بس متعرضهوش")،
        /// و**قبل** <see cref="WithTotals"/> عشان الإجمالي يطلع على الصفوف
        /// المعروضة فعلاً — الإجمالي تحت 10 صفوف لازم يساوي جمعهم، مش
        /// جمع الـ50 اللي اتقصّوا.
        /// </summary>
        public ReportTable ApplySort(string? sortKey, bool descending, int? topN)
        {
            if (!string.IsNullOrWhiteSpace(sortKey))
            {
                var index = Columns.FindIndex(c => c.Key == sortKey);

                // مفتاح مش موجود (قالب قديم بعد ما الأعمدة اتغيّرت) =
                // سيب الترتيب الطبيعي بدل ما ترمي
                if (sortKey == LabelColumnKey)
                    Sort(r => r.Label, descending);
                else if (index >= 0 && Columns[index].Kind == ReportValueKind.Text)
                    Sort(r => Text(r, index) ?? "", descending);
                else if (index >= 0)
                    Sort(r => Value(r, index) ?? 0m, descending);
            }

            if (topN is { } take && take > 0 && Rows.Count > take)
                Rows.RemoveRange(take, Rows.Count - take);

            return this;
        }

        private void Sort<TKey>(Func<ReportRow, TKey> selector, bool descending)
        {
            var ordered = descending
                ? Rows.OrderByDescending(selector).ToList()
                : Rows.OrderBy(selector).ToList();

            Rows.Clear();
            Rows.AddRange(ordered);
        }

        /// <summary>
        /// بيطبّق اختيار المستخدم للأعمدة: يخفي، يرتّب، ويعيد التسمية.
        ///
        /// **القيم بتتحرك مع أعمدتها.** الصف بيخزن قيمه بترتيب الأعمدة
        /// (مش بالاسم)، فأي إعادة ترتيب للأعمدة من غير نفس الترتيب
        /// للقيم بتخلي الأرقام تتحط تحت أعمدة غلط — وده أسوأ من إن
        /// الميزة متبقاش موجودة أصلاً.
        ///
        /// أعمدة في التخطيط ومش في التقرير بتتساب (قالب اتحفظ لموضوع
        /// وأعمدته اتغيّرت)، وأعمدة في التقرير ومش في التخطيط بتتزوّد
        /// في الآخر عشان ميضيعش عمود جديد من غير ما حد ياخد باله.
        /// </summary>
        public ReportTable ApplyLayout(IReadOnlyList<ReportColumnChoice>? layout)
        {
            if (layout is not { Count: > 0 }) return this;

            var order = new List<int>();

            foreach (var choice in layout)
            {
                if (!choice.Visible) continue;

                var index = Columns.FindIndex(c => c.Key == choice.Key);
                if (index >= 0 && !order.Contains(index)) order.Add(index);
            }

            // عمود موجود في التقرير ومحدش قال عنه حاجة = يفضل ظاهر
            var mentioned = layout.Select(c => c.Key).ToHashSet();
            for (var i = 0; i < Columns.Count; i++)
                if (!mentioned.Contains(Columns[i].Key) && !order.Contains(i))
                    order.Add(i);

            if (order.Count == 0) return this; // المستخدم خفى كل حاجة — مش هنطلّع جدول فاضي

            var headerByKey = layout
                .Where(c => !string.IsNullOrWhiteSpace(c.Header))
                .GroupBy(c => c.Key)
                .ToDictionary(g => g.Key, g => g.Last().Header!);

            var sumsByKey = layout
                .Where(c => c.Sums is not null)
                .GroupBy(c => c.Key)
                .ToDictionary(g => g.Key, g => g.Last().Sums!.Value);

            var columns = order.Select(i =>
            {
                var source = Columns[i];
                return new ReportColumn
                {
                    Key = source.Key,
                    Header = headerByKey.TryGetValue(source.Key, out var custom) ? custom : source.Header,
                    Kind = source.Kind,
                    Sums = sumsByKey.TryGetValue(source.Key, out var sums) ? sums : source.Sums
                };
            }).ToList();

            foreach (var row in Rows) Reorder(row, order);
            if (Totals is not null) Reorder(Totals, order);

            Columns.Clear();
            Columns.AddRange(columns);

            return this;
        }

        private static void Reorder(ReportRow row, IReadOnlyList<int> order)
        {
            var values = order.Select(i => Value(row, i)).ToList();
            var texts = order.Select(i => Text(row, i)).ToList();

            row.Values.Clear();
            row.Values.AddRange(values);
            row.Texts.Clear();
            row.Texts.AddRange(texts);
        }

        private static decimal? Value(ReportRow row, int index) =>
            index >= 0 && index < row.Values.Count ? row.Values[index] : null;

        private static string? Text(ReportRow row, int index) =>
            index >= 0 && index < row.Texts.Count ? row.Texts[index] : null;

        /// <summary>بيبني سطر الإجماليات من الصفوف الموجودة</summary>
        public ReportTable WithTotals(string label = "الإجمالي")
        {
            if (Rows.Count == 0) return this;

            var totals = new ReportRow { Label = label };

            for (var i = 0; i < Columns.Count; i++)
            {
                if (!Columns[i].Sums) { totals.Values.Add(null); continue; }

                decimal sum = 0;
                foreach (var row in Rows)
                    if (i < row.Values.Count && row.Values[i] is { } v) sum += v;

                totals.Values.Add(sum);
            }

            Totals = totals;
            return this;
        }
    }
}
