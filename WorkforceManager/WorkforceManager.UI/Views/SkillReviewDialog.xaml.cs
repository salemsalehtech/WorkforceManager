using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// المراجعة الشهرية للتقييمات: النظام بيعرض العمال اللي أداؤهم الفعلي
    /// بقى مختلف عن تقييم المدير، والمدير بيوافق أو يتجاهل.
    ///
    /// **اقتراحات مش قرارات** — النافذة دي مبتغيّرش أي تقييم لوحدها.
    /// المدير شايف حاجات الأرقام مبتقولهاش (ماكينة بايظة، عامل جديد
    /// بيتعلّم)، فالكلمة الأخيرة له.
    ///
    /// اللي بيتوافق عليه بيتشال من القايمة فورًا عشان يبان اللي فاضل.
    /// </summary>
    public partial class SkillReviewDialog : Window
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ObservableCollection<SkillSuggestionDto> _pending = new();

        private int _appliedCount;

        private SkillReviewDialog(IServiceScopeFactory scopeFactory, SkillReviewDto review)
        {
            InitializeComponent();
            _scopeFactory = scopeFactory;

            SummaryText.Text = review.SummaryText;

            foreach (var suggestion in review.Suggestions) _pending.Add(suggestion);
            SuggestionsList.ItemsSource = _pending;

            RefreshEmptyState();
        }

        /// <summary>بيبني المراجعة ويعرضها. بيرجّع عدد التعديلات اللي المدير وافق عليها.</summary>
        public static int Show(Window? owner, IServiceScopeFactory scopeFactory, SkillReviewDto review)
        {
            var dialog = new SkillReviewDialog(scopeFactory, review);
            if (owner is not null) dialog.Owner = owner;

            dialog.ShowDialog();
            return dialog._appliedCount;
        }

        private async void ApplyOne_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not SkillSuggestionDto suggestion) return;
            await ApplyAsync(suggestion);
        }

        private void Ignore_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not SkillSuggestionDto suggestion) return;

            // التجاهل بيشيله من القايمة بس — التقييم بيفضل زي ما هو،
            // والاقتراح هيرجع الشهر الجاي لو الفرق لسه موجود
            _pending.Remove(suggestion);
            RefreshEmptyState();
        }

        private async void ApplyAll_Click(object sender, RoutedEventArgs e)
        {
            ApplyAllButton.IsEnabled = false;
            try
            {
                foreach (var suggestion in _pending.ToList())
                    await ApplyAsync(suggestion);
            }
            finally
            {
                ApplyAllButton.IsEnabled = true;
            }
        }

        private async Task ApplyAsync(SkillSuggestionDto suggestion)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<SkillRatingService>()
                    .ApplySuggestionAsync(suggestion);

                _pending.Remove(suggestion);
                _appliedCount++;
                RefreshEmptyState();
            }
            catch (InvalidOperationException ex)
            {
                Notify.Warn(ex.Message, "مش هينفع");
            }
        }

        private void RefreshEmptyState()
        {
            var empty = _pending.Count == 0;

            EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            ListScroller.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            ApplyAllButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

            // "اتحفظ" مش "اتعدّل": السطر ممكن يكون تأكيد لتقييم مطابق
            // أصلاً، ووصفه بإنه اتعدّل بيخلي المدير يدوّر على تغيير محصلش
            AppliedText.Text = _appliedCount == 0 ? "" : $"اتحفظ {_appliedCount} تقييم";
        }

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}
