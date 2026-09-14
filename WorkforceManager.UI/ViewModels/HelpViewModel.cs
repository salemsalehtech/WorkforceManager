using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkforceManager.UI.Tour;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// عقل شاشة "الدليل": مرجع دائم لكل شاشات البرنامج، عكس جولة "إيه
    /// الجديد" اللي بتظهر مرة واحدة بس. المحتوى ثابت (<see cref="HelpTopics.All"/>)
    /// فمفيش تحميل من قاعدة بيانات هنا خالص.
    /// </summary>
    public partial class HelpViewModel : ObservableObject
    {
        public IReadOnlyList<HelpTopic> Topics => HelpTopics.All;

        /// <summary>
        /// بيشغّل جولة سبوت لايت قصيرة لموضوع واحد — نفس محرك جولة "إيه
        /// الجديد" بالظبط (MainWindow.RunTourAsync)، من غير ما نحتاج DI
        /// لمرجع النافذة (نفس نمط Application.Current.MainWindow المستخدم
        /// أصلاً كـOwner لديالوجات في DailyEntryViewModel وغيرها).
        /// </summary>
        [RelayCommand]
        private async Task TryTourAsync(HelpTopic? topic)
        {
            if (topic is null) return;
            if (Application.Current.MainWindow is MainWindow main)
                await main.RunTourAsync(topic.TourSteps);
        }
    }
}
