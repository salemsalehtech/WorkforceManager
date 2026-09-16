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
    // ======================= وضع التجربة (Sandbox) + التدريب التوجيهي =======================
    // فصل عن MainWindow.xaml.cs الأساسي — جلسة تجربة معزولة (Sandbox\SandboxSession)
    // ومحرك التدريب التفاعلي (RunGuidedPracticeAsync) اللي بيستخدم فوقها.
    // بيستخدم TourOverlay/PositionTourStep/_tourStepTcs/TourAction من
    // MainWindow.Tour.cs — نفس واجهة الجولة، مفيش تكرار لها هنا.
    public partial class MainWindow
    {
        private Sandbox.SandboxSession? _sandbox;
        private object? _realContentBeforeSandbox;
        private bool _sandboxActive;

        /// <summary>
        /// بيفتح جلسة تجربة معزولة (Sandbox\SandboxSession) ويستبدل محتوى
        /// الشاشة الحقيقي بنسخة من نفس الشاشة متحلّة من مزوّد التجربة —
        /// FindTourTarget بيدوّر على MainContent.Content زي ما هو دايمًا،
        /// فمحرك السبوت لايت مش محتاج يعرف إنه في تجربة أصلًا.
        /// </summary>
        private async Task EnterSandboxModeAsync(Tour.TourScreen screen)
        {
            if (_sandbox is not null) return;

            _sandbox = await Sandbox.SandboxSession.CreateAsync();
            _realContentBeforeSandbox = MainContent.Content; // عادة شاشة "الدليل" نفسها، هنرجعلها بعد التجربة

            NavigateToTourScreen(screen); // بيفتح الشاشة الحقيقية مؤقتًا (Checked handler عادي، هنستبدلها فورًا)
            var view = ResolveSandboxView(screen);
            MainContent.Content = view;

            // الشاشة بتحمّل بياناتها لوحدها في حدث Loaded (شوف مثلًا
            // WorkersView.xaml.cs: "Loaded += async (_, _) => await
            // viewModel.LoadAsync();"). كنا بننادي LoadAsync() تاني هنا
            // صراحةً فوق النداء ده — نداءين بيتسابقوا على نفس القايمة،
            // والتاني لو خلص بعد ما SelectFirstWorker تحت حدّد عامل، بيعمل
            // Workers.Clear() فبيصفّر التحديد من تحت الجولة (شوف الكومنت
            // على OnSelectedWorkerChanged في WorkersViewModel.cs). الحل:
            // نستنى نفس النداء الوحيد ده يخلص (Loaded يضمن إنه ابتدى،
            // IsLoading يضمن إنه خلص) بدل ما نعمل نداء تاني يتسابق معاه.
            if (view is FrameworkElement fe)
            {
                var loadedTcs = new TaskCompletionSource();
                fe.Loaded += (_, _) => loadedTcs.TrySetResult();
                await loadedTcs.Task;

                if (fe.DataContext is ViewModels.WorkersViewModel workersVm)
                    while (workersVm.IsLoading) await Task.Delay(30);
            }

            _sandboxActive = true;
            SandboxBanner.Visibility = Visibility.Visible;
        }

        private void ExitSandboxMode()
        {
            if (_sandbox is null) return;

            MainContent.Content = _realContentBeforeSandbox;
            _sandboxActive = false;
            SandboxBanner.Visibility = Visibility.Collapsed;

            _sandbox.Dispose();
            _sandbox = null;
            _realContentBeforeSandbox = null;
        }

        /// <summary>
        /// بينادى من أول سطر في كل NavXxx_Checked. دوسة على أي عنصر تنقّل
        /// وانت في وضع تجربة = خروج من التجربة (زي "تخطي الكل" بالظبط) —
        /// الدوسة التانية (لما الوضع يبقى مقفول) هي اللي فعليًا بتنقّل.
        /// أبسط وأأمن من محاولة ننقّل ونطلّع من التجربة في نفس اللحظة.
        /// </summary>
        /// <returns>true لو الهاندلر لازم يوقف هنا من غير ما ينقّل فعليًا</returns>
        private bool ExitSandboxOnRealNavigation()
        {
            if (!_sandboxActive) return false;
            _tourStepTcs?.TrySetResult(TourAction.Skip);
            return true;
        }

        private object ResolveSandboxView(Tour.TourScreen screen) => screen switch
        {
            Tour.TourScreen.Workers => _sandbox!.Services.GetRequiredService<WorkersView>(),
            _ => throw new ArgumentOutOfRangeException(nameof(screen), screen, "مفيش شاشة تجربة لسه للشاشة دي")
        };

        private void SandboxExit_Click(object sender, RoutedEventArgs e) =>
            _tourStepTcs?.TrySetResult(TourAction.Skip);

        /// <summary>
        /// محرك مواز لـRunTourAsync لفلو تدريب تفاعلي كامل: يفتح وضع تجربة
        /// (EnterSandboxModeAsync)، يشغّل خطواته واحدة واحدة
        /// (RunGuidedStepAsync)، وبيقفل وضع التجربة أيًا كانت نتيجة الفلو
        /// (خلص عادي، أو المستخدم دوس "تخطي"/Escape/عنصر تنقّل تاني).
        /// بيستخدم نفس TourOverlay/PositionTourStep/_tourStepTcs وأزرار
        /// التالي-السابق-تخطي الموجودين بالظبط — مفيش تكرار لواجهة الجولة.
        /// </summary>
        public async Task RunGuidedPracticeAsync(Tour.GuidedPracticeFlow flow)
        {
            // [RelayCommand] بتاع HelpViewModel بيستدعي الدالة دي من غير حد
            // بينتظرها (زرار WPF)، فأي استثناء هنا كان هيضيع بصمت من غير
            // أي رسالة أو تصرّف مرئي — نفس المشكلة اللي SafeAsync.Run
            // (ViewModels\SafeAsync.cs) موجودة أصلًا عشانها في الشاشات
            // التانية. try/catch هنا يضمن إن أي خطأ يبان للمستخدم، وإن
            // وضع التجربة يتقفل صح حتى لو الفتح نفسه فشل في نص الطريق.
            try
            {
                await EnterSandboxModeAsync(flow.Screen);
                try
                {
                    if (flow.SelectFirstWorker &&
                        (MainContent.Content as FrameworkElement)?.DataContext is ViewModels.WorkersViewModel workersVm)
                        workersVm.SelectedWorker = workersVm.Workers.FirstOrDefault();

                    await Task.Delay(flow.SelectFirstWorker ? 400 : 150);

                    TourOverlay.Visibility = Visibility.Visible;
                    var i = 0;
                    while (i >= 0 && i < flow.Steps.Count)
                    {
                        var action = await RunGuidedStepAsync(flow.Steps[i], i + 1, flow.Steps.Count);
                        if (action == TourAction.Skip) break;
                        i += action == TourAction.Previous ? -1 : 1;
                    }
                }
                finally
                {
                    TourOverlay.Visibility = Visibility.Collapsed;
                    ExitSandboxMode();
                }
            }
            catch (Exception ex)
            {
                ExitSandboxMode(); // ضمان إضافي لو الفشل حصل جوّه EnterSandboxModeAsync نفسها
                Notify.Error($"مقدرناش نفتح وضع التجربة:\n\n{ex.Message}");
            }
        }

        /// <summary>
        /// خطوة واحدة: بتلوّن السبوت لايت وتستنى إن step.IsComplete يرجّع
        /// true — مش دوسة "التالي" (اللي فاضل شغال كطريق طوارئ لو الاكتشاف
        /// فشل لأي سبب، أحسن من مستخدم واقف عالق).
        /// </summary>
        private async Task<TourAction> RunGuidedStepAsync(Tour.GuidedPracticeStep step, int stepNumber, int totalSteps)
        {
            var vm = (MainContent.Content as FrameworkElement)?.DataContext;
            if (vm is null || await FindTourTargetAsync(step.TargetElementName) is not { } target ||
                target.Visibility != Visibility.Visible || target.ActualWidth <= 0 || target.ActualHeight <= 0)
                return TourAction.Next; // هدف مش موجود = تعدّى، زي RunTourAsync بالظبط

            PositionTourStep(target, stepNumber, totalSteps, step.Title, step.Description);
            TourGuidedHint.Visibility = Visibility.Visible;
            _tourStepTcs = new TaskCompletionSource<TourAction>();

            void Handler(object? _, EventArgs __)
            {
                if (step.IsComplete(vm)) _tourStepTcs?.TrySetResult(TourAction.Next);
            }

            var watched = new List<object> { vm };
            if (step.WatchSelectors is not null) watched.AddRange(step.WatchSelectors(vm));

            foreach (var o in watched)
            {
                if (o is INotifyPropertyChanged p) p.PropertyChanged += Handler;
                if (o is INotifyCollectionChanged c) c.CollectionChanged += Handler;
            }

            if (step.IsComplete(vm)) _tourStepTcs.TrySetResult(TourAction.Next); // اتحقّقت أصلًا قبل ما نستنى

            try
            {
                var action = await _tourStepTcs.Task;
                if (action == TourAction.Next) await Task.Delay(300); // ومضة تأكيد قبل ما نكمّل
                return action;
            }
            finally
            {
                foreach (var o in watched)
                {
                    if (o is INotifyPropertyChanged p) p.PropertyChanged -= Handler;
                    if (o is INotifyCollectionChanged c) c.CollectionChanged -= Handler;
                }

                TourGuidedHint.Visibility = Visibility.Collapsed;
            }
        }
    }
}
