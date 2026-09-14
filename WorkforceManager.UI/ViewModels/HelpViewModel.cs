using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkforceManager.UI.Tour;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// عقل شاشة "الدليل": مرجع دائم لكل شاشات البرنامج، عكس جولة "إيه
    /// الجديد" اللي بتظهر مرة واحدة بس. المحتوى ثابت (<see cref="HelpTopics"/>)
    /// فمفيش تحميل من قاعدة بيانات هنا خالص.
    ///
    /// قايمتين منفصلتين مش قايمة واحدة عشان الشاشة تقدر تحط عنوان قسم
    /// فوق كروت "تسجيل الإنتاج اليومي" السبعة، فيبان إنهم أجزاء من نفس
    /// الشاشة مش مواضيع مستقلة.
    /// </summary>
    public partial class HelpViewModel : ObservableObject
    {
        public IReadOnlyList<HelpTopic> MainTopics => HelpTopics.MainTopics;
        public IReadOnlyList<HelpTopic> DailyEntryTopics => HelpTopics.DailyEntryTopics;

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
