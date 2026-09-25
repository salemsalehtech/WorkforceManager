using System.Collections.ObjectModel;
using System.Diagnostics;
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
    /// بيروح لوحده مش مكان لقرار. الاستثناء الوحيد "تراجع": ده مش سؤال،
    /// العملية اتنفذت خلاص — الزرار فرصة اختيارية لعكسها (Notify.SuccessWithUndo).
    ///
    /// مكان واحد بيستقبل كل الإشعارات من أي شاشة عن طريق
    /// <see cref="Notify"/> — الشاشات مش بتعرف إن الحاجة دي موجودة.
    /// </summary>
    public partial class ToastHost : UserControl
    {
        /// <summary>كل قد إيه العدّاد بيتحدّث — دقة كفاية لعدّ بالثواني</summary>
        private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(200);

        private readonly ObservableCollection<ToastItem> _items = new();

        public ToastHost()
        {
            InitializeComponent();
            Items.ItemsSource = _items;
        }

        /// <summary>الحاوية الحالية — بيتحطّ مرة واحدة من النافذة الرئيسية</summary>
        public static ToastHost? Current { get; private set; }

        public void Register() => Current = this;

        /// <summary>
        /// بيفك التسجيل لما نافذة الجلسة تتقفل (تسجيل خروج).
        ///
        /// من غيرها الـ static بيفضل ماسك حاوية نافذة مقفولة — يعني
        /// شجرتها البصرية كلها بتفضل عايشة لحد ما جلسة جديدة تسجّل
        /// حاوية تانية، وأي إشعار بين الاتنين كان هيروح لنافذة مش
        /// معروضة ويضيع في صمت. الشرط (ReferenceEquals) عشان حاوية
        /// جديدة سجّلت نفسها بالفعل ماتتشالش بالغلط.
        /// </summary>
        public void Unregister()
        {
            if (ReferenceEquals(Current, this)) Current = null;
        }

        /// <param name="actionText">نص زرار إجراء على الإشعار (زي "تراجع") — null = إشعار عادي</param>
        /// <param name="action">اللي بيتنفّذ لما المستخدم يدوس الزرار — مرة واحدة بس</param>
        public void Show(string message, string? title, ToastKind kind,
            string? actionText = null, Func<Task>? action = null)
        {
            var item = new ToastItem(message, title, kind, Remove, actionText, action);

            _items.Add(item);

            while (_items.Count > ToastPolicy.MaxVisible) _items.RemoveAt(ToastPolicy.EvictionIndex(_items));

            // عدّاد بخطوات صغيرة بدل Timer بمدة الإشعار كلها — عشان إشعار
            // التراجع يقدر يوقف العد والنافذة مش نشطة (شوف ToastCountdown)
            var countdown = new ToastCountdown(ToastPolicy.LifetimeFor(kind, item.HasAction), pauseWhileInactive: item.HasAction);
            var clock = Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TickInterval };
            timer.Tick += (_, _) =>
            {
                var elapsed = clock.Elapsed;
                clock.Restart();

                // اتشال خلاص (إخفاء بإيد المستخدم، تراجع، أو زحمة) → مفيش داعي يكمّل يعدّ
                if (!_items.Contains(item) || countdown.Advance(elapsed, Window.GetWindow(this)?.IsActive ?? true))
                {
                    timer.Stop();
                    Remove(item);
                }
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

        public ToastItem(string message, string? title, ToastKind kind, Action<ToastItem> dismiss,
            string? actionText = null, Func<Task>? action = null)
        {
            Message = message;
            Title = title ?? "";
            Kind = kind;
            _dismiss = dismiss;
            DismissCommand = new DismissToastCommand(() => _dismiss(this));

            if (actionText is not null && action is not null)
            {
                ActionText = actionText;
                ActionCommand = new ToastActionCommand(action, () => _dismiss(this));
            }
        }

        public string Message { get; }
        public string Title { get; }
        public bool HasTitle => Title.Length > 0;
        public ToastKind Kind { get; }

        public ICommand DismissCommand { get; }

        /// <summary>زرار إجراء اختياري على الإشعار (زي "تراجع")</summary>
        public string? ActionText { get; }
        public ICommand? ActionCommand { get; }
        public bool HasAction => ActionCommand is not null;

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
