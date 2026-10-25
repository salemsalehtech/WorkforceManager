using System.Windows;
using System.Windows.Controls;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// لوحة بتوزّع العناصر على أعمدة **بالتتابع الرأسي**: العناصر الأولى كلها تحت بعض في العمود الأول، ولما
    /// العمود يخلص بيكمّل الباقي في العمود اللي بعده (زي أعمدة الجرنال) — عكس <see cref="UniformGrid"/> اللي
    /// بيرصّ أفقيًا (1 و2 جنب بعض). مناسبة لمراحل المنتج: 1…6 تحت بعض وبعدها 7…11 في العمود التاني، فالترتيب
    /// بيتقرا من فوق لتحت مش بيتنطّط يمين وشمال.
    ///
    /// عدد العناصر في العمود = ⌈العدد ÷ الأعمدة⌉ (الأعمدة الأولى هي الأكتر). كل عمود بعرض متساوي، وارتفاع كل
    /// عنصر على حسب محتواه (مرحلة وصلت لها قايمة اقتراحات بتطوّل عمودها بس). في واجهة RTL العمود الأول بيظهر على
    /// اليمين لأن WPF بيعكس إحداثيات اللوحة من نفسه.
    ///
    /// <b>AutoFit</b>: بدل عدد أعمدة ثابت، اللوحة بتختار **أقل عدد أعمدة** (لحد MaxColumns) بيخلّي كل العناصر
    /// تبان في الارتفاع المتاح من غير تمرير — شاشة توزيع العمال على المراحل عايزة كل المراحل قدام المستخدم مرة
    /// واحدة. الارتفاع المتاح هو ارتفاع اللوحة نفسها لو محدد، وإلا <see cref="FitHeight"/> (بيتربط بارتفاع
    /// الـScrollViewer اللي حوالين اللوحة، لأن جوّاه الارتفاع بيوصل لانهائي).
    /// </summary>
    public class ColumnFlowPanel : Panel
    {
        public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
            nameof(Columns), typeof(int), typeof(ColumnFlowPanel),
            new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public static readonly DependencyProperty AutoFitProperty = DependencyProperty.Register(
            nameof(AutoFit), typeof(bool), typeof(ColumnFlowPanel),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
            nameof(MaxColumns), typeof(int), typeof(ColumnFlowPanel),
            new FrameworkPropertyMetadata(5, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public static readonly DependencyProperty FitHeightProperty = DependencyProperty.Register(
            nameof(FitHeight), typeof(double), typeof(ColumnFlowPanel),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public int Columns
        {
            get => (int)GetValue(ColumnsProperty);
            set => SetValue(ColumnsProperty, value);
        }

        /// <summary>يختار عدد الأعمدة لوحده عشان كل العناصر تبان في الارتفاع المتاح</summary>
        public bool AutoFit
        {
            get => (bool)GetValue(AutoFitProperty);
            set => SetValue(AutoFitProperty, value);
        }

        public int MaxColumns
        {
            get => (int)GetValue(MaxColumnsProperty);
            set => SetValue(MaxColumnsProperty, value);
        }

        /// <summary>الارتفاع اللي AutoFit بيحاول يحشر فيه العناصر لما ارتفاع اللوحة نفسه لانهائي (0 = مفيش)</summary>
        public double FitHeight
        {
            get => (double)GetValue(FitHeightProperty);
            set => SetValue(FitHeightProperty, value);
        }

        /// <summary>عدد الأعمدة اللي اتحسب في آخر Measure (هو اللي Arrange بيرتّب عليه)</summary>
        private int _columns = 1;

        private static int PerColumn(int count, int columns) => (int)Math.Ceiling(count / (double)Math.Max(1, columns));

        protected override Size MeasureOverride(Size availableSize)
        {
            _columns = AutoFit ? ChooseColumns(availableSize) : Math.Max(1, Columns);
            var heights = MeasureAt(availableSize.Width, _columns);

            var width = double.IsInfinity(availableSize.Width)
                ? InternalChildren.Cast<UIElement>().Select(c => c.DesiredSize.Width).DefaultIfEmpty(0).Max() * _columns
                : availableSize.Width;
            return new Size(width, heights.Max());
        }

        /// <summary>بيقيس كل العناصر بعرض العمود ويرجّع مجموع ارتفاع كل عمود</summary>
        private double[] MeasureAt(double totalWidth, int columns)
        {
            var columnWidth = double.IsInfinity(totalWidth) ? double.PositiveInfinity : totalWidth / columns;
            var perColumn = Math.Max(1, PerColumn(InternalChildren.Count, columns));

            var heights = new double[columns];
            for (var i = 0; i < InternalChildren.Count; i++)
            {
                var child = InternalChildren[i];
                child.Measure(new Size(columnWidth, double.PositiveInfinity));
                heights[Math.Min(i / perColumn, columns - 1)] += child.DesiredSize.Height;
            }

            return heights;
        }

        private int ChooseColumns(Size availableSize)
        {
            var limit = double.IsInfinity(availableSize.Height) ? FitHeight : availableSize.Height;
            var max = Math.Max(1, Math.Min(MaxColumns, Math.Max(1, InternalChildren.Count)));

            // مفيش ارتفاع نقيس عليه أو العرض لانهائي: ارجع للعدد الثابت
            if (limit <= 0 || double.IsInfinity(availableSize.Width)) return Math.Clamp(Columns, 1, max);

            var best = max;
            var bestHeight = double.MaxValue;
            for (var c = 1; c <= max; c++)
            {
                var tallest = MeasureAt(availableSize.Width, c).Max();
                if (tallest <= limit) return c; // أقل عدد أعمدة بيكفي

                if (tallest < bestHeight)
                {
                    bestHeight = tallest;
                    best = c;
                }
            }

            return best; // مفيش حاجة بتكفي: أقصر أطول عمود، والباقي بالتمرير
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var columns = Math.Max(1, _columns);
            var columnWidth = finalSize.Width / columns;
            var perColumn = Math.Max(1, PerColumn(InternalChildren.Count, columns));

            var y = new double[columns];
            for (var i = 0; i < InternalChildren.Count; i++)
            {
                var column = Math.Min(i / perColumn, columns - 1);
                var child = InternalChildren[i];
                child.Arrange(new Rect(column * columnWidth, y[column], columnWidth, child.DesiredSize.Height));
                y[column] += child.DesiredSize.Height;
            }

            return finalSize;
        }
    }
}
