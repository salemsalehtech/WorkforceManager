using System.Windows;

namespace WorkforceManager.UI
{
    /// <summary>
    /// رسايل المستخدم في مكان واحد.
    ///
    /// كان فيه ٧٨ نداء لـ MessageBox.Show متفرقين، كل واحد بيختار
    /// أزراره وأيقونته بنفسه — فنفس نوع الرسالة كان بيطلع بشكلين
    /// مختلفين في شاشتين. هنا كل نوع رسالة له دالة، والشكل بيتحدد مرة
    /// واحدة.
    /// </summary>
    public static class Notify
    {
        /// <summary>حاجة مش هتتنفّذ، والمستخدم محتاج يعرف ليه</summary>
        public static void Warn(string message, string title = "مش هينفع") =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

        /// <summary>خبر أو نتيجة — مفيش قرار مطلوب</summary>
        public static void Info(string message, string title = "تنبيه") =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

        /// <summary>سؤال عادي — الافتراضي "أيوه"</summary>
        public static bool Ask(string message, string title = "تأكيد") =>
            MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
                == MessageBoxResult.Yes;

        /// <summary>
        /// سؤال على حاجة مش بترجع (حذف، استبدال). الافتراضي **"لأ"**
        /// عن قصد: ضغطة Enter بالغلط مالهاش حق تمسح شغل.
        /// </summary>
        public static bool AskDangerous(string message, string title = "تأكيد") =>
            MessageBox.Show(message, title, MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    }
}
