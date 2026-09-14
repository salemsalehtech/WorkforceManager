using WorkforceManager.UI.Tour;
using WorkforceManager.UI.ViewModels;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// أوامر أكورديون شاشة الدليل (اختيار كارت الشبكة، وأكورديونات "تسجيل
    /// الإنتاج اليومي"/الأسئلة الشائعة/تعلم مميزات التحديث) بيانات مجرّدة —
    /// مش عناصر WPF — فمفيش داعي لـ WpfThread هنا، عكس اختبارات الديالوجات
    /// اللي بتلمس عناصر واجهة حقيقية.
    /// </summary>
    public class HelpViewModelTests
    {
        [Fact]
        public void SelectTopic_يحدد_موضوع_واحد_بس_ويقفل_الباقي()
        {
            var vm = new HelpViewModel();
            var first = vm.Topics[0];
            var second = vm.Topics[1];

            vm.SelectTopicCommand.Execute(first);
            Assert.Same(first, vm.SelectedTopic);
            Assert.True(first.IsExpanded);
            Assert.True(vm.HasSelectedTopic);

            vm.SelectTopicCommand.Execute(second);
            Assert.Same(second, vm.SelectedTopic);
            Assert.True(second.IsExpanded);
            Assert.False(first.IsExpanded);
        }

        [Fact]
        public void ToggleSubTopic_أكورديون_واحد_مفتوح_جوه_الموضوع_المختار_فقط()
        {
            var vm = new HelpViewModel();
            var dailyEntry = vm.Topics.Single(t => t.HasSubTopics);
            vm.SelectTopicCommand.Execute(dailyEntry);

            var firstSub = dailyEntry.SubTopics[0];
            var secondSub = dailyEntry.SubTopics[1];

            vm.ToggleSubTopicCommand.Execute(firstSub);
            Assert.True(firstSub.IsExpanded);

            vm.ToggleSubTopicCommand.Execute(secondSub);
            Assert.True(secondSub.IsExpanded);
            Assert.False(firstSub.IsExpanded);

            // دوسة تانية على نفس التبويب المفتوح بتقفله (أكورديون حقيقي، مش راديو)
            vm.ToggleSubTopicCommand.Execute(secondSub);
            Assert.False(secondSub.IsExpanded);
        }

        [Fact]
        public void ToggleFaq_أكورديون_سؤال_واحد_مفتوح_بس()
        {
            var vm = new HelpViewModel();
            var first = vm.Faq[0];
            var second = vm.Faq[1];

            vm.ToggleFaqCommand.Execute(first);
            Assert.True(first.IsExpanded);

            vm.ToggleFaqCommand.Execute(second);
            Assert.True(second.IsExpanded);
            Assert.False(first.IsExpanded);
        }

        [Fact]
        public void ToggleLearnVersion_أكورديون_إصدار_واحد_مفتوح_بس()
        {
            var vm = new HelpViewModel();
            Assert.NotEmpty(vm.LearnVersions);
            var first = vm.LearnVersions[0];

            // الأحدث مفتوح افتراضيًا في المحتوى نفسه
            Assert.True(first.IsExpanded);

            vm.ToggleLearnVersionCommand.Execute(first);
            Assert.False(first.IsExpanded);

            vm.ToggleLearnVersionCommand.Execute(first);
            Assert.True(first.IsExpanded);
        }

        [Fact]
        public void Topics_بترتيب_القايمة_الجانبية_وتسجيل_الإنتاج_اليومي_تالت_عنصر()
        {
            var vm = new HelpViewModel();

            Assert.Equal(9, vm.Topics.Count);
            Assert.True(vm.Topics[2].HasSubTopics);
            Assert.Equal(7, vm.Topics[2].SubTopics.Count);
        }
    }
}
