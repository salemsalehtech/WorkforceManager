using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// قاعدة مشتركة لكل نوافذ الـDialog بلا إطار نظام: بطاقة عائمة (حدود
    /// مدورة + ظل + هيدر ملوّن بزرار إغلاق) — كانت الشكل ده متكرر حرفيًا
    /// (Border+DropShadow+هيدر+Window_Drag) في 29 ملف Dialog منفصل، شوف
    /// CLAUDE.md. الشكل نفسه معرّف مرة واحدة في نمط ضمني بـApp.xaml.
    ///
    /// كل نافذة بترث من الكلاس ده بدل Window مباشرة، وتحط محتواها العادي
    /// في Content زي أي Window، وعنوان/تحت-عنوان الهيدر (اللي شكله بيختلف
    /// من نافذة لتانية — ثابت، متغيّر من الكود، إلخ) في HeaderContent.
    /// LoginWindow وMessageDialog شكل الهيدر بتاعهم مختلف أصلًا فمش بيرثوا
    /// من هنا.
    /// </summary>
    public class ChromelessDialogWindow : Window
    {
        static ChromelessDialogWindow()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(ChromelessDialogWindow), new FrameworkPropertyMetadata(typeof(ChromelessDialogWindow)));
        }

        /// <summary>لون خلفية الهيدر — دهبي افتراضيًا (النمط الضمني)، بيتغيّر لأحمر تحذيري للحذف مثلًا</summary>
        public static readonly DependencyProperty HeaderBrushProperty = DependencyProperty.Register(
            nameof(HeaderBrush), typeof(Brush), typeof(ChromelessDialogWindow));

        public Brush? HeaderBrush
        {
            get => (Brush?)GetValue(HeaderBrushProperty);
            set => SetValue(HeaderBrushProperty, value);
        }

        /// <summary>محتوى العنوان/تحت-العنوان جوه الهيدر — شكله بيختلف من نافذة لتانية</summary>
        public static readonly DependencyProperty HeaderContentProperty = DependencyProperty.Register(
            nameof(HeaderContent), typeof(object), typeof(ChromelessDialogWindow));

        public object? HeaderContent
        {
            get => GetValue(HeaderContentProperty);
            set => SetValue(HeaderContentProperty, value);
        }

        /// <summary>سحب النافذة من الهيدر — كان مكرر كـWindow_Drag في كل ملف Dialog على حدة</summary>
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (GetTemplateChild("PART_Header") is UIElement header)
                header.MouseLeftButtonDown += (_, e) =>
                {
                    if (e.ButtonState == MouseButtonState.Pressed) DragMove();
                };
        }
    }
}
