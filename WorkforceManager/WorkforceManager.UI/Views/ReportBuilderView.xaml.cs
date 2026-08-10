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

            // **العرض على المحتوى مش على الشاشة.** كان نجوم (2* و1*)،
            // يعني الأعمدة بتتقسم عرض الشاشة مهما كان المحتوى أطول —
            // فاسم زي "عبدالله السيد عبدالجواد" بيتقص. دلوقتي كل عمود
            // بياخد عرضه من محتواه، والجدول بيتمرّر أفقيًا لو زاد.
            PreviewGrid.Columns.Add(new DataGridTextColumn
            {
                Header = headers[0],
                Binding = new Binding(nameof(PreviewRow.Label)),
                Width = DataGridLength.Auto,
                MinWidth = 150,
                HeaderStyle = (Style)FindResource("ModernGridHeader"),
                ElementStyle = TextCellStyle()
            });

            for (var i = 1; i < headers.Count; i++)
            {
                var isText = _viewModel.IsTextColumn(i - 1);

                PreviewGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = headers[i],
                    Binding = new Binding($"{nameof(PreviewRow.Cells)}[{i - 1}]"),
                    Width = DataGridLength.Auto,
                    MinWidth = isText ? 130 : 90,

                    // الرأس بيتحاذى زي الخلية: النص للبداية والرقم في
                    // النص. من غير كده الرأس بيبقى فوق عمود تاني بصريًا
                    HeaderStyle = (Style)FindResource(
                        isText ? "ModernGridHeader" : "ModernGridHeaderCentered"),
                    ElementStyle = isText ? TextCellStyle() : NumberCellStyle()
                });
            }
        }

        /// <summary>الأرقام في النص ومتراصّة — الأعمدة بتتقارن بالعين</summary>
        private static Style NumberCellStyle()
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            return style;
        }

        /// <summary>
        /// النص من بداية السطر، وبيتقص بنقط لو طال **مع تلميح بالكامل** —
        /// عمود ضيّق مبيخفيش الاسم خالص.
        /// </summary>
        private static Style TextCellStyle()
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Left));
            style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,
                new Binding(nameof(TextBlock.Text)) { RelativeSource = RelativeSource.Self }));
            return style;
        }
    }
}
