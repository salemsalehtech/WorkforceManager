using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WorkforceManager.UI
{
    public enum ShortcutAction { None, QuickAdd, Save, Search }

    /// <summary>
    /// اختصارات الكيبورد العامة — الأجزاء النقية منها هنا عشان تتختبر من غير
    /// نافذة (KeyboardShortcutsTests). الخريطة الكاملة للمستخدم في الدليل
    /// (KeyboardShortcutsContent) وفي CLAUDE.md.
    /// </summary>
    public static class KeyboardShortcuts
    {
        /// <summary>Ctrl لوحده بس — Ctrl+Shift+S مثلًا مش اختصار، وكتابة حرف N عادي عمره ما يوصل هنا</summary>
        public static ShortcutAction Resolve(Key key, ModifierKeys modifiers)
        {
            if (modifiers != ModifierKeys.Control) return ShortcutAction.None;

            return key switch
            {
                Key.N => ShortcutAction.QuickAdd,
                Key.S => ShortcutAction.Save,
                Key.F => ShortcutAction.Search,
                _ => ShortcutAction.None
            };
        }

        /// <summary>بينفّذ أمر موجود بس لو CanExecute بيسمح — نفس اللي الزرار المربوط بيه كان هيعمله</summary>
        public static bool TryExecute(ICommand? command, object? parameter = null)
        {
            if (command is null || !command.CanExecute(parameter)) return false;
            command.Execute(parameter);
            return true;
        }

        /// <summary>Ctrl+F: المؤشر في خانة البحث والنص القديم متحدد، فالكتابة الجديدة بتستبدله على طول</summary>
        public static bool FocusSearchBox(TextBox? box)
        {
            if (box is null || !box.IsVisible) return false;
            box.Focus();
            box.SelectAll();
            return true;
        }

        /// <summary>
        /// Enter بينقل للخانة اللي بعدها في خانات الإدخال العادية بس: TextBox
        /// من سطر واحد، أو ComboBox قايمته مقفولة (لو مفتوحة Enter بيختار منها)
        /// </summary>
        public static bool ShouldAdvanceOnEnter(object? element) => element switch
        {
            TextBox box => !box.AcceptsReturn,
            ComboBox combo => !combo.IsDropDownOpen,
            _ => false
        };

        /// <summary>
        /// الأسهم ↑/↓ في خانة إدخال من سطر واحد بتنقل للخانة اللي فوق/تحت — زي جدول إكسل: كمية الخطة لكل منتج،
        /// عدد القطع لكل عامل، نصيب كل عامل... من غير ماوس ولا Tab. معالج واحد على مستوى النوع (App.OnStartup)
        /// فبيشتغل في كل الشاشات والديالوجات. التنقل "مكاني" (الخانة اللي تحت فعلًا على الشاشة)، ولو مفيش
        /// خانة تحت/فوق مفيش حركة. الخانات اللي الأسهم ليها معنى تاني جواها مستبعدة
        /// (شوف <see cref="ShouldMoveWithArrows"/>).
        /// </summary>
        public static void ArrowNavigation(object sender, KeyEventArgs e)
        {
            if (e.Handled || e.Key is not (Key.Up or Key.Down) || Keyboard.Modifiers != ModifierKeys.None) return;
            // متسجّل على مستوى النافذة (أول واحد بيشوف الضغطة) والخانة هي اللي عليها الفوكس فعلًا — مش sender/OriginalSource
            if (Keyboard.FocusedElement is not TextBox box) return;
            if (!ShouldMoveWithArrows(box)) return;

            if (FindVerticalNeighbor(box, e.Key == Key.Down) is not { } target) return;

            target.Focus();
            target.SelectAll();
            target.BringIntoView();
            e.Handled = true;

            KeepFocusThroughReload(target);
        }

        /// <summary>
        /// شاشات كتير بتحفظ الخانة لما تسيبها (LostFocus) وبعدين **بتعيد تحميل القايمة** — الصفوف بتتبني من جديد،
        /// فالخانة اللي السهم نقلنا لها بتتشال من الشاشة والفوكس بيقع على النافذة، والسهم التاني مابيلاقيش خانة
        /// (ده اللي كان بيحصل في "الإنتاج اليومي للمنتجات"). الحل: نفتكر الخانة بالاسم + ترتيبها بين الخانات اللي
        /// بنفس الاسم، ولو الفوكس ضاع خلال ثانية بعد النقل، نرجّعه لنفس الترتيب في القايمة الجديدة.
        /// </summary>
        private static void KeepFocusThroughReload(TextBox target)
        {
            if (Window.GetWindow(target) is not { } root || string.IsNullOrEmpty(target.Name)) return;

            var index = SameNameBoxes(root, target.Name).IndexOf(target);
            if (index < 0) return;

            var tries = 0;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            timer.Tick += (_, _) =>
            {
                tries++;
                var focusedElement = Keyboard.FocusedElement;

                // الفوكس لسه في مكان حقيقي (خانة ظاهرة، أو المستخدم دوس زرار) = مانتدخلش؛ بنرجّعه بس لو وقع على النافذة/اتشال
                var lost = focusedElement is null or Window || focusedElement is FrameworkElement { IsLoaded: false };
                if (!lost)
                {
                    if (tries >= 8) timer.Stop(); // الفوكس ثابت — خلاص
                    return;
                }

                var boxes = SameNameBoxes(root, target.Name);
                if (index < boxes.Count)
                {
                    var again = boxes[index];
                    again.Focus();
                    again.SelectAll();
                    again.BringIntoView();
                }

                if (tries >= 8) timer.Stop();
            };
            timer.Start();
        }

        private static List<TextBox> SameNameBoxes(Window root, string name) =>
            Descendants<TextBox>(root)
                .Where(t => t.Name == name && t.IsVisible)
                .OrderBy(t => t.TranslatePoint(new Point(0, 0), root).Y)
                .ThenBy(t => t.TranslatePoint(new Point(0, 0), root).X)
                .ToList();

        /// <summary>
        /// أقرب خانة إدخال فعلًا تحت/فوق الخانة الحالية على الشاشة (بالإحداثيات، مش ترتيب Tab) — الأولوية لنفس
        /// العمود، فكمية منتج بتنزل لكمية المنتج اللي تحته حتى لو بينهم زراير. تنقّل WPF المكاني الجاهز
        /// (FocusNavigationDirection.Down) كان بيقف على الزراير اللي في نفس الصف أو مابيلاقيش حاجة جوّه القوائم.
        /// </summary>
        private static TextBox? FindVerticalNeighbor(TextBox box, bool down)
        {
            if (Window.GetWindow(box) is not { } root) return null;

            var origin = box.TranslatePoint(new Point(box.ActualWidth / 2, box.ActualHeight / 2), root);
            TextBox? best = null;
            var bestScore = double.MaxValue;

            foreach (var candidate in Descendants<TextBox>(root))
            {
                if (ReferenceEquals(candidate, box) || !candidate.IsVisible || !candidate.IsEnabled || !candidate.Focusable
                    || !ShouldMoveWithArrows(candidate)) continue;

                var p = candidate.TranslatePoint(new Point(candidate.ActualWidth / 2, candidate.ActualHeight / 2), root);
                var dy = p.Y - origin.Y;
                if (down ? dy <= 4 : dy >= -4) continue;

                var score = Math.Abs(dy) + Math.Abs(p.X - origin.X) * 2; // نفس العمود الأول
                if (score < bestScore) { bestScore = score; best = candidate; }
            }

            return best;
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) yield return match;
                foreach (var deeper in Descendants<T>(child)) yield return deeper;
            }
        }

        /// <summary>
        /// مستبعد من التنقل بالأسهم: خانة متعددة الأسطر، خانات البحث (السهم فيها بيمشي في الاقتراحات)،
        /// وأي خانة جوّه قايمة/جدول/كومبو/تاريخ (الأسهم هناك ليها شغلها الأصلي).
        /// </summary>
        public static bool ShouldMoveWithArrows(TextBox box)
        {
            if (box.AcceptsReturn || box.IsReadOnly) return false;
            if (box.Name.Contains("Search", StringComparison.OrdinalIgnoreCase)) return false;

            for (DependencyObject? node = box; node is not null; node = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node))
            {
                if (node is ComboBox or DataGrid or ListBox or DatePicker or Calendar or Views.WorkerSearchBox) return false;
            }

            return true;
        }

        /// <summary>
        /// Ctrl+S جوّه أي ديالوج = دوسة على زرار الحفظ الافتراضي (IsDefault)
        /// بالظبط — عن طريق ButtonAutomationPeer مش RaiseEvent، عشان نفس
        /// التحقق اللي في Save_Click يشتغل. زرار مقفول (IsEnabled=false) أو
        /// مخفي = مفيش حاجة.
        /// </summary>
        public static bool TryClickDefaultButton(DependencyObject root)
        {
            var button = FindDefaultButton(root);
            if (button is null) return false;

            if (new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke)
                return false;
            invoke.Invoke();
            return true;
        }

        /// <summary>
        /// زرار ⋮ جوّه كارت: بيطلع في الشجرة لأول عنصر عنده ContextMenu (الكارت)
        /// ويفتحها عليه — نفس القائمة بالظبط اللي الزرار اليمين بيفتحها، فمفيش
        /// قائمة تانية تتحدّث. بيرجّع false لو مفيش قائمة فوق.
        /// </summary>
        public static bool OpenOwningContextMenu(DependencyObject? from)
        {
            for (var node = from is null ? null : VisualTreeHelper.GetParent(from); node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is not FrameworkElement { ContextMenu: { } menu } owner) continue;
                if (!menu.IsEnabled) return false;

                menu.PlacementTarget = owner;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.IsOpen = true;
                return true;
            }
            return false;
        }

        private static Button? FindDefaultButton(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Button { IsDefault: true, IsEnabled: true, IsVisible: true } button) return button;
                if (FindDefaultButton(child) is { } found) return found;
            }
            return null;
        }
    }
}
