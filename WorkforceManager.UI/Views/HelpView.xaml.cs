using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>شاشة "الدليل" — مرجع دائم لكل شاشات البرنامج، شوف HelpViewModel</summary>
    public partial class HelpView : UserControl
    {
        public HelpView(HelpViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // مرة واحدة بس لما الشاشة تفتح — مش كل مرة SelectedTopic يتغيّر
            Loaded += (_, _) => AnimateTilesIn();
        }

        /// <summary>
        /// حركة دخول متدرّجة للشبكة: كل تايل بيبدأ شفاف ومزاح شوية لتحت
        /// (شوف Opacity/RenderTransform في HelpView.xaml)، وبيتحرّك لحالته
        /// الطبيعية بـStoryboard حقيقي — مش Task.Delay بيغيّر خصائص يدوي كل
        /// شوية، ده اللي القاعدة في المشروع بتمنعه صراحة. الفرق بين تايل
        /// والتاني هو BeginTime بس (مبني على ترتيب التايلات في الشبكة، اللي
        /// أصلًا نفس ترتيب القايمة الجانبية) — بناء الـTimeline نفسه في الكود
        /// عادي جدًا في WPF لأن BeginTime لكل عنصر لوحده صعب تربطه إعلانيًا
        /// (Binding) جوّه Style.Triggers، مش لأننا بنحرّك الخصائص يدوي.
        /// </summary>
        private void AnimateTilesIn()
        {
            TopicsGrid.UpdateLayout();

            for (var i = 0; i < TopicsGrid.Items.Count; i++)
            {
                if (TopicsGrid.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter presenter)
                    continue;

                presenter.ApplyTemplate();
                if (VisualTreeHelper.GetChild(presenter, 0) is not FrameworkElement tile) continue;

                var beginTime = TimeSpan.FromMilliseconds(i * 60);
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

                var fadeIn = new DoubleAnimation
                {
                    From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(280),
                    BeginTime = beginTime, EasingFunction = ease
                };
                Storyboard.SetTarget(fadeIn, tile);
                Storyboard.SetTargetProperty(fadeIn, new PropertyPath(UIElement.OpacityProperty));

                var slideUp = new DoubleAnimation
                {
                    From = 14, To = 0, Duration = TimeSpan.FromMilliseconds(280),
                    BeginTime = beginTime, EasingFunction = ease
                };
                Storyboard.SetTarget(slideUp, tile);
                Storyboard.SetTargetProperty(slideUp,
                    new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

                var storyboard = new Storyboard();
                storyboard.Children.Add(fadeIn);
                storyboard.Children.Add(slideUp);
                storyboard.Begin();
            }
        }
    }
}
