using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using WorkforceManager.Core.Helpers;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// بحث عن عامل بالاسم — **العنصر الوحيد** اللي بيعمل ده في التطبيق،
    /// مستخدم في الجزاءات والسلف.
    ///
    /// كان ComboBox في الشاشتين: قايمة بـ 46 عامل والوصول لحد بالتمرير
    /// بطيء، والكتابة فيها بتدوّر على أول حرف بس. البحث هنا بيستخدم
    /// <see cref="ArabicSearch"/> فبيتجاهل الهمزات — نفس بحث شاشة
    /// العمال وشاشة التسجيل، عشان "احمد" و"أحمد" يلاقوا نفس النتيجة.
    ///
    /// الكيبورد: السهمين بيتنقلوا، Enter بيختار، Esc بيقفل.
    /// </summary>
    public partial class WorkerSearchBox : UserControl
    {
        /// <summary>أقصى عدد اقتراحات معروضة — أكتر من كده بيبقى تمرير تاني</summary>
        private const int MaxSuggestions = 8;

        private readonly ObservableCollection<WorkerSuggestion> _matches = new();
        private int _highlighted = -1;

        /// <summary>بيمنع تحديث النص وهو بيتكتب برمجيًا (اختيار عامل)</summary>
        private bool _suppressSearch;

        public WorkerSearchBox()
        {
            InitializeComponent();
            MatchesList.ItemsSource = _matches;
            RefreshPlaceholder();
        }

        // ------- مصدر العمال -------

        public static readonly DependencyProperty WorkersProperty =
            DependencyProperty.Register(nameof(Workers), typeof(IEnumerable), typeof(WorkerSearchBox),
                new PropertyMetadata(null));

        public IEnumerable? Workers
        {
            get => (IEnumerable?)GetValue(WorkersProperty);
            set => SetValue(WorkersProperty, value);
        }

        // ------- العامل المختار (ثنائي الاتجاه) -------

        public static readonly DependencyProperty SelectedWorkerProperty =
            DependencyProperty.Register(nameof(SelectedWorker), typeof(AttendanceRow), typeof(WorkerSearchBox),
                new FrameworkPropertyMetadata(null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedWorkerChanged));

        public AttendanceRow? SelectedWorker
        {
            get => (AttendanceRow?)GetValue(SelectedWorkerProperty);
            set => SetValue(SelectedWorkerProperty, value);
        }

        // ------- نص الخانة الفاضية -------

        public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(WorkerSearchBox),
                new PropertyMetadata("دوّر على العامل بالاسم…", OnPlaceholderChanged));

        public string Placeholder
        {
            get => (string)GetValue(PlaceholderProperty);
            set => SetValue(PlaceholderProperty, value);
        }

        private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
            ((WorkerSearchBox)d).RefreshPlaceholder();

        /// <summary>
        /// الاختيار اتغيّر من برّا (الشاشة صفّرته بعد الحفظ مثلاً) —
        /// الخانة بتتزامن معاه بدل ما تفضل شايلة اسم عامل مش مختار
        /// </summary>
        private static void OnSelectedWorkerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var box = (WorkerSearchBox)d;
            var worker = e.NewValue as AttendanceRow;

            box._suppressSearch = true;
            box.SearchBox.Text = worker?.FullName ?? string.Empty;
            box._suppressSearch = false;

            box.ClosePopup();
            box.ClearButton.Visibility = worker is null ? Visibility.Collapsed : Visibility.Visible;
            box.RefreshPlaceholder();
        }

        private void RefreshPlaceholder() =>
            PlaceholderText.Text = SearchBox.Text.Length == 0 ? Placeholder : string.Empty;

        // ------- البحث -------

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshPlaceholder();
            if (_suppressSearch) return;

            // الكتابة معناها إن الاختيار القديم مبقاش صالح
            if (SelectedWorker is not null) SetCurrentValue(SelectedWorkerProperty, null);

            ApplyFilter();
        }

        private void SearchBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            _matches.Clear();
            _highlighted = -1;

            var query = SearchBox.Text.Trim();
            var source = Workers?.OfType<AttendanceRow>().ToList() ?? new List<AttendanceRow>();

            var matches = (query.Length == 0
                    ? source
                    : source.Where(w => ArabicSearch.Contains(w.FullName, query)))
                .Take(MaxSuggestions)
                .ToList();

            foreach (var worker in matches) _matches.Add(new WorkerSuggestion(worker));

            SuggestionsPopup.IsOpen = _matches.Count > 0 && SearchBox.IsKeyboardFocusWithin;
        }

        // ------- الكيبورد -------

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                    MoveHighlight(1);
                    e.Handled = true;
                    break;

                case Key.Up:
                    MoveHighlight(-1);
                    e.Handled = true;
                    break;

                case Key.Enter when _highlighted >= 0 && _highlighted < _matches.Count:
                    Select(_matches[_highlighted].Row);
                    e.Handled = true;
                    break;

                case Key.Escape:
                    ClosePopup();
                    e.Handled = true;
                    break;
            }
        }

        private void MoveHighlight(int delta)
        {
            if (_matches.Count == 0) return;

            SuggestionsPopup.IsOpen = true;

            // أول ضغطة (ومفيش تحديد) بتاخد أول أو آخر عنصر حسب الاتجاه
            _highlighted = _highlighted < 0
                ? (delta > 0 ? 0 : _matches.Count - 1)
                : Math.Clamp(_highlighted + delta, 0, _matches.Count - 1);

            for (var i = 0; i < _matches.Count; i++)
                _matches[i].IsHighlighted = i == _highlighted;
        }

        // ------- الاختيار -------

        private void PickWorker_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is AttendanceRow worker) Select(worker);
        }

        private void Select(AttendanceRow worker)
        {
            SetCurrentValue(SelectedWorkerProperty, worker);
            ClosePopup();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            SetCurrentValue(SelectedWorkerProperty, null);

            _suppressSearch = true;
            SearchBox.Text = string.Empty;
            _suppressSearch = false;

            RefreshPlaceholder();
            SearchBox.Focus();
        }

        private void ClosePopup()
        {
            SuggestionsPopup.IsOpen = false;
            _highlighted = -1;
        }
    }

    /// <summary>سطر اقتراح — غلاف بيضيف حالة "متظلّل بالكيبورد" على الصف</summary>
    public partial class WorkerSuggestion : ObservableObject
    {
        public WorkerSuggestion(AttendanceRow row) => Row = row;

        public AttendanceRow Row { get; }

        [ObservableProperty]
        private bool _isHighlighted;
    }
}
