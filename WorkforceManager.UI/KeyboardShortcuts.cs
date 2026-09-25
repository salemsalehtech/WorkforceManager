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
