using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Helpers;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using WorkforceManager.UI.Views;

namespace WorkforceManager.UI
{
    // ======================= توقيع نهاية اليوم =======================
    // فصل عن MainWindow.xaml.cs الأساسي — بوابة "حفظ نهائي" الواحدة
    // اللي بتتنادى من تلات أماكن (الزرار، محاولة الإغلاق، تسجيل الخروج).
    public partial class MainWindow
    {
        /// <summary>
        /// خروج المستخدم الحالي وعرض شاشة الدخول تاني من غير ما البرنامج
        /// كله يقفل — مهم دلوقتي إن كل حساب إداري بقى ليه يوزر وباسورد
        /// لوحده، فأكتر من حد ممكن يستخدم نفس الجهاز في الشيفت.
        ///
        /// إلغاء شاشة الدخول (زرار الإغلاق) بعد الخروج معناه المستخدم
        /// مش عايز يدخل بحساب تاني دلوقتي — نفس قاعدة الإغلاق وقت فتح
        /// البرنامج (LoginWindow.Close_Click)، فالبرنامج بيقفل بدل ما
        /// يفضل واقف من غير حد داخل بيه.
        /// </summary>
        private async void Logout_Click(object sender, RoutedEventArgs e)
        {
            if (!Notify.Ask("تسجيل الخروج من الحساب الحالي؟", "تأكيد")) return;

            // نفس حارس معالج الإغلاق: ضغطتين سريعتين كانوا هيفتحوا فلوين
            if (_closeFlowRunning) return;
            _closeFlowRunning = true;

            try
            {
                // **نفس بوابة إغلاق البرنامج بالظبط** — تسجيل الخروج
                // بيسيب الجهاز لحساب تاني، فاليوم اللي مش موقّع بيضيع
                // مسؤوليته زي ما بيضيع لما البرنامج يتقفل
                if (!await RunFinalSaveFlowAsync(SignOffTrigger.Logout)) return;
            }
            finally
            {
                _closeFlowRunning = false;
            }

            // البوابة عدّت — النافذة بتتقفل جوه SignOutAndRestartSession،
            // ومعالج Closing مالوش داعي يسأل تاني
            _closeConfirmed = true;

            App.SignOutAndRestartSession();
        }

        /// <summary>
        /// مين طلب التوقيع. بيغيّر رسالة الرفض بس — المنطق واحد للتلاتة
        /// (شوف <see cref="RunFinalSaveFlowAsync"/>).
        /// </summary>
        private enum SignOffTrigger
        {
            /// <summary>زرار "حفظ نهائي" — المستخدم طالب الفلو بنفسه</summary>
            Button,

            /// <summary>محاولة قفل البرنامج</summary>
            WindowClose,

            /// <summary>تسجيل خروج — بيسيب الجهاز لحساب تاني</summary>
            Logout
        }

        private async void FinalSave_Click(object sender, RoutedEventArgs e) =>
            await RunFinalSaveFlowAsync();

        /// <summary>
        /// اتحقّقت اليوم فعلًا واتقفل من غير سؤال — <see cref="MainWindow_Closing"/>
        /// بينادي Close() تاني بعد نجاح التوقيع، وده بيرجّعه هنا؛ من غير
        /// العلم ده كان هيدخل في حلقة (Closing → RunFinalSaveFlowAsync
        /// → Close → Closing تاني).
        /// </summary>
        private bool _closeConfirmed;

        /// <summary>يمنع تراكم أكتر من محاولة توقيع لو المستخدم دبس X كذا مرة بسرعة</summary>
        private bool _closeFlowRunning;

        /// <summary>
        /// مايسمحش بإغلاق البرنامج قبل ما اليوم يتوقّع — نفس فلو زرار
        /// "حفظ نهائي" بالظبط، غير إنه بيتنادى تلقائي عند محاولة الإغلاق.
        ///
        /// **لازم تفضل sync بالكامل، مفيش await هنا خالص.** WPF بيسيب
        /// الـ Window في حالة "بيتقفل" داخليًا (`_isClosing`) لحد ما
        /// المعالج يرجع تمامًا، حتى لو `e.Cancel = true` اتحطت قبل كده —
        /// أي `await` جوّه المعالج ده (زي ما كان هنا قبل الإصلاح ده)
        /// بيرجّع التحكم لـ WPF قبل ما الحالة دي تتصفّر، فأي محاولة تفتح
        /// ديالوج بعدها (حتى لو الحدث نفسه أكّد الإلغاء) بترمي
        /// "Cannot ... call ... ShowDialog ... while a Window is closing" —
        /// وده بالظبط اللي كان بيخلي البرنامج يقفل من غير ما يطلب التوقيع.
        /// الحل: نلغي فورًا وبشكل متزامن، ونأجّل الفلو الحقيقي (اللي فيه
        /// async وديالوجات) لدورة Dispatcher تالية بـ BeginInvoke، بعد ما
        /// WPF يخلّص إلغاء الإغلاق ده تمامًا ويصفّر حالته.
        /// </summary>
        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (_closeConfirmed) return; // إغلاق حقيقي بعد توقيع ناجح — سيبه يكمل

