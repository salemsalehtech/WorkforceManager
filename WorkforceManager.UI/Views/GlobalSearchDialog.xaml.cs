using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Helpers;
using WorkforceManager.Data;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// بحث سريع شامل — بيغطي كل الفئات العشرة الموثّقة في CLAUDE.md (عمال،
    /// منتجات، مراحل، رصيد أولي، خطط ذاكرة، سجل عمليات، قوالب تقارير،
    /// حسابات إدارية، إعدادات، الدليل)، بمطابقة عربية متسامحة مع الأخطاء
    /// الإملائية.
    ///
    /// **الديالوج نفسه مالوش أي منطق مطابقة** — بينادي <see cref="_search"/>
    /// (delegate من المنادي، MainWindow.GlobalSearch_Click) اللي بيجمّع
    /// نتايج GlobalSearchService (الفئات الثمانية المرتبطة بقاعدة
    /// البيانات) مع فئتي الإعدادات والدليل الثابتين — نفس مبدأ "منطق
    /// المطابقة في Business/helper، أبدًا جوّه code-behind الديالوج".
    ///
    /// **البحث مؤجّل ~250ms بعد آخر حرف** (`_debounce`)، عشان الكتابة
    /// السريعة ما تشغّلش خط أنابيب كامل — بما فيه مطابقة فَزّي محتملة على
    /// آلاف صفوف سجل العمليات — على كل حرف؛ الفئات الأصغر (عمال، منتجات...)
    /// كانت سريعة كفاية من غيره، لكن الفئة الأكبر حجمًا لأ.
    /// `_searchGeneration` بيرمي أي نتيجة بحث سابق توصل متأخرة بعد بحث
    /// أحدث منها (نفس فكرة `_previewGeneration` في محرك التقارير).
    /// </summary>
    public partial class GlobalSearchDialog : Window
    {
        private readonly Func<string, Task<IReadOnlyList<GlobalSearchResult>>> _search;
        private readonly DispatcherTimer _debounce;
        private int _searchGeneration;
        private readonly Func<GlobalSearchResult, Task<SearchIntentAnswer?>>? _preview;
        private int _previewGeneration;

        /// <summary>أوامر سريعة بتظهر قبل ما تكتب — دوسة بتكتب العبارة، والبحث بيعرض الأمر كنتيجة تختارها</summary>
        private static readonly string[] QuickActionHints =
        {
            "سجّل غياب",
            "سجّل جزاء",
            "أضف عامل",
            "سجّل إنتاج",
            "حفظ نهائي",
        };

        /// <summary>
        /// أمثلة ثابتة بتعرّف بالنيات لما صندوق البحث فاضي — من غير الأمثلة
        /// دي محدش هيعرف إن "غياب" أو "أعلى إنتاج الأسبوع ده" شغالين أصلًا.
        /// كلها استعلامات بلا اسم (مفيش "[اسم عامل]") عشان تفضل قابلة
        /// للدوسة مباشرة وتشتغل صح لأي مصنع، من غير الاعتماد على بيانات حقيقية.
        /// </summary>
        private static readonly string[] ExampleHints =
        {
            "غياب",
            "أعلى إنتاج الأسبوع ده",
            "أقل إنتاج الأسبوع ده",
            "مين أحسن عامل",
            "إنتاج امبارح",
            "حضور النهارده",
            "المتأخر في الخطة",
            "هالك الأسبوع ده",
            "إنتاج الشهر ده",
        };

        public GlobalSearchResult? Chosen { get; private set; }

        private GlobalSearchDialog(
            Func<string, Task<IReadOnlyList<GlobalSearchResult>>> search,
            Func<GlobalSearchResult, Task<SearchIntentAnswer?>>? preview,
            IReadOnlyList<SearchSuggestion>? alerts)
        {
            InitializeComponent();

            _search = search;
            _preview = preview;
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _debounce.Tick += async (_, _) =>
            {
                _debounce.Stop();
                await RunSearchAsync(SearchBox.Text.Trim());
            };

            HintChipsList.ItemsSource = ExampleHints;
            QuickActionsList.ItemsSource = QuickActionHints;

            if (alerts is { Count: > 0 })
            {
                AlertsList.ItemsSource = alerts;
                AlertsSection.Visibility = Visibility.Visible;
            }

            // "آخر بحث" بس لو فيه فعلًا اختيارات مسجّلة قبل كده — قايمة فاضية
            // (أول تشغيل للبرنامج مثلًا) معناها القسم ده مايتعرضش خالص
            var recent = SearchRankingStore.GetRecentQueries(4);
            if (recent.Count > 0)
            {
                RecentSearchesList.ItemsSource = recent;
                RecentSearchesSection.Visibility = Visibility.Visible;
            }

            EmptyStatePanel.Visibility = Visibility.Visible;

            Loaded += (_, _) => SearchBox.Focus();
        }

        /// <summary>بيعرض النافذة ويرجّع اللي المستخدم اختاره، أو null لو لغى</summary>
        public static GlobalSearchResult? Ask(
            Window? owner, Func<string, Task<IReadOnlyList<GlobalSearchResult>>> search,
            Func<GlobalSearchResult, Task<SearchIntentAnswer?>>? preview = null,
            IReadOnlyList<SearchSuggestion>? alerts = null)
        {
            var dialog = new GlobalSearchDialog(search, preview, alerts);
            if (owner is not null) dialog.Owner = owner;

            return dialog.ShowDialog() == true ? dialog.Chosen : null;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _debounce.Stop();
            _debounce.Start();
        }

        private async Task RunSearchAsync(string query)
        {
            var generation = ++_searchGeneration;

            if (query.Length == 0)
            {
                ResultsList.ItemsSource = null;
                EmptyText.Visibility = Visibility.Collapsed;
                EmptyStatePanel.Visibility = Visibility.Visible;
                return;
            }

            EmptyStatePanel.Visibility = Visibility.Collapsed;

            IReadOnlyList<GlobalSearchResult> results;
            try
            {
                results = await _search(query);
            }
            catch (Exception ex)
            {
                // فشل البحث نفسه (استثناء حقيقي، مش "مفيش نتائج") لازم يوصل
                // للمستخدم — سكوت هنا كان هيخلي الديالوج يقعد فاضي من غير تفسير
                Notify.Error("حصلت مشكلة أثناء البحث: " + ex.Message);
                return;
            }

            if (generation != _searchGeneration) return; // نتيجة بحث سابق وصلت متأخرة — اتجاوزها

            var view = CollectionViewSource.GetDefaultView(results);
            view.GroupDescriptions.Clear();
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(GlobalSearchResult.Category)));

            ResultsList.ItemsSource = view;
            EmptyText.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>دوسة على تشيب "آخر بحث"/"جرّب" — بتحط نصه في الصندوق وتشغّل نفس مسار البحث العادي</summary>
        private void HintChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Content: string text }) return;

            SearchBox.Text = text;
            SearchBox.CaretIndex = text.Length;
            SearchBox.Focus();
        }

        /// <summary>دوسة على تنبيه — بتكتب السؤال اللي بيجاوبه (Tag) وتشغّل البحث العادي</summary>
        private void AlertChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string query }) return;

            SearchBox.Text = query;
            SearchBox.CaretIndex = query.Length;
            SearchBox.Focus();
        }

        /// <summary>
        /// كارت المعاينة: كل ما نتيجة عامل/منتج تتحدد بنعرض ملخصها جنب القايمة (من غير ما تفتح الشاشة).
        /// نتيجة معاينة قديمة وصلت متأخرة بعد تحديد تاني بتترمي (_previewGeneration).
        /// </summary>
        private async void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var generation = ++_previewGeneration;

            if (_preview is null || ResultsList.SelectedItem is not GlobalSearchResult item)
            {
                ShowPreview(null);
                return;
            }

            SearchIntentAnswer? answer;
            try { answer = await _preview(item); }
            catch { answer = null; }

            if (generation != _previewGeneration) return;
            ShowPreview(answer);
        }

        private void ShowPreview(SearchIntentAnswer? answer)
        {
            PreviewTitle.Text = answer?.Title ?? "";
            PreviewLines.ItemsSource = answer?.Lines;
            PreviewCard.Visibility = answer is null ? Visibility.Collapsed : Visibility.Visible;
            PreviewPlaceholder.Visibility = answer is null ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // تحت أو Enter من صندوق البحث بيوديك لأول نتيجة، بدل ما تحتاج تدوس Tab
            if (e.Key is not (Key.Down or Key.Enter) || ResultsList.Items.Count == 0) return;

            ResultsList.SelectedIndex = 0;

            if (e.Key == Key.Enter) { Confirm(); return; }

            (ResultsList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
            e.Handled = true;
        }

        private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ResultsList.SelectedItem is not null) Confirm();
        }

        private void ResultsList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && ResultsList.SelectedItem is not null) Confirm();
        }

        private void Confirm()
        {
            if (ResultsList.SelectedItem is not GlobalSearchResult item) return;

            // "الترتيب بالاستخدام": نسجّل الاختيار ده قبل القفل، عشان استعلام
            // مشابه بعدين يرقّي نفس النتيجة (شوف SearchRankingScorer وMainWindow.
            // SearchAllCategoriesAsync). فشل التسجيل (ملف مقفول من عملية تانية
            // مثلًا) ميمنعش المستخدم من اختيار نتيجته — البحث أهم من التلميح.
            var key = GlobalSearchService.RankingKey(item);
            if (key is not null)
            {
                try
                {
                    SearchRankingStore.RecordPick(ArabicSearch.Normalize(SearchBox.Text), key, DateTime.Now);
                }
                catch { /* تلميح ترتيب، مش وظيفة أساسية — فشله ميوقفش الاختيار */ }
            }

            Chosen = item;
            DialogResult = true;
            Close();
        }

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}

namespace WorkforceManager.UI.Views
{
    /// <summary>تنبيه في البحث قبل الكتابة: النص اللي بيظهر، والسؤال اللي بيتكتب لما تدوس عليه</summary>
    public sealed record SearchSuggestion(string Text, string Query);
}
