using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using MaterialDesignThemes.Wpf;
using WorkforceManager.Business.DTOs;
using WorkforceManager.UI.ViewModels;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// قوائم كارت العامل/المنتج — التوفّر (مين ظاهر/مقفول) دوال نقية على
    /// WorkerRow/ProductRow، وأسماء الأيقونات بنفس أسلوب
    /// GlobalSearchDialogTests (النص بيتأجّل لحد الرسم الفعلي، فأي غلطة
    /// اسم بتعدّي تحميل XAML بسلام).
    /// </summary>
    public class CardContextMenuTests
    {
        private static WorkerRow Worker(bool isHourly = false, bool isActive = true) =>
            new() { WorkerId = 1, FullName = "أحمد علي", IsHourly = isHourly, IsActive = isActive };

        [Fact]
        public void EditSkills_HiddenForHourlyWorker()
        {
            Assert.True(Worker(isHourly: false).CanEditSkills);
            Assert.False(Worker(isHourly: true).CanEditSkills);
        }

        [Fact]
        public void DailyEntryActions_DisabledForInactiveWorker()
        {
            Assert.True(Worker(isActive: true).CanRecordDailyEntry);
            Assert.False(Worker(isActive: false).CanRecordDailyEntry);
        }

        [Theory]
        [InlineData(DailyEntryWorkerTarget.Attendance, DailyEntryViewModel.AttendanceTab)]
        [InlineData(DailyEntryWorkerTarget.Penalty, DailyEntryViewModel.PenaltiesTab)]
        [InlineData(DailyEntryWorkerTarget.Adjustment, DailyEntryViewModel.AdjustmentsTab)]
        public void TabIndexFor_MapsEachTarget(DailyEntryWorkerTarget target, int expectedTab)
        {
            Assert.Equal(expectedTab, DailyEntryViewModel.TabIndexFor(target));
        }

        [Fact]
        public void ProductRow_MonthlyPlan_DisabledWhenInactive()
        {
            Assert.True(new ProductRow { ProductId = 1, IsActive = true }.CanUseMonthlyPlan);
            Assert.False(new ProductRow { ProductId = 1, IsActive = false }.CanUseMonthlyPlan);
        }

        [Fact]
        public void ProductsViewModel_CardMenu_DisabledDuringBulkSelect()
        {
            var vm = new ProductsViewModel(scopeFactory: null!);

            Assert.True(vm.CanShowCardMenu);
            vm.IsBulkSelectMode = true;
            Assert.False(vm.CanShowCardMenu);
        }

        [Fact]
        public void ReportBuilder_ShowProductionFor_ChecksOnlyThatProduct_SelectsProductionSubject()
        {
            var vm = new ReportBuilderViewModel(scopeFactory: null!);
            vm.Products.Add(new CheckableItem(1, "دبلة"));
            vm.Products.Add(new CheckableItem(2, "سلسلة") { IsChecked = true }); // من فلترة قديمة، لازم يتلغي

            vm.ShowProductionFor(1);

            Assert.Equal(ReportSubject.Production, vm.SelectedSubject.Subject);
            Assert.True(vm.Products.Single(p => p.Id == 1).IsChecked);
            Assert.False(vm.Products.Single(p => p.Id == 2).IsChecked);
        }

        [Theory]
        [InlineData("WorkersView.xaml")]
        [InlineData("ProductsView.xaml")]
        public void كل_اسم_أيقونة_في_XAML_موجود_فعلًا_في_PackIconKind(string fileName)
        {
            var xaml = File.ReadAllText(Path.Combine(SolutionRoot(), "WorkforceManager.UI", "Views", fileName));

            var names = Regex.Matches(xaml, @"Kind=""([A-Za-z]+)""|Property=""Kind""\s+Value=""([A-Za-z]+)""")
                .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
                .Distinct()
                .ToList();

            Assert.NotEmpty(names);

            var invalid = names.Where(n => !Enum.TryParse<PackIconKind>(n, out _)).ToList();

            Assert.True(invalid.Count == 0,
                $"أسماء أيقونات مش موجودة في PackIconKind ({fileName}): " + string.Join("، ", invalid));
        }

        private static string SolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WorkforceManager.sln")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }
    }
}
