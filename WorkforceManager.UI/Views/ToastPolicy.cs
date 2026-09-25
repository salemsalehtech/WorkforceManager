using System.Windows.Input;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// قواعد الإشعارات الطايرة (المدة، مين يتشال لما يزيدوا) — منفصلة عن
    /// ToastHost عشان تتختبر من غير نافذة ولا استنّا وقت حقيقي (ToastPolicyTests).
    /// </summary>
    public static class ToastPolicy
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(4);

        /// <summary>التحذير بيقعد أطول — المستخدم محتاج وقت يقراه</summary>
        public static readonly TimeSpan WarnLifetime = TimeSpan.FromSeconds(7);

        /// <summary>إشعار فيه "تراجع" — وقت كفاية يلاحظ ويدوس، ومش طويل لدرجة يزحم الركن</summary>
        public static readonly TimeSpan UndoLifetime = TimeSpan.FromSeconds(8);

        /// <summary>أكتر من كده بيتحوّل لحيطة إشعارات</summary>
        public const int MaxVisible = 4;

        public static TimeSpan LifetimeFor(ToastKind kind, bool hasAction) =>
            hasAction ? UndoLifetime : kind == ToastKind.Warn ? WarnLifetime : Lifetime;

        /// <summary>
        /// لما الإشعارات تزيد عن MaxVisible: أقدم إشعار **مالوش** تراجع هو اللي
        /// يمشي — عملية تانية وراها بسرعة مايصحّش تضيّع فرصة التراجع عن الأولى.
        /// لو كلهم فيهم تراجع (خمس عمليات في أقل من 8 ثواني)، الأقدم يمشي.
        /// </summary>
        public static int EvictionIndex(IReadOnlyList<ToastItem> items)
        {
            for (var i = 0; i < items.Count; i++)
                if (!items[i].HasAction) return i;
            return 0;
        }
    }

    /// <summary>
    /// عدّاد إشعار واحد. إشعار التراجع بيوقف العد والنافذة مش نشطة (ديالوج
    /// فوقها زي بروفايل العامل، أو المستخدم راح لبرنامج تاني) — وإلا التراجع
    /// كان ممكن يخلص وهو مستخبّي ورا نافذة، والمستخدم عمره ما شافه.
    /// </summary>
    public sealed class ToastCountdown
    {
        private readonly bool _pauseWhileInactive;

        public ToastCountdown(TimeSpan lifetime, bool pauseWhileInactive)
        {
            Remaining = lifetime;
            _pauseWhileInactive = pauseWhileInactive;
        }

        public TimeSpan Remaining { get; private set; }

        /// <summary>بيرجّع true لما الوقت يخلص</summary>
        public bool Advance(TimeSpan elapsed, bool windowActive)
        {
            if (_pauseWhileInactive && !windowActive) return false;
            Remaining -= elapsed;
            return Remaining <= TimeSpan.Zero;
        }
    }

    /// <summary>
    /// زرار الإجراء على الإشعار ("تراجع"): بيشتغل **مرة واحدة بس** — دوسة
    /// تانية بسرعة قبل ما الإشعار يختفي مش هتنفّذ العكس مرتين. الإشعار بيتشال
    /// فورًا، والفشل بيظهر تحذير بدل ما يضيع في صمت.
    /// </summary>
    public sealed class ToastActionCommand : ICommand
    {
        private readonly Func<Task> _action;
        private readonly Action _dismiss;
        private bool _used;

        public ToastActionCommand(Func<Task> action, Action dismiss)
        {
            _action = action;
            _dismiss = dismiss;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => !_used;

        public async void Execute(object? parameter)
        {
            if (_used) return;
            _used = true;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            _dismiss();

            try
            {
                await _action();
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "مش قدرنا نتراجع");
            }
        }
    }
}
