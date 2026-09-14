using System.Linq;
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
    ///
    /// كل كارت أكورديون: دوسة على هيدره بتفتحله قايمة ميزاته الفردية
    /// (<see cref="ToggleTopic"/>)، والمستخدم يختار هو عايز يجرّب أنهي
    /// ميزة (<see cref="TryTourAsync"/>) بدل ما يتفرّج على جولة طويلة
    /// بالترتيب مفروضة عليه — مع خيار "شغّل كل الميزات بالترتيب"
    /// (<see cref="TryFullTourAsync"/>) لمين عايز الجولة القديمة برضو.
    /// </summary>
    public partial class HelpViewModel : ObservableObject
    {
        public IReadOnlyList<HelpTopic> MainTopics => HelpTopics.MainTopics;
        public IReadOnlyList<HelpTopic> DailyEntryTopics => HelpTopics.DailyEntryTopics;

        /// <summary>
        /// أكورديون كارت واحد مفتوح بس في كل القوائم (زي
        /// WorkersViewModel.ToggleSkillGroup بالظبط) — عشان شاشة الدليل
        /// تفضل قصيرة حتى مع 15 موضوع.
        /// </summary>
        [RelayCommand]
        private void ToggleTopic(HelpTopic? topic)
        {
            if (topic is null) return;

            var opening = !topic.IsExpanded;
            foreach (var other in MainTopics.Concat(DailyEntryTopics)) other.IsExpanded = false;
            topic.IsExpanded = opening;
        }

        /// <summary>
        /// بيشغّل سبوت لايت ميزة واحدة بس — نفس محرك جولة "إيه الجديد"
        /// بالظبط (MainWindow.RunTourAsync)، بقايمة خطوة واحدة (زرار
        /// "السابق" بيبقى معطّل تلقائي، عداد "1 من 1"). من غير ما نحتاج
        /// DI لمرجع النافذة (نفس نمط Application.Current.MainWindow
        /// المستخدم أصلًا كـOwner لديالوجات في DailyEntryViewModel وغيرها).
        /// </summary>
        [RelayCommand]
        private async Task TryTourAsync(AppTourStep? step)
        {
            if (step is null) return;
            if (Application.Current.MainWindow is MainWindow main)
                await main.RunTourAsync(new[] { step });
        }

        /// <summary>يشغّل كل ميزات الموضوع ورا بعض — الجولة الكاملة القديمة.</summary>
        [RelayCommand]
        private async Task TryFullTourAsync(HelpTopic? topic)
        {
            if (topic is null) return;
            if (Application.Current.MainWindow is MainWindow main)
                await main.RunTourAsync(topic.TourSteps);
        }

        /// <summary>
        /// بيفتح وضع تجربة (بيانات وهمية) ويشغّل فلو تدريب تفاعلي حقيقي —
        /// المستخدم بيدوس العنصر الحقيقي بنفسه، مش بس بيتفرّج (شوف
        /// MainWindow.RunGuidedPracticeAsync).
        /// </summary>
        [RelayCommand]
        private async Task TryGuidedPracticeAsync(GuidedPracticeFlow? flow)
        {
            if (flow is null) return;
            if (Application.Current.MainWindow is MainWindow main)
                await main.RunGuidedPracticeAsync(flow);
        }
    }
}
