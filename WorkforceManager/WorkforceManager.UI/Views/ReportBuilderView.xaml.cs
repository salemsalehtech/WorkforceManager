using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// شاشة التقارير. الكود هنا بيعمل حاجة واحدة بس: يبني أعمدة الجدول.
    ///
    /// الأعمدة مش ثابتة — بتتغيّر مع موضوع التقرير (الإنتاج له أعمدة
    /// والأجور ليها أعمدة تانية)، وXAML مش بيعرف يعمل أعمدة من قايمة.
    /// فالشاشة بتسمع لـ PreviewHeaders وتعيد بناء الأعمدة معاها.
    ///
    /// المكسب: **جدول واحد بيعرض الستة مواضيع كلها**. لو الأعمدة
    /// كانت مكتوبة في XAML كان لازم جدول لكل موضوع.
    /// </summary>
    public partial class ReportBuilderView : UserControl
    {
        private readonly ReportBuilderViewModel _viewModel;

        public ReportBuilderView(ReportBuilderViewModel viewModel)
        {
            InitializeComponent();

            _viewModel = viewModel;
            DataContext = viewModel;

            viewModel.PreviewHeaders.CollectionChanged += OnHeadersChanged;
            Unloaded += (_, _) => viewModel.PreviewHeaders.CollectionChanged -= OnHeadersChanged;

            Loaded += async (_, _) => await viewModel.InitializeAsync();
        }

        /// <summary>مستني إعادة بناء متجدولة — بيمنع تجدولة تانية معاها</summary>
        private bool _rebuildPending;

        /// <summary>
        /// القايمة بتتفضى وتتملي مع كل تقرير، فبنستنى لحد ما تخلص
        /// (آخر إضافة) بدل ما نعيد البناء مع كل عمود.
        ///
        /// العلم مهم: من غيره كل Add بيجدول إعادة بناء لوحده، يعني
        /// تقرير بخمس أعمدة بيمسح الأعمدة ويبنيها **خمس مرات** ورا بعض
        /// — الجدول بيرفّ قدام المستخدم وهو بيتفرج. واحدة بتكفي، لأن
        /// اللي بيتجدول بيقرا القايمة كاملة وقت ما يشتغل.
        /// </summary>
        private void OnHeadersChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) return;
            if (_rebuildPending) return;

            _rebuildPending = true;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _rebuildPending = false;
                RebuildColumns();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void RebuildColumns()
        {
            var headers = _viewModel.PreviewHeaders;
            if (headers.Count == 0) return;

            PreviewGrid.Columns.Clear();

            // أول عمود: اسم العامل أو المنتج أو اليوم — نص ومحاذاته للبداية
            PreviewGrid.Columns.Add(new DataGridTextColumn
            {
                Header = headers[0],
                Binding = new Binding(nameof(PreviewRow.Label)),
                Width = new DataGridLength(2, DataGridLengthUnitType.Star)
            });

            // باقي الأعمدة أرقام جاهزة كنصوص — التنسيق اتعمل في PreviewRow
            for (var i = 1; i < headers.Count; i++)
                PreviewGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = headers[i],
                    Binding = new Binding($"{nameof(PreviewRow.Cells)}[{i - 1}]"),
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                    ElementStyle = NumberCellStyle()
                });
        }

        /// <summary>الأرقام في النص ومتراصّة — الأعمدة بتتقارن بالعين</summary>
        private static Style NumberCellStyle()
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center));
            return style;
        }
    }
}
