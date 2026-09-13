using WorkforceManager.UI.Views;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// تدقيق لقى: تعديل ذاتي (رئيس قسم بيعدّل بياناته هو) كان بيقفل
    /// المسمّى بس (RoleBox.IsEnabled) ومش بيقفل سعر اليومية — يعني رئيس
    /// قسم يقدر يكتب سعر يومية جديد لنفسه، وUpdateWorkerAsync كانت
    /// هتنفّذه من غير أي باسورد عمليات لو الميزة دي مش متفعّلة أصلًا
    /// (البوابة "مفتوحة" لما مفيش كلمة سر متسجّلة — قرار متعمّد وموثّق
    /// في OperationsPasswordService، بس مش المفروض يوصل لحد يزوّد راتبه
    /// لنفسه). القفل بقى على الاتنين، وWagePanel لسه بيتخفى لمدير القسم
    /// زي ما كان (شوف RoleBox_SelectionChanged).
    /// </summary>
    [Collection("WPF")]
    public class DepartmentAccountEditDialogTests
    {
        [Fact]
        public void SelfEdit_LocksBothRoleAndWage_NotJustRole()
        {
            WpfThread.Run<object?>(() =>
            {
                var dialog = new DepartmentAccountEditDialog(
                    isEditMode: true, restrictToSelf: true, hasOperationsPassword: false);

                var roleBox = (System.Windows.Controls.ComboBox)dialog.FindName("RoleBox")!;
                var wageBox = (System.Windows.Controls.TextBox)dialog.FindName("WageBox")!;

                Assert.False(roleBox.IsEnabled);
                Assert.False(wageBox.IsEnabled);

                return null;
            });
        }

        [Fact]
        public void AdminEdit_LeavesBothRoleAndWageEditable()
        {
            WpfThread.Run<object?>(() =>
            {
                var dialog = new DepartmentAccountEditDialog(
                    isEditMode: true, restrictToSelf: false, hasOperationsPassword: false);

                var roleBox = (System.Windows.Controls.ComboBox)dialog.FindName("RoleBox")!;
                var wageBox = (System.Windows.Controls.TextBox)dialog.FindName("WageBox")!;

                Assert.True(roleBox.IsEnabled);
                Assert.True(wageBox.IsEnabled);

                return null;
            });
        }
    }
}
