using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;
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

        /// <summary>
        /// **الغلطة الحقيقية اللي اكتشفتها المراجعة دي**: `Setter
        /// Property="Kind"` بقيمة نصية غلط (زي "HistoryOutline" مش
        /// موجودة في PackIconKind، الصح "History") بتعدّي XamlLoadTests
        /// والاختبارات التانية كلها بسلام — تحويل النص لـ PackIconKind
        /// بيتأجّل لحد ما الـStyle يتطبّق فعليًا على صف حقيقي وقت الرسم،
        /// مش وقت تحميل XAML ولا مجرد تعيين ItemsSource. النتيجة: زرار
        /// شغّال ومحمّل بنجاح في كل الاختبارات، لكن بيرمي استثناء "Windows.
        /// Setter threw an exception" لأول مستخدم حقيقي يشوف نتيجة
        /// بالفئة دي. الاختبار ده بيفحص **نص ملف XAML نفسه** (نفس أسلوب
        /// ServiceRegistrationTests) بدل ما يحاول يجبر رسم WPF فعلي —
        /// أوثق وأبسط من محاولة تعطيل الـ Virtualization وقياس استثناء
        /// وقت التخطيط.
        /// </summary>
        [Fact]
        public void كل_اسم_أيقونة_في_XAML_موجود_فعلًا_في_PackIconKind()
        {
            var xaml = File.ReadAllText(Path.Combine(SolutionRoot(), @"WorkforceManager.UI\Views\GlobalSearchDialog.xaml"));

            // بيغطي الاستخدام المباشر (Kind="X") وبتاع الـSetter داخل الأنماط
            // (Property="Kind" Value="X") — الغلطة اللي اكتشفتها المراجعة
            // كانت في النوع التاني بالظبط، مش المباشر
            var names = Regex.Matches(xaml, @"Kind=""([A-Za-z]+)""|Property=""Kind""\s+Value=""([A-Za-z]+)""")
                .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
                .Distinct()
                .ToList();

            Assert.NotEmpty(names); // لو الـ regex اتكسر الاختبار ميعديش صامت

            var invalid = names.Where(n => !Enum.TryParse<PackIconKind>(n, out _)).ToList();

            Assert.True(invalid.Count == 0,
                "أسماء أيقونات مش موجودة في PackIconKind: " + string.Join("، ", invalid));
        }

        /// <summary>جذر الحل — بيتلاقى بالطلوع من مجلد الاختبارات (نفس منطق ServiceRegistrationTests.SolutionRoot)</summary>
        private static string SolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);

            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WorkforceManager.sln")))
                dir = dir.Parent;

            Assert.NotNull(dir);
            return dir!.FullName;
        }

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
