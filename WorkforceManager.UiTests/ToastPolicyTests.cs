using WorkforceManager.UI.Views;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>قواعد إشعار "تراجع": المدة، التكدّس، الإيقاف والنافذة مش نشطة، والتنفيذ مرة واحدة</summary>
    public class ToastPolicyTests
    {
        private static ToastItem Plain() => new("خبر", null, ToastKind.Success, _ => { });

        private static ToastItem WithUndo() =>
            new("اتوقف", null, ToastKind.Success, _ => { }, "تراجع", () => Task.CompletedTask);

        [Fact]
        public void UndoToast_Lives8Seconds_OthersUnchanged()
        {
            Assert.Equal(TimeSpan.FromSeconds(8), ToastPolicy.LifetimeFor(ToastKind.Success, hasAction: true));
            Assert.Equal(TimeSpan.FromSeconds(4), ToastPolicy.LifetimeFor(ToastKind.Success, hasAction: false));
            Assert.Equal(TimeSpan.FromSeconds(7), ToastPolicy.LifetimeFor(ToastKind.Warn, hasAction: false));
        }

        [Fact]
        public void Eviction_DropsOldestPlainToast_KeepingEarlierUndo()
        {
            // عملية تانية ورا الأولى بسرعة مابتضيّعش تراجع الأولى
            var items = new List<ToastItem> { WithUndo(), Plain(), WithUndo(), Plain(), WithUndo() };

            Assert.Equal(1, ToastPolicy.EvictionIndex(items));
        }

        [Fact]
        public void Eviction_AllUndo_DropsOldest()
        {
            var items = new List<ToastItem> { WithUndo(), WithUndo(), WithUndo(), WithUndo(), WithUndo() };

            Assert.Equal(0, ToastPolicy.EvictionIndex(items));
        }

        [Fact]
        public void Countdown_ExpiresAfterLifetime()
        {
            var countdown = new ToastCountdown(TimeSpan.FromSeconds(8), pauseWhileInactive: true);

            Assert.False(countdown.Advance(TimeSpan.FromSeconds(7.8), windowActive: true));
            Assert.True(countdown.Advance(TimeSpan.FromSeconds(0.2), windowActive: true));
        }

        [Fact]
        public void Countdown_PausesWhileWindowInactive()
        {
            // ديالوج فوق النافذة (بروفايل العامل) — التراجع مايخلصش وهو مستخبي
            var countdown = new ToastCountdown(TimeSpan.FromSeconds(8), pauseWhileInactive: true);

            Assert.False(countdown.Advance(TimeSpan.FromSeconds(30), windowActive: false));
            Assert.Equal(TimeSpan.FromSeconds(8), countdown.Remaining);
        }

        [Fact]
        public void PlainToastCountdown_DoesNotPause()
        {
            var countdown = new ToastCountdown(TimeSpan.FromSeconds(4), pauseWhileInactive: false);

            Assert.True(countdown.Advance(TimeSpan.FromSeconds(5), windowActive: false));
        }

        [Fact]
        public void UndoAction_RunsOnlyOnce_AndDismissesToast()
        {
            var runs = 0;
            var dismissed = 0;
            var command = new ToastActionCommand(() => { runs++; return Task.CompletedTask; }, () => dismissed++);

            command.Execute(null);
            command.Execute(null);

            Assert.Equal(1, runs);
            Assert.Equal(1, dismissed);
            Assert.False(command.CanExecute(null));
        }

        [Fact]
        public void PlainToast_HasNoAction()
        {
            Assert.False(Plain().HasAction);
            Assert.True(WithUndo().HasAction);
        }
    }
}
