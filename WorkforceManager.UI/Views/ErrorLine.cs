using System.Windows;
using System.Windows.Controls;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// سطر الخطأ الأحمر اللي جوّه كل نافذة إدخال.
    ///
    /// عشره نوافذ فيها <c>ErrorText</c>، وكل واحدة كانت بتكتب سطرين
    /// (النص + الإظهار) بإيدها — تلاتة منهم لفّوهم في دالة
    /// <c>ShowError</c> خاصة بيهم بنفس الجسم بالحرف، والباقي كان
    /// بيكررهم في مكانهم.
    ///
    /// النتيجة كانت إن سطر الخطأ ممكن يفضل ظاهر من محاولة فاتت وانت
    /// بتحاول تاني — نافذتين كانوا بينضّفوه ونافذة لأ. الامتدادين دول
    /// بيخلوا الإظهار والإخفاء حاجة واحدة اسمها واضح.
    /// </summary>
    public static class ErrorLine
    {
        /// <summary>يعرض رسالة الخطأ في السطر</summary>
        public static void ShowError(this TextBlock line, string message)
        {
            line.Text = message;
            line.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// يخفي السطر. بيتنادى قبل كل محاولة جديدة عشان خطأ المحاولة
        /// اللي فاتت ميفضلش معروض جنب إدخال اتصلّح خلاص.
        /// </summary>
        public static void ClearError(this TextBlock line)
        {
            line.Text = string.Empty;
            line.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// نفس الفكرة للنوافذ اللي خطأها جوّه صندوق ملوّن مش سطر عريان
        /// (نوافذ كلمة السر والحساب): النص جوّه، والإظهار على الصندوق.
        ///
        /// دالة تانية مش معامل زيادة على اللي فوق، لأن دول عنصرين
        /// مختلفين على الشاشة — والدالة اللي بتاخد "يا إما ده يا إما ده"
        /// بتبقى أصعب في القراءة من اتنين كل واحدة اسمها بيقول شغلها.
        /// </summary>
        public static void ShowError(this UIElement box, TextBlock line, string message)
        {
            line.Text = message;
            box.Visibility = Visibility.Visible;
        }
    }
}
