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
