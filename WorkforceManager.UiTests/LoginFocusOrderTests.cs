using WorkforceManager.UI.Views;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>ترتيب الأسهم فوق/تحت في شاشة الدخول — LoginFocusOrder دالة نقية</summary>
    public class LoginFocusOrderTests
    {
        [Theory]
        [InlineData(LoginFocusTarget.Username, true, LoginFocusTarget.Password)]
        [InlineData(LoginFocusTarget.Password, true, LoginFocusTarget.LoginButton)]
        [InlineData(LoginFocusTarget.LoginButton, false, LoginFocusTarget.Password)]
        [InlineData(LoginFocusTarget.Password, false, LoginFocusTarget.Username)]
        public void ArrowMovesToAdjacentControl(LoginFocusTarget current, bool goingDown, LoginFocusTarget expected)
        {
            Assert.Equal(expected, LoginFocusOrder.Next(current, goingDown));
        }

        [Fact]
        public void UpFromUsername_StaysPut()
        {
            Assert.Null(LoginFocusOrder.Next(LoginFocusTarget.Username, goingDown: false));
        }

        [Fact]
        public void DownFromLoginButton_StaysPut()
        {
            Assert.Null(LoginFocusOrder.Next(LoginFocusTarget.LoginButton, goingDown: true));
        }
    }
}
