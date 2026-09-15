using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using WorkforceManager.Business.DTOs;
using WorkforceManager.UI.Views;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// البحث الشامل الجديد — الديالوج نفسه مالوش منطق مطابقة (شوف
    /// SearchMatcherTests/GlobalSearchServiceTests لمنطق المطابقة الحقيقي)،
    /// لكن عنده منطق واجهة جديد يستاهل تغطية: التجميع بالفئة، حالة "مفيش
    /// نتائج"، وتأكيد الاختيار.
    ///
    /// الـ constructor خاص عن قصد (نفس MessageDialog — النداء من Ask بس)،
    /// فالاختبار بينادي عليه وعلى RunSearchAsync/Confirm بالـ Reflection،
    /// نفس أسلوب MessageDialogTests بالظبط. RunSearchAsync بتتنادى مباشرة
    /// (مش عن طريق كتابة نص وانتظار الـ debounce الحقيقي 250ms) عشان
    /// الاختبار يبقى حتمي وسريع.
    /// </summary>
    [Collection("WPF")]
    public class GlobalSearchDialogTests
    {
        private static readonly GlobalSearchResult WorkerResult = new()
        {
            Category = SearchCategory.Worker, PrimaryText = "أحمد", Score = 1000, WorkerId = 1
        };

        private static readonly GlobalSearchResult ProductResult = new()
        {
            Category = SearchCategory.Product, PrimaryText = "دبلة", Score = 900, ProductId = 1
        };

        private static readonly GlobalSearchResult SettingResult = new()
        {
            Category = SearchCategory.Setting, PrimaryText = "الوضع الليلي", Score = 800,
            SettingTargetElementName = "DarkModeCard"
        };

        [Fact]
        public void نتايج_البحث_بتتجمع_بالفئة_وتتعرض()
        {
            var (itemsSource, emptyVisibility) = WpfThread.Run(() =>
            {
                var dialog = Build(_ => Task.FromResult<IReadOnlyList<GlobalSearchResult>>(
                    new[] { WorkerResult, ProductResult, SettingResult }));

                RunSearch(dialog, "أ");

                return (ResultsList(dialog).ItemsSource, EmptyText(dialog).Visibility);
            });

            Assert.NotNull(itemsSource);
            var results = itemsSource!.Cast<GlobalSearchResult>().ToList();
            Assert.Equal(3, results.Count);
            Assert.Contains(results, r => r.Category == SearchCategory.Worker);
            Assert.Contains(results, r => r.Category == SearchCategory.Product);
            Assert.Contains(results, r => r.Category == SearchCategory.Setting);
            Assert.Equal(Visibility.Collapsed, emptyVisibility);
        }

        [Fact]
        public void استعلام_فاضي_بيمسح_النتايج_من_غير_ما_ينادي_البحث()
        {
            var searchWasCalled = false;

            var (itemsSource, emptyVisibility) = WpfThread.Run(() =>
            {
                var dialog = Build(_ =>
                {
                    searchWasCalled = true;
                    return Task.FromResult<IReadOnlyList<GlobalSearchResult>>(new[] { WorkerResult });
                });

                RunSearch(dialog, "");

                return (ResultsList(dialog).ItemsSource, EmptyText(dialog).Visibility);
            });

            Assert.False(searchWasCalled);
            Assert.Null(itemsSource);
            Assert.Equal(Visibility.Collapsed, emptyVisibility); // مفيش "مفيش نتائج" لسه ماكتبش حاجة أصلًا
        }

        [Fact]
        public void مفيش_نتائج_بتظهر_الرسالة_المناسبة()
        {
            var emptyVisibility = WpfThread.Run(() =>
            {
                var dialog = Build(_ => Task.FromResult<IReadOnlyList<GlobalSearchResult>>(Array.Empty<GlobalSearchResult>()));
                RunSearch(dialog, "مفيش زي كده");

                return EmptyText(dialog).Visibility;
            });

            Assert.Equal(Visibility.Visible, emptyVisibility);
        }

        [Fact]
        public void اختيار_نتيجة_وتأكيدها_بيحط_Chosen()
        {
            // Confirm() بتحط DialogResult، واللي مينفعش غير على نافذة
            // اتعرضت فعلًا بـ ShowDialog — نفس سبب ShowAndClick في
            // MessageDialogTests بالظبط
            var chosen = WpfThread.Run(() =>
            {
                var dialog = Build(_ => Task.FromResult<IReadOnlyList<GlobalSearchResult>>(new[] { WorkerResult }));

                dialog.Loaded += (_, _) =>
                {
                    RunSearch(dialog, "أ");
                    ResultsList(dialog).SelectedItem = WorkerResult;
                    Confirm(dialog);
                };

                dialog.ShowDialog();

                return (GlobalSearchResult?)typeof(GlobalSearchDialog)
                    .GetProperty(nameof(GlobalSearchDialog.Chosen))!
                    .GetValue(dialog);
            });

            Assert.Same(WorkerResult, chosen);
        }

        // ======================= أدوات =======================

        private static GlobalSearchDialog Build(Func<string, Task<IReadOnlyList<GlobalSearchResult>>> search)
        {
            var ctor = typeof(GlobalSearchDialog).GetConstructors(
                BindingFlags.Instance | BindingFlags.NonPublic).Single();

            return (GlobalSearchDialog)ctor.Invoke(new object[] { search });
        }

        /// <summary>بينادي RunSearchAsync مباشرة بالـ Reflection — بيتخطى الـ debounce الحقيقي (250ms) عشان الاختبار يبقى حتمي وسريع</summary>
        private static void RunSearch(GlobalSearchDialog dialog, string query)
        {
            var method = typeof(GlobalSearchDialog).GetMethod(
                "RunSearchAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

            // الـ delegate هنا دايمًا Task.FromResult (مكتمل فورًا)، فالـ
            // await جوه RunSearchAsync بيكمل تزامنيًا من غير الحاجة لضخ
            // الـ Dispatcher — GetAwaiter().GetResult() آمن هنا
            ((Task)method.Invoke(dialog, new object[] { query })!).GetAwaiter().GetResult();
        }

        private static void Confirm(GlobalSearchDialog dialog)
        {
            var method = typeof(GlobalSearchDialog).GetMethod(
                "Confirm", BindingFlags.Instance | BindingFlags.NonPublic)!;

            method.Invoke(dialog, null);
        }

        private static ListBox ResultsList(GlobalSearchDialog dialog) => (ListBox)dialog.FindName("ResultsList")!;
        private static TextBlock EmptyText(GlobalSearchDialog dialog) => (TextBlock)dialog.FindName("EmptyText")!;
    }
}
