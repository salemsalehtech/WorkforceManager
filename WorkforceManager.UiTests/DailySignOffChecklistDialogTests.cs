using System.Collections.Generic;
using System.Windows.Controls;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Models;
using WorkforceManager.UI.Views;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// قايمة اكتمال البيانات جوّه ديالوج مراجعة التوقيع — إفادة بس، مش
    /// بوابة حجب، لكن لازم تأكيد صريح قبل ما زرار التوقيع يشتغل لو فيه
    /// نواقص، شوف DailySignOffSummaryDialog.xaml.cs.
    /// </summary>
    [Collection("WPF")]
    public class DailySignOffChecklistDialogTests
    {
        private static readonly List<ActivityEvent> NoEvents = new();

        private static DailySignOffSummaryDialog Build(DailySignOffChecklist checklist) =>
            new(DateTime.Today, NoEvents, checklist);

        [Fact]
        public void ConfirmDisabled_UntilAcknowledged_WhenChecklistHasIssues()
        {
            var (initiallyEnabled, enabledAfterCheck, disabledAfterUncheck) = WpfThread.Run(() =>
            {
                var dialog = Build(new DailySignOffChecklist { ProductsWithNoEntries = new[] { "دبلة" } });

                var confirmButton = (Button)dialog.FindName("ConfirmButton")!;
                var acknowledge = (CheckBox)dialog.FindName("ChecklistAcknowledge")!;

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
        public void ConfirmEnabled_AndAcknowledgeHidden_WhenChecklistIsClean()
        {
            var (confirmEnabled, acknowledgeVisible) = WpfThread.Run(() =>
            {
                var dialog = Build(new DailySignOffChecklist());

                var confirmButton = (Button)dialog.FindName("ConfirmButton")!;
                var acknowledge = (CheckBox)dialog.FindName("ChecklistAcknowledge")!;

                return (confirmButton.IsEnabled, acknowledge.Visibility == System.Windows.Visibility.Visible);
            });

            Assert.True(confirmEnabled);
            Assert.False(acknowledgeVisible);
        }
    }
}
