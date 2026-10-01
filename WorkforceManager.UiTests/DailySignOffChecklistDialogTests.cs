using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Models;
using WorkforceManager.UI.Views;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// قايمة اكتمال البيانات جوّه ديالوج توقيع نهاية اليوم — الحضور الناقص
    /// والذاكرة المستحقة محتاجين تأكيد صريح قبل ما زرار التوقيع يشتغل؛ منتج
    /// من غير إنتاج إفادة بس (مش كل منتج بيتشغل كل يوم). شوف
    /// DailySignOffSummaryDialog.xaml.cs.
    /// </summary>
    [Collection("WPF")]
    public class DailySignOffChecklistDialogTests
    {
        private static readonly List<ActivityEvent> NoEvents = new();

        private static DailySignOffSummaryDialog Build(DailySignOffChecklist checklist, Func<Task<string?>>? markPresent = null) =>
            new(DateTime.Today, NoEvents, checklist, passwordRequired: true,
                trySignAsync: _ => Task.FromResult<string?>(null), markPresentAsync: markPresent);

        private static DailySignOffChecklist MissingAttendance() => new()
        {
            WorkersWithNoAttendance = new[] { "أحمد" },
            WorkerIdsWithNoAttendance = new[] { 1 },
            ActiveWorkerCount = 2
        };

        [Fact]
        public void ConfirmDisabled_UntilAcknowledged_WhenAttendanceIsMissing()
        {
            var (initiallyEnabled, enabledAfterCheck, disabledAfterUncheck) = WpfThread.Run(() =>
            {
                var dialog = Build(MissingAttendance());

                var confirmButton = (Button)dialog.FindName("ConfirmButton")!;
                var acknowledge = (CheckBox)dialog.FindName("AcknowledgeBox")!;

                var before = confirmButton.IsEnabled;
                acknowledge.IsChecked = true;
                var afterCheck = confirmButton.IsEnabled;
                acknowledge.IsChecked = false;
                var afterUncheck = confirmButton.IsEnabled;

                return (before, afterCheck, afterUncheck);
            });

            Assert.False(initiallyEnabled);
            Assert.True(enabledAfterCheck);
            Assert.False(disabledAfterUncheck);
        }

        [Fact]
        public void ProductsWithoutEntries_AreInformationOnly()
        {
            var (confirmEnabled, acknowledgeVisible, productsVisible) = WpfThread.Run(() =>
            {
                var dialog = Build(new DailySignOffChecklist { ProductsWithNoEntries = new[] { "دبلة" }, ActiveProductCount = 3 });

                return (((Button)dialog.FindName("ConfirmButton")!).IsEnabled,
                        ((CheckBox)dialog.FindName("AcknowledgeBox")!).Visibility == Visibility.Visible,
                        ((Border)dialog.FindName("ProductsSection")!).Visibility == Visibility.Visible);
            });

            Assert.True(confirmEnabled);
            Assert.False(acknowledgeVisible);
            Assert.True(productsVisible);
        }

        [Fact]
        public void ConfirmEnabled_AndAcknowledgeHidden_WhenChecklistIsClean()
        {
            var (confirmEnabled, acknowledgeVisible) = WpfThread.Run(() =>
            {
                var dialog = Build(new DailySignOffChecklist());

                return (((Button)dialog.FindName("ConfirmButton")!).IsEnabled,
                        ((CheckBox)dialog.FindName("AcknowledgeBox")!).Visibility == Visibility.Visible);
            });

            Assert.True(confirmEnabled);
            Assert.False(acknowledgeVisible);
        }

        [Fact]
        public async Task MarkRemainingPresent_ClearsTheAttendanceGap()
        {
            var called = false;
            var dialog = WpfThread.Run(() => Build(MissingAttendance(), () => { called = true; return Task.FromResult<string?>(null); }));

            await WpfThread.Run(async () =>
            {
                var button = (Button)dialog.FindName("MarkPresentButton")!;
                button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                await Task.Delay(50);
            });

            var (sectionVisible, confirmEnabled) = WpfThread.Run(() =>
                (((Border)dialog.FindName("WorkersSection")!).Visibility == Visibility.Visible,
                 ((Button)dialog.FindName("ConfirmButton")!).IsEnabled));

            Assert.True(called);
            Assert.False(sectionVisible);
            Assert.True(confirmEnabled);
        }
    }
}
