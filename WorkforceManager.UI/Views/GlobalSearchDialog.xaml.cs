using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WorkforceManager.UI.Views
{
    /// <summary>نتيجة بحث واحدة — عامل أو منتج، بالاسم بس (كل التفاصيل تتحمّل بعد التنقّل للشاشة نفسها)</summary>
    public class GlobalSearchItem
    {
        public string Name { get; init; } = "";
        public bool IsWorker { get; init; }
    }

    /// <summary>
    /// بحث سريع عن عامل أو منتج من أي شاشة، من غير ما تفتح شاشته الأول.
    ///
    /// القايمة بتتحمّل عند المنادي **قبل** الفتح (نفس سبب أي دايالوج خفيف
    /// هنا — تحميل async جوه Constructor نافذة خطر Deadlock)، وده كافي
    /// لحجم بيانات المصنع (عمال ومنتجات مش آلاف)، فالفلترة كلها محلية
    /// في الذاكرة زي بحث شاشة العمال/المنتجات نفسها.
    /// </summary>
    public partial class GlobalSearchDialog : Window
    {
        private readonly IReadOnlyList<GlobalSearchItem> _all;

        public GlobalSearchItem? Chosen { get; private set; }

        private GlobalSearchDialog(IReadOnlyList<GlobalSearchItem> items)
        {
            InitializeComponent();

            _all = items;
            ResultsList.ItemsSource = _all;

            Loaded += (_, _) => SearchBox.Focus();
        }

        /// <summary>بيعرض النافذة ويرجّع اللي المستخدم اختاره، أو null لو لغى</summary>
        public static GlobalSearchItem? Ask(Window? owner, IReadOnlyList<GlobalSearchItem> items)
        {
            var dialog = new GlobalSearchDialog(items);
            if (owner is not null) dialog.Owner = owner;

            return dialog.ShowDialog() == true ? dialog.Chosen : null;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SearchBox.Text.Trim();

            var filtered = query.Length == 0
                ? _all
                : _all.Where(i => i.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

            ResultsList.ItemsSource = filtered;
            EmptyText.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
            if (ResultsList.SelectedItem is not GlobalSearchItem item) return;

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
