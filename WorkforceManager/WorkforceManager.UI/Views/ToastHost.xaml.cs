using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// الإشعارات الطايرة في ركن الشاشة.
    ///
    /// ليه بدل نوافذ الرسايل: نافذة الرسالة **بتوقف شغلك** وتستنى منك
    /// "موافق" — وده صح للسؤال، وغلط تمامًا للخبر. المستخدم اللي حفظ
    /// حاجة عايز يعرف إنها اتحفظت ويكمّل، مش يدوس زرار عشان يكمّل.
    ///
    /// **الأسئلة بتفضل نوافذ** عن قصد: السؤال لازم يوقف، والإشعار اللي
    /// بيروح لوحده مش مكان لقرار.
    ///
    /// مكان واحد بيستقبل كل الإشعارات من أي شاشة عن طريق
    /// <see cref="Notify"/> — الشاشات مش بتعرف إن الحاجة دي موجودة.
    /// </summary>
    public partial class ToastHost : UserControl
    {
        /// <summary>مدة عرض الإشعار العادي</summary>
        private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(4);

        /// <summary>التحذير بيقعد أطول — المستخدم محتاج وقت يقراه</summary>
        private static readonly TimeSpan WarnLifetime = TimeSpan.FromSeconds(7);

        /// <summary>أكتر من كده بيتحوّل لحيطة إشعارات</summary>
        private const int MaxVisible = 4;

        private readonly ObservableCollection<ToastItem> _items = new();

        public ToastHost()
        {
            InitializeComponent();
            Items.ItemsSource = _items;
        }

        /// <summary>الحاوية الحالية — بيتحطّ مرة واحدة من النافذة الرئيسية</summary>
        public static ToastHost? Current { get; private set; }

        public void Register() => Current = this;

        public void Show(string message, string? title, ToastKind kind)
        {
            var item = new ToastItem(message, title, kind, Remove);

            _items.Add(item);

            while (_items.Count > MaxVisible) _items.RemoveAt(0);

            var timer = new DispatcherTimer
            {
                Interval = kind == ToastKind.Warn ? WarnLifetime : Lifetime
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Remove(item);
            };
            timer.Start();
        }

        private void Remove(ToastItem item) => _items.Remove(item);
    }

    public enum ToastKind { Info, Success, Warn }

    /// <summary>إشعار واحد معروض</summary>
    public class ToastItem
    {
        private readonly Action<ToastItem> _dismiss;

        public ToastItem(string message, string? title, ToastKind kind, Action<ToastItem> dismiss)
        {
            Message = message;
            Title = title ?? "";
            Kind = kind;
            _dismiss = dismiss;
            DismissCommand = new DismissToastCommand(() => _dismiss(this));
        }

        public string Message { get; }
        public string Title { get; }
        public bool HasTitle => Title.Length > 0;
        public ToastKind Kind { get; }

        public ICommand DismissCommand { get; }

        /// <summary>الأيقونة واللون بيتحددوا من النوع — مفيش نداء بيختارهم بنفسه</summary>
        public string Icon => Kind switch
        {
            ToastKind.Success => "CheckCircleOutline",
            ToastKind.Warn => "AlertOutline",
            _ => "InformationOutline"
        };

        public Brush Accent => (Brush)Application.Current.Resources[Kind switch
        {
            ToastKind.Success => "GoodBrush",
            ToastKind.Warn => "WarnBrush",
            _ => "InfoBrush"
        }];
    }

    /// <summary>أمر بسيط لزرار الإخفاء — مش محتاج MVVM كامل لسطر واحد</summary>
    public class DismissToastCommand : ICommand
    {
        private readonly Action _run;

        public DismissToastCommand(Action run) => _run = run;

        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _run();
    }
}
