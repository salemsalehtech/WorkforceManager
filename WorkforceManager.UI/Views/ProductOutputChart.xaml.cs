using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// رسم إنتاج المنتجات — شوف الكومنت في أول ProductOutputChart.xaml.
    /// كل الخصايص DependencyProperty عشان الشاشة المستضيفة تربطها
    /// بالـViewModel بتاعها مباشرة (التقييم: ChartBuckets، الرئيسية: WeekChartBuckets).
    /// </summary>
    public partial class ProductOutputChart : UserControl
    {
        public static readonly DependencyProperty BucketsProperty = DependencyProperty.Register(
            nameof(Buckets), typeof(IEnumerable), typeof(ProductOutputChart));

        public static readonly DependencyProperty HasDataProperty = DependencyProperty.Register(
            nameof(HasData), typeof(bool), typeof(ProductOutputChart), new PropertyMetadata(true));

        public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(
            nameof(EmptyText), typeof(string), typeof(ProductOutputChart), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty FillWidthProperty = DependencyProperty.Register(
            nameof(FillWidth), typeof(bool), typeof(ProductOutputChart), new PropertyMetadata(false));

        /// <summary>
        /// ارتفاع منطقة الرسم — لازم يفضل أكبر بفرق بسيط (~12) من maxBarHeight
        /// اللي اتبعت لـProductOutputChartBuilder.Build، وإلا أطول عمود بيتقص من فوق
        /// </summary>
        public static readonly DependencyProperty PlotAreaHeightProperty = DependencyProperty.Register(
            nameof(PlotAreaHeight), typeof(double), typeof(ProductOutputChart), new PropertyMetadata(272.0));

        public IEnumerable? Buckets
        {
            get => (IEnumerable?)GetValue(BucketsProperty);
            set => SetValue(BucketsProperty, value);
        }

        public bool HasData
        {
            get => (bool)GetValue(HasDataProperty);
            set => SetValue(HasDataProperty, value);
        }

        public string EmptyText
        {
            get => (string)GetValue(EmptyTextProperty);
            set => SetValue(EmptyTextProperty, value);
        }

        public bool FillWidth
        {
            get => (bool)GetValue(FillWidthProperty);
            set => SetValue(FillWidthProperty, value);
        }

        public double PlotAreaHeight
        {
            get => (double)GetValue(PlotAreaHeightProperty);
            set => SetValue(PlotAreaHeightProperty, value);
        }

        public ProductOutputChart()
        {
            InitializeComponent();
        }
    }
}
