namespace WorkforceManager.UI.Views
{
    /// <summary>العناصر اللي الأسهم بتتنقّل بينها في شاشة الدخول، بنفس ترتيب Tab</summary>
    public enum LoginFocusTarget { Username, Password, LoginButton }

    /// <summary>
    /// ترتيب التنقّل بالسهمين فوق/تحت في شاشة الدخول — دالة نقية منفصلة
    /// عن الشاشة عشان تتختبر من غير نافذة (LoginFocusOrderTests). مفيش
    /// لفّ: تحت من الزرار أو فوق من الاسم مابيعملش حاجة.
    /// </summary>
    public static class LoginFocusOrder
    {
        public static LoginFocusTarget? Next(LoginFocusTarget current, bool goingDown) => (current, goingDown) switch
        {
            (LoginFocusTarget.Username, true) => LoginFocusTarget.Password,
            (LoginFocusTarget.Password, true) => LoginFocusTarget.LoginButton,
            (LoginFocusTarget.Password, false) => LoginFocusTarget.Username,
            (LoginFocusTarget.LoginButton, false) => LoginFocusTarget.Password,
            _ => null
        };
    }
}
