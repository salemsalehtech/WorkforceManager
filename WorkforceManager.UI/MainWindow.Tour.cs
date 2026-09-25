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
    // ======================= جولة "إيه الجديد" (Tour.AppTourContent) =======================
    // فصل عن MainWindow.xaml.cs الأساسي — محرك السبوت لايت المشترك بين
    // الجولة الإرشادية (RunTourAsync) والتدريب التوجيهي التفاعلي
    // (MainWindow.Sandbox.cs's RunGuidedPracticeAsync/RunGuidedStepAsync،
    // اللي بيستخدموا TourOverlay/PositionTourStep/_tourStepTcs هنا بالظبط).
    public partial class MainWindow
    {
        private enum TourAction { Next, Previous, Skip }

        private TaskCompletionSource<TourAction>? _tourStepTcs;

        /// <summary>
        /// بيشغّل الجولة خطوة خطوة: تنقّل للشاشة الصح لو الخطوة محتاجاها
        /// (وتبويب "تسجيل الإنتاج اليومي" الصح لو محدّد)، استنى التخطيط
        /// يستقر، لوّن سبوت لايت على العنصر المستهدف، واستنى "التالي"/"السابق"/
        /// "تخطي الكل" (أو Escape، أو دوسة على المنطقة المعتمة — نفس تأثير
        /// "تخطي الكل"). بينادى من App.OfferAppTourIfNewAsync وHelpViewModel.
        ///
        /// عنصر مش موجود دلوقتي (نادر — بس ممكن لو حد غيّر XAML بعدين
        /// ونسي يحدّث المحتوى)، أو موجود بس مخفي/بلا مساحة (عناصر بتظهر
        /// بشرط، زي زرار "إضافة حساب" اللي بيختفي لغير مدير القسم) —
        /// بيتخطّى بنفس اتجاه الحركة الحالي (تقدّم أو رجوع)، مش بيوقف
        /// الجولة كلها.
        /// </summary>
        public async Task RunTourAsync(IReadOnlyList<Tour.AppTourStep> steps)
        {
            TourOverlay.Visibility = Visibility.Visible;

            try
            {
                var i = 0;
                var delta = 1; // اتجاه الحركة الحالي — بيتغيّر لـ-1 لو المستخدم دوس "السابق"

                while (i >= 0 && i < steps.Count)
                {
                    var step = steps[i];

                    NavigateToTourScreen(step.Screen);

                    if (step.TabIndex is int tab)
                        _session.GetRequiredService<ViewModels.DailyEntryViewModel>().SelectedTabIndex = tab;

                    // (MainContent.Content as ...)?.DataContext مش _session.GetRequiredService
                    // عن قصد: WorkersView/WorkersViewModel مسجّلين Transient، فـ
                    // GetRequiredService كان بيبني نسخة تانية يتيمة غير اللي فعليًا
                    // ظاهرة على الشاشة (اللي NavigateToTourScreen فوق بناها) — العامل
                    // كان بيتحدد على نسخة محدش شايفها، والخطوة كانت بتتخطّى بصمت
                    if (step.SelectFirstWorker &&
                        (MainContent.Content as FrameworkElement)?.DataContext is ViewModels.WorkersViewModel workersVm)
                        workersVm.SelectedWorker = workersVm.Workers.FirstOrDefault();

                    // تحديد عامل بيحمّل بروفايله (SelectedWorker/OnSelectedWorkerChanged) async
                    // في الخلفية، فمحتاج وقت أطول من مجرد استقرار تخطيط الشاشة.
                    // الحالة العادية لازم تعدّي دخول الشاشة (EntranceAnimation) كله،
                    // وإلا مكان الإضاءة بيتقاس على عنصر لسه بينزلق
                    await Task.Delay(step.SelectFirstWorker ? 400 : EntranceAnimation.DurationMs + 70);

                    if (await FindTourTargetAsync(step.TargetElementName) is not { } target ||
                        target.Visibility != Visibility.Visible || target.ActualWidth <= 0 || target.ActualHeight <= 0)
                    {
                        i += delta; // موجود جوه الشجرة بس مخفي فعليًا دلوقتي، أو مش موجود أصلًا
                        continue;
                    }

                    PositionTourStep(target, i + 1, steps.Count, step.Title, step.Description);
                    TourBackButton.IsEnabled = i > 0;

                    _tourStepTcs = new TaskCompletionSource<TourAction>();
                    var action = await _tourStepTcs.Task;

                    if (action == TourAction.Skip) return;

                    delta = action == TourAction.Previous ? -1 : 1;
                    i += delta;
                }
            }
            finally
            {
                TourOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void NavigateToTourScreen(Tour.TourScreen screen)
        {
            switch (screen)
            {
                case Tour.TourScreen.Workers: NavWorkersItem.IsChecked = true; break;
                case Tour.TourScreen.Products: NavProductsItem.IsChecked = true; break;
                case Tour.TourScreen.DailyEntry: NavDailyEntryItem.IsChecked = true; break;
                case Tour.TourScreen.Memory: NavMemoryItem.IsChecked = true; break;
                case Tour.TourScreen.Evaluation: NavEvaluationItem.IsChecked = true; break;
                case Tour.TourScreen.Reports: NavReportsItem.IsChecked = true; break;
                case Tour.TourScreen.ActivityLog: NavActivityLogItem.IsChecked = true; break;
                case Tour.TourScreen.Settings: NavSettingsItem.IsChecked = true; break;
                case Tour.TourScreen.DepartmentAccounts: NavDepartmentAccountsItem.IsChecked = true; break;
                case Tour.TourScreen.None: break;
            }
        }

        /// <summary>بيدوّر على عنصر بالاسم: الشريط الجانبي الأول (ثابت دايمًا)، وبعدين الشاشة المفتوحة حاليًا</summary>
        private FrameworkElement? FindTourTarget(string name)
        {
            if (FindName(name) is FrameworkElement sidebarElement) return sidebarElement;
            return (MainContent?.Content as FrameworkElement)?.FindName(name) as FrameworkElement;
        }

        /// <summary>
        /// نفس FindTourTarget، بس لو الهدف جوّه الشريط الجانبي (SidebarContent)
        /// والشريط مطوي، بتفتحه الأول وتستنى الحركة تخلص — وإلا الخطوة كانت
        /// هتتخطى بصمت (FindTourTarget's caller بيشيل أي هدف Visibility != Visible)
        /// زي أي هدف مش موجود أصلًا، والجولة تفضل ناقصة من غير أي تفسير.
        /// </summary>
        private async Task<FrameworkElement?> FindTourTargetAsync(string name)
        {
            var target = FindTourTarget(name);

            if (target is not null && _isSidebarCollapsed && SidebarContent.IsAncestorOf(target))
            {
                AnimateSidebarCollapse(false);
                await Task.Delay(SidebarToggleAnimationMs + 50);
            }

            return target;
        }

        /// <summary>
        /// بيحسب مكان العنصر بالنسبة لـ TourOverlay (إحداثيات فعلية —
        /// شوف كومنت FlowDirection على TourOverlay في XAML)، ويبني ثقب
        /// السبوت لايت والفقاعة حواليه.
        /// </summary>
        private void PositionTourStep(
            FrameworkElement target, int stepNumber, int totalSteps, string title, string description)
        {
            var bounds = target.TransformToVisual(TourOverlay)
                .TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight));

            const double pad = 8;
            var hole = new Rect(bounds.X - pad, bounds.Y - pad, bounds.Width + pad * 2, bounds.Height + pad * 2);

            var outer = new RectangleGeometry(new Rect(0, 0, TourOverlay.ActualWidth, TourOverlay.ActualHeight));
            var inner = new RectangleGeometry(hole, 8, 8);
            TourDimPath.Data = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);

            TourHighlight.Width = hole.Width;
            TourHighlight.Height = hole.Height;
            TourHighlight.Margin = new Thickness(hole.X, hole.Y, 0, 0);

            TourStepCounter.Text = $"{stepNumber} من {totalSteps}";
            TourTitle.Text = title;
            TourDescription.Text = description;

            // الفقاعة تحت العنصر لو فيه مساحة، وإلا فوقه — عشان متطلعش برّه الشاشة
            const double calloutWidth = 340, calloutHeight = 210;
            var calloutTop = hole.Bottom + 12;
            if (calloutTop + calloutHeight > TourOverlay.ActualHeight)
                calloutTop = Math.Max(0, hole.Top - calloutHeight - 12);

            var calloutLeft = Math.Clamp(bounds.X, 12, Math.Max(12, TourOverlay.ActualWidth - calloutWidth - 12));
            TourCallout.Margin = new Thickness(calloutLeft, calloutTop, 0, 0);
        }

        private void TourNext_Click(object sender, RoutedEventArgs e) => _tourStepTcs?.TrySetResult(TourAction.Next);

        private void TourBack_Click(object sender, RoutedEventArgs e) => _tourStepTcs?.TrySetResult(TourAction.Previous);

        private void TourSkip_Click(object sender, RoutedEventArgs e) => _tourStepTcs?.TrySetResult(TourAction.Skip);

        /// <summary>دوسة على المنطقة المعتمة برّه الفقاعة = زي "تخطي الكل" — نفس تعامل أي Overlay بيتقفل بدوسة برّه</summary>
        private void TourDim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
            _tourStepTcs?.TrySetResult(TourAction.Skip);
    }
}
