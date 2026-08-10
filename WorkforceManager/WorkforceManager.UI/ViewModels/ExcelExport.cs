using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// طقوس تصدير أي ملف Excel من أي شاشة: اسأل المستخدم يحفظ فين،
    /// اكتب الملف، اعرض إن الحفظ تم واسأله يفتحه، وامسك أي خطأ.
    ///
    /// كانت الأربع خطوات دول مكتوبين بالحرف **أربع مرات** في شاشة
    /// التقارير (الكشف الأسبوعي، كشف الأجور، التقرير العام، تقرير
    /// العامل) — ١٤٠ سطر بيقولوا نفس الحاجة. أي تحسين في التجربة (زي
    /// فتح مجلد الملف بدل الملف، أو رسالة أوضح لما القرص يبقى مليان)
    /// كان لازم يتكتب أربع مرات وإلا الشاشات تختلف عن بعض.
    ///
    /// اللي بيتغيّر بين نداء ونداء تلات حاجات بس: عنوان النافذة، اسم
    /// الملف المقترح، والكتابة نفسها. وكل واحدة منهم معامل هنا.
    /// </summary>
    public static class ExcelExport
    {
        /// <summary>
        /// بيرجّع true لو الملف اتحفظ فعلاً، وfalse لو المستخدم لغى أو
        /// حصل خطأ (الرسالة بتكون اتعرضت له خلاص).
        /// </summary>
        /// <param name="title">عنوان نافذة الحفظ — بيقول للمستخدم بيحفظ إيه</param>
        /// <param name="suggestedFileName">اسم الملف المقترح، من غير امتداد</param>
        /// <param name="writeAsync">بيكتب الملف على المسار اللي المستخدم اختاره</param>
        public static async Task<bool> RunAsync(
            string title, string suggestedFileName, Func<string, Task> writeAsync)
        {
            var dialog = new SaveFileDialog
            {
                Title = title,
                Filter = "Excel (*.xlsx)|*.xlsx",
                FileName = suggestedFileName + ".xlsx"
            };

            if (dialog.ShowDialog() != true) return false;

            // **الكتابة على خيط تاني.** كل المتصلين بيبعتوا دالة بتشتغل
            // كلها فورًا وترجّع Task.CompletedTask، يعني كانت بتتنفّذ على
            // خيط الواجهة: تصدير تقرير سنة (14 ألف سجل تفصيلي) بياخد أكتر
            // من تلات ثواني، والشاشة واقفة فيهم — مفيش رسم ولا استجابة،
            // وويندوز بيكتب "لا يستجيب" لو طوّلت. والمدة بتكبر مع تاريخ
            // المصنع، فاللي كان مقبول في السنة الأولى بيبقى تعليق كامل
            // بعد عشر سنين.
            //
            // آمن هنا لأن كل متصل بيعمل Scope خاص بيه جوه الدالة وبيشتغل
            // على DTOs متبنية خلاص — مفيش DbContext ولا عنصر واجهة
            // بيتلمس من الخيط التاني.
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                await Task.Run(() => writeAsync(dialog.FileName));
            }
            catch (Exception ex)
            {
                // الرسالة بتقول المشكلة إيه — الملف مفتوح، القرص مليان،
                // المجلد محمي. كلها حاجات المستخدم يقدر يعملها
                Notify.Warn($"تعذر حفظ الملف:\n{ex.Message}", "خطأ في التصدير");
                return false;
            }
            finally
            {
                // في finally مش بعد الـ try: لو الكتابة وقعت، المؤشر
                // كان هيفضل ساعة رملية للأبد
                Mouse.OverrideCursor = null;
            }

            if (Notify.Ask($"تم حفظ الملف:\n{dialog.FileName}\n\nتفتحه دلوقتي؟", "تم التصدير"))
                OpenFile(dialog.FileName);

            return true;
        }

        /// <summary>
        /// فتح الملف بالبرنامج الافتراضي. فشل الفتح مش فشل التصدير —
        /// الملف اتحفظ خلاص، فالرسالة بتقوله مكانه بدل ما تقوله "فشل".
        /// </summary>
        private static void OpenFile(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Notify.Warn(
                    $"الملف اتحفظ بس متعرفش يتفتح:\n{ex.Message}\n\nمكانه:\n{path}",
                    "التصدير تم");
            }
        }
    }
}
