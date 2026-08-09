using System.Windows;
using WorkforceManager.UI.Views;

namespace WorkforceManager.UI
{
    /// <summary>
    /// رسايل المستخدم في مكان واحد.
    ///
    /// القاعدة اللي الملف ده قايم عليها: **اللي بيقول خبر مبيوقفش
    /// الشغل، واللي بيسأل بيوقفه.**
    ///
    /// قبل كده كان كل حاجة نافذة رسالة بتستنى "موافق" — حتى "تم
    /// الحفظ". المستخدم اللي حفظ حاجة عايز يعرف إنها اتحفظت ويكمّل، مش
    /// يدوس زرار عشان يكمّل. الأخبار والتحذيرات بقت إشعارات طايرة في
    /// الركن بتروح لوحدها، والأسئلة بس هي اللي فضلت نوافذ.
    ///
    /// التحويل ده كلّف تعديل ملف واحد لأن كل النداءات كانت اتجمّعت هنا
    /// قبل كده — لو كانت لسه 72 نداء متفرقين كان لازم يتلمسوا واحد واحد.
    /// </summary>
    public static class Notify
    {
        /// <summary>حصلت حاجة كويسة — إشعار أخضر بيروح لوحده</summary>
        public static void Success(string message, string? title = null) =>
            Toast(message, title, ToastKind.Success);

        /// <summary>خبر أو نتيجة، مفيش قرار مطلوب</summary>
        public static void Info(string message, string? title = null) =>
            Toast(message, title, ToastKind.Info);

        /// <summary>
        /// حاجة مش هتتنفّذ والمستخدم محتاج يعرف ليه.
        ///
        /// إشعار مش نافذة: التحذير بيوصل والشغل بيكمّل. بيقعد أطول من
        /// الإشعار العادي عشان يتقرا.
        /// </summary>
        public static void Warn(string message, string? title = null) =>
            Toast(message, title ?? "مش هينفع", ToastKind.Warn);

        /// <summary>
        /// خطأ لازم المستخدم يشوفه أكيد (فشل حفظ، مشكلة في ملف).
        ///
        /// **نافذة مش إشعار**: الإشعار بيروح لوحده، والحاجة اللي فشلت
        /// ميصحّش تعدّي من غير ما حد ياخد باله.
        /// </summary>
        public static void Error(string message, string title = "حصل خطأ") =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

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

        /// <summary>
        /// بيوصّل الإشعار للحاوية. لو الحاوية لسه ماتسجّلتش (رسالة قبل
        /// ما النافذة الرئيسية تفتح، زي "البرنامج شغال بالفعل")
        /// بيرجع لنافذة الرسالة — الرسالة توصل أهم من شكلها.
        /// </summary>
        private static void Toast(string message, string? title, ToastKind kind)
        {
            var host = ToastHost.Current;

            if (host is null)
            {
                MessageBox.Show(message, title ?? "تنبيه",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            host.Dispatcher.Invoke(() => host.Show(message, title, kind));
        }
    }
}
