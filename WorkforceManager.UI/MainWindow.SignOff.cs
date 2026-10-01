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
            DailySignOffChecklist checklist;
            using (var checkScope = App.AppHost.Services.CreateScope())
            {
                var signOff = checkScope.ServiceProvider.GetRequiredService<DailyOperationsSignOffService>();

                // "مغطّى" = فيه توقيع ومفيش أي شغل بعده. لو المستخدم وقّع
                // الساعة 6 وحذف إنتاج الساعة 8، اليوم بيرجع محتاج توقيع
                if (await signOff.IsFullySignedOffAsync(today))
                    return true; // مفيش حاجة لسه محتاجة إمضاء — اقفل عادي

                pending = (await signOff.GetActivitySinceLastSignOffAsync(today)).ToList();

                // اكتمال بيانات النهارده (منتج بلا إنتاج، عامل بلا حضور، خطة
                // ذاكرة مستحقة بلا إنتاج) — محور مختلف تمامًا عن تغطية سجل
                // العمليات فوق، إفادة بس مش بوابة حجب، شوف DailySignOffChecklistService
                checklist = await checkScope.ServiceProvider.GetRequiredService<DailySignOffChecklistService>()
                    .BuildAsync(today);
            }

            bool passwordRequired;
            using (var gateScope = App.AppHost.Services.CreateScope())
                passwordRequired = await gateScope.ServiceProvider.GetRequiredService<OperationsPasswordService>().IsConfiguredAsync();

            // **خطوة واحدة**: الملخص الأول وكلمة السر في آخره (كانت نافذة باسورد
            // قبل الملخص — المستخدم كان بيكتب السر قبل ما يشوف بيوقّع على إيه).
            // الملخص بيعرض اللي لسه محتاج توقيع بس — عرض عمليات موقّعة خلاص كان
            // هيخلي المستخدم يمضي على نفس الحاجة مرتين.
            var (reason, confirmLabel, deferLabel) = trigger switch
            {
                SignOffTrigger.WindowClose => ("فيه شغل النهارده لسه ما اتوقّعش — وقّعه دلوقتي أو أجّله لبكره.",
                    "وقّع واقفل البرنامج", "اقفل وأوقّع بكره"),
                SignOffTrigger.Logout => ("فيه شغل النهارده لسه ما اتوقّعش — وقّعه دلوقتي أو أجّله لبكره.",
                    "وقّع وسجّل خروج", "اخرج وأوقّع بكره"),
                _ => ((string?)null, "وقّع اليوم", (string?)null)
            };

            var summary = new DailySignOffSummaryDialog(
                today, pending, checklist, passwordRequired,
                trySignAsync: password => TrySignOffAsync(today, password),
                markPresentAsync: checklist.WorkerIdsWithNoAttendance.Count > 0
                    ? () => MarkRemainingPresentAsync(today, checklist.WorkerIdsWithNoAttendance)
                    : null,
                closingReason: reason, confirmLabel: confirmLabel, deferLabel: deferLabel)
            { Owner = this };
            summary.ShowDialog();

            switch (summary.Outcome)
            {
                case DailySignOffOutcome.Signed:
                    // نسخة إكسل من الخطة الشهرية لحد النهارده في فولدر التقارير — جاهزة تتبعت للمدير
                    var reportPath = await ViewModels.MonthlyPlanExport.WriteDailyAutoReportAsync(
                        App.AppHost.Services.GetRequiredService<IServiceScopeFactory>(), today);
                    Notify.Info(
                        reportPath is null
                            ? $"اتوقّع يوم {today:yyyy/MM/dd} ✓ — {pending.Count} عملية."
                            : $"اتوقّع يوم {today:yyyy/MM/dd} ✓ — {pending.Count} عملية.\nتقرير الخطة اتحفظ في: {reportPath}",
                        "تم الحفظ النهائي");
                    return true;

                case DailySignOffOutcome.Deferred:
                    // ديالوج اللحاق وقت التشغيل الجاي (LateSignOffCatchUpDialog) بيطلب الأيام الفايتة اللي ما اتوقّعتش
                    Notify.Info("اليوم ده هيتطلب توقيعه أول ما تفتح البرنامج المرة الجاية.", "اتأجّل التوقيع");
                    return true;

                case DailySignOffOutcome.OpenDailyEntry:
                    NavDailyEntryItem.IsChecked = true;
                    return false;

                case DailySignOffOutcome.OpenMemory:
                    NavMemoryItem.IsChecked = true;
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>التوقيع الفعلي — بيرجع رسالة الخطأ (كلمة سر غلط، قفل بعد محاولات...) أو null</summary>
        private static async Task<string?> TrySignOffAsync(DateTime day, string password)
        {
            try
            {
                using var scope = App.AppHost.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<DailyOperationsSignOffService>().SignOffAsync(day, password);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>"سجّل الباقيين حاضرين" — نفس مسار حفظ الحضور العادي (Upsert جماعي + مصالحة الجزاءات)</summary>
        private static async Task<string?> MarkRemainingPresentAsync(DateTime day, IReadOnlyList<int> workerIds)
        {
            try
            {
                using var scope = App.AppHost.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<AttendanceService>()
                    .RecordAttendanceBatchAsync(day, workerIds.Select(id => (id, AttendanceStatus.Present)));
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
