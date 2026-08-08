using System.Windows;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// تشغيل آمن للعمليات غير المنتظرة (fire-and-forget) في الـ ViewModels.
    ///
    /// المشكلة اللي بيحلها: استدعاءات زي "_ = LoadAsync()" (اللي بتحصل
    /// عند تغيير اليوم أو المنتج أو العامل المحدد) لو فشلت، الاستثناء
    /// بيضيع جوه الـ Task ولا بيوصل لمعالج الأخطاء العام — والنتيجة شاشة
    /// فاضية من غير أي رسالة والمستخدم مش فاهم حصل إيه.
    ///
    /// الحل: كل عملية بتتلف في try/catch بيعرض الخطأ بوضوح للمستخدم.
    /// </summary>
    public static class SafeAsync
    {
        /// <summary>يشغّل عملية async بدون انتظار، مع إظهار أي خطأ للمستخدم بدل ما يضيع بصمت</summary>
        public static async void Run(Func<Task> operation)
        {
            try
            {
                await operation();
            }
            catch (Exception ex)
            {
                Notify.Warn($"حصل خطأ أثناء تحميل البيانات:\n\n{ex.Message}", "خطأ");
            }
        }
    }
}