            // **بنلغي دايمًا الأول**: السؤال "اليوم مغطّى بتوقيع؟" محتاج
            // قراءة من قاعدة البيانات (فيه نشاط بعد آخر توقيع؟)، وده async —
            // وممنوع نعمل await هنا. فبنلغي، وبنسأل في دورة Dispatcher
            // تالية؛ لو طلع مغطّى فعلاً بنقفل فورًا من غير ما يحس المستخدم.
            e.Cancel = true;
            if (_closeFlowRunning) return; // فلو شغال بالفعل من محاولة إغلاق سابقة

            _closeFlowRunning = true;

            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    if (await RunFinalSaveFlowAsync(SignOffTrigger.WindowClose))
                    {
                        _closeConfirmed = true;
                        Close();
                    }
                }
                catch (Exception ex)
                {
                    // من غير الـ catch ده الاستثناء بيروح لمعالج
                    // DispatcherUnhandledException العام وبيتبلع كتوست
                    // عام مالوش علاقة بالتوقيع — والمستخدم مايعرفش إن
                    // فلو الإغلاق نفسه هو اللي وقع
                    Notify.Error($"حصل خطأ أثناء الحفظ النهائي:\n\n{ex.Message}", "خطأ");
                }
                finally
                {
                    _closeFlowRunning = false;
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// فلو "حفظ نهائي" الكامل: باسورد → مراجعة كل حاجة حصلت النهارده
        /// → توقيع. **بوابة واحدة بتتنادى من تلات أماكن** (الزرار، محاولة
        /// الإغلاق، تسجيل الخروج) عشان التلاتة يمشوا بنفس المسار بالظبط
        /// — نسخة تانية من نفس الفحص كانت هتفترق عن دي أول تعديل.
        ///
        /// اللي بيفرق حسب المشغّل هو **رسالة الرفض بس**: المستخدم لازم
        /// يفهم ليه الحاجة اللي طلبها ما حصلتش، مش يشوف ديالوج ظهر فجأة.
        /// </summary>
        /// <returns>النهارده بقى مغطّى بتوقيع (سواء دلوقتي أو من قبل)؟</returns>
        private async Task<bool> RunFinalSaveFlowAsync(SignOffTrigger trigger = SignOffTrigger.Button)
        {
            var today = DateTime.Today;

            List<ActivityEvent> pending;
            using (var checkScope = App.AppHost.Services.CreateScope())
            {
                var signOff = checkScope.ServiceProvider.GetRequiredService<DailyOperationsSignOffService>();

                // "مغطّى" = فيه توقيع ومفيش أي شغل بعده. لو المستخدم وقّع
                // الساعة 6 وحذف إنتاج الساعة 8، اليوم بيرجع محتاج توقيع
                if (await signOff.IsFullySignedOffAsync(today))
                    return true; // مفيش حاجة لسه محتاجة إمضاء — اقفل عادي

                pending = (await signOff.GetActivitySinceLastSignOffAsync(today)).ToList();
            }

            // الزرار مالوش رسالة: المستخدم هو اللي طلب الفلو، فالديالوج
            // اللي جاي هو الرد. الاتنين التانيين لازم يتقالهم ليه الحاجة
            // اللي طلبوها اتوقفت
            if (trigger is SignOffTrigger.WindowClose)
                Notify.Warn(
                    "فيه شغل النهارده لسه ما اتوقّعش عليه. لازم \"حفظ نهائي\" الأول قبل ما تقفل البرنامج.",
                    "مش هينفع تقفل");
            else if (trigger is SignOffTrigger.Logout)
                Notify.Warn(
                    "فيه شغل النهارده لسه ما اتوقّعش عليه. لازم \"حفظ نهائي\" الأول قبل ما تسجّل خروج.",
                    "مش هينفع تسجّل خروج");

            using var gateScope = App.AppHost.Services.CreateScope();
            var gate = gateScope.ServiceProvider.GetRequiredService<OperationsPasswordService>();

            var input = SensitiveActionDialog.Ask(
                this, "حفظ نهائي",
                "توقيع نهاية اليوم — بيغطي كل حاجة حصلت في البرنامج النهارده بدل ما تتأكّد من كل عملية لوحدها.",
                SensitiveActionKind.Save, await gate.IsConfiguredAsync(), reasonRequired: false);

            if (input is null) return false;

            // الملخص بيعرض اللي لسه محتاج توقيع بس — عرض عمليات موقّعة
            // خلاص كان هيخلي المستخدم يمضي على نفس الحاجة مرتين
            var summary = new DailySignOffSummaryDialog(today, pending) { Owner = this };
            if (summary.ShowDialog() != true) return false;

            try
            {
                using var signScope = App.AppHost.Services.CreateScope();
                await signScope.ServiceProvider.GetRequiredService<DailyOperationsSignOffService>()
                    .SignOffAsync(today, input.Password);

                Notify.Info($"اتوقّع يوم {today:yyyy/MM/dd} بنجاح.", "تم الحفظ النهائي");
                return true;
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "مش هينفع");
                return false;
            }
        }
    }
}
