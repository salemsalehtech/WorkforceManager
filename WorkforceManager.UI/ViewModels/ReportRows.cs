namespace WorkforceManager.UI.ViewModels
{
    // صفوف شاشة "التقييم والمتابعة": إنتاج اليوم ورسم إنتاج المنتجات.
    //
    // صفوف تبويب "تقييم اليوم" (DailyProductRow / DailyReportRow) اتشالت
    // مع التبويب نفسه — الجدول اللي كانت بتعرضه موجود في مُنشئ التقارير
    // (الإنتاج بالعامل) وبيتصدّر Excel كمان.

    /// <summary>عمود واحد في رسم إنتاج المنتجات (يوم أو أسبوع أو شهر)</summary>
    public class ChartBucket
    {
        public string Label { get; init; } = "";

        /// <summary>إجمالي الفترة — بيتكتب فوق العمود كرقم واحد</summary>
        public string TotalText { get; init; } = "";

        /// <summary>شرايح العمود المكدّس، بترتيب المفتاح</summary>
        public List<ChartBar> Segments { get; init; } = new();

        public bool HasWork { get; init; }

        /// <summary>الرقم الخام — المقارنة والمتوسط بيتحسبوا منه</summary>
        public int Total { get; init; }

        /// <summary>الفترة الجارية — لسه مكملتش، فالمقارنة بيها ناقصة</summary>
        public bool IsCurrent { get; init; }

        public string CurrentNote => IsCurrent ? "لسه شغال" : "";

        /// <summary>
        /// ارتفاع خط المتوسط فوق خط الأرض. كل عمود بيرسم قطعته من الخط
        /// بدل طبقة واحدة فوق الرسم كله — الطبقة كانت هتحتاج ربط على
        /// Margin، وده نوع قيمة بيكسّر القالب وقت التشغيل.
        /// </summary>
        public double AverageOffset { get; init; }

        public bool ShowAverage { get; init; }
    }

    /// <summary>شريحة في العمود المكدّس: منتج في فترة (اللون بيميز المنتج)</summary>
    public class ChartBar
    {
        public string Color { get; init; } = "Series1Brush";
        public double Height { get; init; }
        public string Tooltip { get; init; } = "";
    }

    /// <summary>عنصر في مفتاح ألوان الرسم: المنتج ولونه وإجماليه وتغيّره</summary>
    public class ChartLegendItem
    {
        public string Color { get; init; } = "Series1Brush";
        public string ProductName { get; init; } = "";
        public string TotalText { get; init; } = "";

        /// <summary>التغيّر عن الفترة اللي قبلها — فاضي لو مفيش مقارنة</summary>
        public string ChangeText { get; init; } = "";

        /// <summary>مفتاح فرشاة — مش كود لون (شوف <see cref="WorkforceManager.UI.ThemeBrush"/>)</summary>
        public string ChangeColor { get; init; } = "InkSoftBrush";

        public bool HasChange => ChangeText.Length > 0;
    }

    /// <summary>منتج في فلتر الرسم — علامة بتظهره أو تخفيه</summary>
    public partial class ChartProductFilterItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";

        [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
        private bool _isChecked = true;
    }
}
