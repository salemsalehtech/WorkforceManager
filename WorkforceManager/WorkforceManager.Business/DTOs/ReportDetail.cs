namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// خلية في صف التفاصيل — نص أو رقم.
    ///
    /// الملخص كل أعمدته أرقام فبيكفيه <c>decimal?</c>، لكن التفاصيل
    /// الخام بتخلط الاتنين في نفس الصف (اسم العامل جنب عدد القطع)،
    /// فمحتاجة نوع بيشيلهم مع بعض من غير ما نلف كل حاجة على نص —
    /// الرقم لازم يفضل رقم في Excel عشان الجمع والـ Pivot يشتغلوا.
    /// </summary>
    public readonly record struct ReportCell(string? Text, decimal? Number)
    {
        public static ReportCell Of(string? text) => new(text, null);
        public static ReportCell Of(decimal? number) => new(null, number);
        public static ReportCell Of(DateTime date) => new(date.ToString("yyyy/MM/dd"), null);
    }

    public class ReportDetailRow
    {
        public List<ReportCell> Cells { get; init; } = new();

        /// <summary>
        /// المجموعة اللي الصف ده تابع لها في الملخص (اسم المنتج، العامل،
        /// اليوم…). بيستخدمها التصدير لما المستخدم يطلب شيت لكل مجموعة.
        /// </summary>
        public string? GroupLabel { get; init; }
    }

    /// <summary>
    /// السجلات الخام ورا التقرير — سطر لكل سجل، من غير أي تجميع.
    ///
    /// ده اللي بيخلي المستخدم يعمل Pivot بنفسه في Excel بدل ما يستنى
    /// عمود جديد يتكتب في البرنامج. الملخص بيجاوب على السؤال المتوقع،
    /// والتفاصيل بتخليه يسأل اللي مكانش متوقع.
    /// </summary>
    public class ReportDetail
    {
        public required string SheetName { get; init; }
        public List<ReportColumn> Columns { get; init; } = new();
        public List<ReportDetailRow> Rows { get; init; } = new();

        public bool IsEmpty => Rows.Count == 0;
    }

    /// <summary>اللي المستخدم عايزه في ملف Excel</summary>
    public class ReportExportOptions
    {
        /// <summary>شيت تاني فيه كل سجل على حدة</summary>
        public bool IncludeDetailSheet { get; init; }

        /// <summary>شيت لكل مجموعة (كل منتج/عامل لوحده) بسجلاته الخام</summary>
        public bool SheetPerGroup { get; init; }

        /// <summary>اسم المصنع فوق التقرير</summary>
        public string? FactoryName { get; init; }

        /// <summary>مسار صورة الشعار (بيتساب لو مش موجود)</summary>
        public string? LogoPath { get; init; }
    }
}
