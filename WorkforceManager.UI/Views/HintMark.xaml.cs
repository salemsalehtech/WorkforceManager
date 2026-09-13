using System.Windows;
using System.Windows.Controls;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// علامة ؟ بتشرح رقم أو حقل. حطها جنب أي حاجة محتاجة توضيح وحدّد
    /// <see cref="Text"/> — الشرح بيظهر لما المستخدم يقف عليها.
    /// </summary>
    public partial class HintMark : UserControl
    {
        public HintMark() => InitializeComponent();

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(HintMark),
                new PropertyMetadata(string.Empty));

        /// <summary>الشرح — جملة أو جملتين، بلغة المستخدم مش بلغة النظام</summary>
        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }
    }
}
