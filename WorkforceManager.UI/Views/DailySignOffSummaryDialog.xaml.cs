using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Models;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>نتيجة ديالوج التوقيع — المستدعي بيتصرّف على حسبها</summary>
    public enum DailySignOffOutcome
    {
        /// <summary>رجع للبرنامج من غير توقيع</summary>
        Cancelled,

        /// <summary>اتوقّع فعلًا (الديالوج نفسه نادى trySignAsync ونجح)</summary>
        Signed,

        /// <summary>اختار "اقفل وأوقّع بكره" — ديالوج اللحاق هيطلب اليوم ده أول تشغيل جاي</summary>
        Deferred,

        /// <summary>عايز يروح يكمّل الناقص في شاشة تسجيل الإنتاج/الحضور</summary>
        OpenDailyEntry,

        /// <summary>عايز يروح يكمّل الناقص في شاشة الذاكرة</summary>
        OpenMemory
    }

    /// <summary>
    /// توقيع نهاية اليوم في خطوة واحدة: ملخص اليوم (كروت + نواقص + عمليات) وكلمة
    /// السر في آخره. **التوقيع نفسه بيحصل من جوه الديالوج** (trySignAsync) عشان
    /// كلمة سر غلط تظهر كرسالة تحت الخانة والديالوج يفضل مفتوح، بدل ما يتقفل
    /// والمستخدم يبدأ من الأول. التوقيع الفعلي في DailyOperationsSignOffService.
    /// </summary>
    public partial class DailySignOffSummaryDialog : Window
    {
        /// <summary>كام اسم يبان قبل "و N كمان" — أكتر من كده الشاشة بترجع كتلة نص</summary>
        private const int ChipPreviewCount = 12;

        private readonly Func<string, Task<string?>> _trySignAsync;
        private readonly Func<Task<string?>>? _markPresentAsync;
        private readonly DailySignOffChecklist _checklist;
        private readonly IReadOnlyList<string> _missingWorkers;
        private bool _workersExpanded;
        private bool _productsExpanded;
        private bool _attendanceResolved;

        public DailySignOffOutcome Outcome { get; private set; } = DailySignOffOutcome.Cancelled;

        /// <param name="trySignAsync">بياخد كلمة السر ويرجّع رسالة خطأ، أو null لو التوقيع نجح</param>
        /// <param name="markPresentAsync">بيسجّل الباقيين حاضرين ويرجّع رسالة خطأ أو null</param>
        /// <param name="closingReason">لو جاي من قفل البرنامج/الخروج: الجملة اللي بتشرح ليه الديالوج ظهر</param>
        /// <param name="deferLabel">نص زرار التأجيل — null يعني الزرار مش متاح (جاي من زرار التوقيع نفسه)</param>
        public DailySignOffSummaryDialog(
            DateTime date, IReadOnlyList<ActivityEvent> events, DailySignOffChecklist checklist,
            bool passwordRequired, Func<string, Task<string?>> trySignAsync, Func<Task<string?>>? markPresentAsync,
            string? closingReason = null, string? confirmLabel = null, string? deferLabel = null)
        {
            InitializeComponent();
            _trySignAsync = trySignAsync;
            _markPresentAsync = markPresentAsync;
            _checklist = checklist;
            _missingWorkers = checklist.WorkersWithNoAttendance;

            // الطول على قد الشاشة — الجسم بس هو اللي بيعمل scroll، الأزرار دايمًا ظاهرة
            MaxHeight = SystemParameters.WorkArea.Height * 0.92;

            DateText.Text = date.ToString("dddd d MMMM yyyy", new System.Globalization.CultureInfo("ar-EG"));
            if (closingReason is not null)
            {
                ReasonText.Text = closingReason;
                ReasonText.Visibility = Visibility.Visible;
            }
            if (confirmLabel is not null) ConfirmText.Text = confirmLabel;
            if (deferLabel is not null)
            {
                DeferText.Text = deferLabel;
                DeferButton.Visibility = Visibility.Visible;
            }

            PasswordSection.Visibility = passwordRequired ? Visibility.Visible : Visibility.Collapsed;
            NoPasswordText.Visibility = passwordRequired ? Visibility.Collapsed : Visibility.Visible;

            // ── الكروت ──
            var producedProducts = Math.Max(0, checklist.ActiveProductCount - checklist.ProductsWithNoEntries.Count);
            EventsCount.Text = events.Count.ToString();
            ProductsCount.Text = $"{producedProducts} من {checklist.ActiveProductCount}";
            MemoryCount.Text = checklist.MemoryDueWithNoEntries.Count.ToString();
            Tint(EventsCard, "GoldTintBrush");
            Tint(ProductsCard, checklist.ProductsWithNoEntries.Count == 0 ? "SuccessBgBrush" : "WarnBgBrush");
            Tint(MemoryCard, checklist.MemoryDueWithNoEntries.Count == 0 ? "SuccessBgBrush" : "DangerTintBrush");
            RefreshAttendance();

            // ── الذاكرة ──
            if (checklist.MemoryDueWithNoEntries.Count > 0)
            {
                MemorySection.Visibility = Visibility.Visible;
                MemoryTitle.Text = $"{checklist.MemoryDueWithNoEntries.Count} منتج مخطط له في الذاكرة النهارده ومدخلش له إنتاج";
                MemoryChips.ItemsSource = checklist.MemoryDueWithNoEntries;
            }

            // ── المنتجات ──
            if (checklist.ProductsWithNoEntries.Count > 0)
            {
                ProductsSection.Visibility = Visibility.Visible;
                ProductsTitle.Text = $"{checklist.ProductsWithNoEntries.Count} منتج من غير إنتاج النهارده";
                ProductsToggleText.Text = "اعرض الأسماء ▾";
            }

            // ── العمليات ──
            var rows = events.OrderByDescending(e => e.OccurredAt).Select(e => new ActivityEventRow(e)).ToList();
            EventsList.ItemsSource = rows;
            EventsTitle.Text = rows.Count > 0 ? $"{rows.Count} عملية اتسجلت النهارده" : "مفيش عمليات اتسجلت النهارده";
            EventsToggle.Content = "اعرضهم";
            EventsToggle.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            UpdateConfirmState();
            Loaded += (_, _) => { if (passwordRequired) PasswordBox.Focus(); };
        }

        private static void Tint(FrameworkElement card, string brushKey) =>
            card.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, brushKey);

        /// <summary>كارت وقسم الحضور — بيتعاد بعد "سجّل الباقيين حاضرين"</summary>
        private void RefreshAttendance()
        {
            var missing = _attendanceResolved ? 0 : _missingWorkers.Count;
            var present = Math.Max(0, _checklist.ActiveWorkerCount - missing);
            AttendanceCount.Text = $"{present} من {_checklist.ActiveWorkerCount}";
            Tint(AttendanceCard, missing == 0 ? "SuccessBgBrush" : "DangerTintBrush");

            WorkersSection.Visibility = missing > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (missing > 0)
            {
                WorkersTitle.Text = missing == _checklist.ActiveWorkerCount
                    ? $"محدش اتسجّل له حضور النهارده ({missing} عامل)"
                    : $"{missing} عامل من غير حضور النهارده";
                MarkPresentButton.Visibility = _markPresentAsync is null ? Visibility.Collapsed : Visibility.Visible;
                RenderWorkerChips();
            }

            var importantIssues = missing > 0 || _checklist.MemoryDueWithNoEntries.Count > 0;
            AllClearBanner.Visibility = importantIssues ? Visibility.Collapsed : Visibility.Visible;
            AcknowledgeBox.Visibility = importantIssues ? Visibility.Visible : Visibility.Collapsed;
            if (!importantIssues) AcknowledgeBox.IsChecked = false;
            UpdateConfirmState();
        }

        private void RenderWorkerChips()
        {
            var filter = WorkersSearch.Text.Trim();
            var names = string.IsNullOrEmpty(filter)
                ? _missingWorkers
                : _missingWorkers.Where(n => n.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

            WorkersChips.ItemsSource = names;
            WorkersBody.Visibility = _workersExpanded ? Visibility.Visible : Visibility.Collapsed;
            WorkersSearch.Visibility = _missingWorkers.Count > ChipPreviewCount ? Visibility.Visible : Visibility.Collapsed;
            WorkersToggleText.Text = _workersExpanded
                ? "اخفي الأسماء ▴"
                : $"{string.Join("، ", _missingWorkers.Take(3))}{(_missingWorkers.Count > 3 ? $" و{_missingWorkers.Count - 3} كمان" : "")} — اعرض ▾";
        }

        private void UpdateConfirmState() =>
            ConfirmButton.IsEnabled = AcknowledgeBox.Visibility != Visibility.Visible || AcknowledgeBox.IsChecked == true;

        private void Acknowledge_Changed(object sender, RoutedEventArgs e) => UpdateConfirmState();

        private void WorkersToggle_Click(object sender, RoutedEventArgs e)
        {
            _workersExpanded = !_workersExpanded;
            RenderWorkerChips();
        }

        private void WorkersSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => RenderWorkerChips();

        private void ProductsToggle_Click(object sender, RoutedEventArgs e)
        {
            _productsExpanded = !_productsExpanded;
            ProductsChips.ItemsSource = _checklist.ProductsWithNoEntries;
            ProductsChips.Visibility = _productsExpanded ? Visibility.Visible : Visibility.Collapsed;
            ProductsToggleText.Text = _productsExpanded ? "اخفي الأسماء ▴" : "اعرض الأسماء ▾";
        }

        private void EventsToggle_Click(object sender, RoutedEventArgs e)
        {
            var show = EventsList.Visibility != Visibility.Visible;
            EventsList.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            EventsToggle.Content = show ? "اخفيهم" : "اعرضهم";
        }

        private async void MarkPresent_Click(object sender, RoutedEventArgs e)
        {
            if (_markPresentAsync is null) return;
            MarkPresentButton.IsEnabled = false;
            try
            {
                var error = await _markPresentAsync();
                if (error is not null)
                {
                    ShowError(error);
                    return;
                }
                _attendanceResolved = true;
                HideError();
                RefreshAttendance();
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
            finally
            {
                MarkPresentButton.IsEnabled = true;
            }
        }

        private void OpenDailyEntry_Click(object sender, RoutedEventArgs e) => Finish(DailySignOffOutcome.OpenDailyEntry);

        private void OpenMemory_Click(object sender, RoutedEventArgs e) => Finish(DailySignOffOutcome.OpenMemory);

        private void Defer_Click(object sender, RoutedEventArgs e) => Finish(DailySignOffOutcome.Deferred);

        private async void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmButton.IsEnabled) return;
            ConfirmButton.IsEnabled = false;
            DeferButton.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                var error = await _trySignAsync(PasswordBox.Password);
                if (error is null)
                {
                    Finish(DailySignOffOutcome.Signed);
                    return;
                }

                // كلمة سر غلط (أو أي سبب تاني) — الديالوج يفضل مفتوح والخانة تتمسح
                ShowError(error);
                PasswordBox.Clear();
                PasswordBox.Focus();
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                DeferButton.IsEnabled = true;
                UpdateConfirmState();
            }
        }

        private void Finish(DailySignOffOutcome outcome)
        {
            Outcome = outcome;
            DialogResult = outcome != DailySignOffOutcome.Cancelled;
            Close();
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorBox.Visibility = Visibility.Visible;
        }

        private void HideError() => ErrorBox.Visibility = Visibility.Collapsed;

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}
