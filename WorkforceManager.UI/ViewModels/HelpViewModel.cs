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
    /// موضوع واحد لكل شاشة في القايمة الجانبية، بنفس ترتيبها (9 عناصر).
    /// شبكة مدمجة فوق (دوسة على كارت = <see cref="SelectTopic"/>، اختيار
    /// بسيط مش أكورديون) + لوحة تفاصيل بعرض كامل تحت مربوطة بـ
    /// <see cref="SelectedTopic"/>. "تسجيل الإنتاج اليومي" وحده عنده
    /// <see cref="HelpTopic.SubTopics"/> (السبع تبويبات الداخلية) —
    /// دي بتاخد أكورديون مستقل جوّه لوحة التفاصيل (<see cref="ToggleSubTopic"/>)،
    /// منفصل عن اختيار الكارت نفسه فوق.
    /// </summary>
    public partial class HelpViewModel : ObservableObject
    {
        public IReadOnlyList<HelpTopic> Topics => HelpTopics.Topics;
        public IReadOnlyList<FaqEntry> Faq => HelpFaq.Entries;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelectedTopic))]
        private HelpTopic? _selectedTopic;

        public bool HasSelectedTopic => SelectedTopic is not null;

        /// <summary>
        /// دوسة تايل في الشبكة = اختيار بسيط (مش أكورديون). IsExpanded بتاعة
        /// كل موضوع بتتظبط هنا كمان — مش لفتح/قفل حاجة، بس عشان تايل الشبكة
        /// يعرف يلوّن حدّه دهبي للتايل المختار من غير ما يحتاج مقارنة مرجع
        /// مع SelectedTopic (Binding عادي على IsExpanded بتاعة نفسه كفاية).
        /// </summary>
        [RelayCommand]
        private void SelectTopic(HelpTopic? topic)
        {
            foreach (var t in Topics) t.IsExpanded = false;
            if (topic is not null) topic.IsExpanded = true;
            SelectedTopic = topic;
        }

        /// <summary>
        /// أكورديون كارت واحد مفتوح بس **جوّه نفس الموضوع الأب** (تسجيل
        /// الإنتاج اليومي دلوقتي، أي موضوع عنده SubTopics مستقبلًا) — مش
        /// عبر الشاشة كلها زي اختيار الكارت الرئيسي فوق.
        /// </summary>
        [RelayCommand]
        private void ToggleSubTopic(HelpTopic? subTopic)
        {
            if (subTopic is null || SelectedTopic is null) return;

            var opening = !subTopic.IsExpanded;
            foreach (var other in SelectedTopic.SubTopics) other.IsExpanded = false;
            subTopic.IsExpanded = opening;
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

        /// <summary>أكورديون سؤال واحد مفتوح بس في قسم الأسئلة الشائعة.</summary>
        [RelayCommand]
        private void ToggleFaq(FaqEntry? entry)
        {
            if (entry is null) return;

            var opening = !entry.IsExpanded;
            foreach (var other in Faq) other.IsExpanded = false;
            entry.IsExpanded = opening;
        }
    }
}
