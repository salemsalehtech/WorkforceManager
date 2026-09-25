using WorkforceManager.UI.ViewModels;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// قاعدة "كارت واحد بس مقلوب في المرة" — WorkersViewModel.NextFlippedWorker
    /// دالة نقية (مش محتاجة قاعدة بيانات ولا نسخة ViewModel كاملة)، شوف
    /// CLAUDE.md قسم "شاشة العمال" لتفاصيل الكارت المقلوب.
    /// </summary>
    public class WorkerCardFlipTests
    {
        private static WorkerRow Worker(int id) => new() { WorkerId = id, FullName = $"عامل {id}" };

        [Fact]
        public void ClickingAnUnflippedCard_FlipsIt()
        {
            var clicked = Worker(1);

            var result = WorkersViewModel.NextFlippedWorker(currentlyFlipped: null, clicked);

            Assert.Same(clicked, result);
        }

        [Fact]
        public void ClickingTheAlreadyFlippedCardAgain_FlipsItBack()
        {
            var flipped = Worker(1);

            var result = WorkersViewModel.NextFlippedWorker(currentlyFlipped: flipped, clicked: flipped);

            Assert.Null(result);
        }

        [Fact]
        public void ClickingADifferentCard_FlipsTheNewOneAndImplicitlyClosesTheOld()
        {
            var current = Worker(1);
            var clicked = Worker(2);

            var result = WorkersViewModel.NextFlippedWorker(currentlyFlipped: current, clicked);

            // النتيجة بترجع الجديد بس — القديم مبيترجعش من هنا، لأن
            // OnFlippedWorkerChanged هي اللي بتلف على _allWorkers كلها
            // وتحط IsFlipped=false على أي صف مش هو ده بالظبط (شوف تعليقها)
            Assert.Same(clicked, result);
            Assert.NotSame(current, result);
        }
    }
}
