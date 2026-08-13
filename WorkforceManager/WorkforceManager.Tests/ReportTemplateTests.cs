using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// القوالب هي اللي بتخلي مرونة التقارير تنفع فعلاً: من غيرها المدير
    /// اللي بيعمل نفس التقرير كل شهر بيظبّطه من الأول كل شهر — يعني
    /// المرونة بتكلّفه شغل بدل ما توفّر عليه.
    ///
    /// عشان كده أهم حاجة هنا إن القالب يرجع **زي ما اتحفظ بالظبط**،
    /// وإنه يفضل يفتح حتى لما البرنامج يتغيّر تحته.
    /// </summary>
    public class ReportTemplateTests : IDisposable
    {
        private readonly string _path =
            Path.Combine(Path.GetTempPath(), $"wm-templates-{Guid.NewGuid():N}.json");

        public void Dispose()
        {
            try { if (File.Exists(_path)) File.Delete(_path); } catch { /* ملف مؤقت */ }
        }

        private static ReportTemplate Full(string name = "قالبي") => new()
        {
            Name = name,
            Subject = ReportSubject.Production,
            GroupBy = ReportGrouping.Product,
            Period = ReportPeriodKind.ThisMonth,
            WorkerKind = WorkerKindFilter.ByProduction,
            WorkerIds = new List<int> { 1, 2 },
            ProductIds = new List<int> { 3 },
            StageIds = new List<int> { 4 },
            ColumnLayout = new List<ReportColumnChoice>
            {
                new() { Key = "workdays" },
                new() { Key = "pieces", Header = "الإنتاج التام" },
                new() { Key = "workers", Visible = false }
            },
            SortKey = "pieces",
            SortDescending = true,
            TopN = 10,
            CompareWithPrevious = true,
            ExportDetailSheet = true,
            ExportSheetPerGroup = true
        };

        [Fact]
        public void ASavedTemplate_ComesBackWithEverythingTheUserSetUp()
        {
            ReportTemplateStore.Save(Full(), _path);

            var loaded = Assert.Single(ReportTemplateStore.LoadSaved(_path));

            Assert.Equal(ReportSubject.Production, loaded.Subject);
            Assert.Equal(ReportGrouping.Product, loaded.GroupBy);
            Assert.Equal(WorkerKindFilter.ByProduction, loaded.WorkerKind);

            Assert.Equal(new[] { 1, 2 }, loaded.WorkerIds);
            Assert.Equal(new[] { 3 }, loaded.ProductIds);
            Assert.Equal(new[] { 4 }, loaded.StageIds);

            Assert.Equal("pieces", loaded.SortKey);
            Assert.True(loaded.SortDescending);
            Assert.Equal(10, loaded.TopN);
            Assert.True(loaded.CompareWithPrevious);
            Assert.True(loaded.ExportSheetPerGroup);

            // الأعمدة بترتيبها وأسمائها وإخفائها
            Assert.Equal(
                new[] { "workdays", "pieces", "workers" },
                loaded.ColumnLayout!.Select(c => c.Key));
            Assert.Equal("الإنتاج التام", loaded.ColumnLayout![1].Header);
            Assert.False(loaded.ColumnLayout![2].Visible);
        }

        [Fact]
        public void TheTemplateBecomesASpec_ThatCarriesTheSameChoices()
        {
            var spec = Full().ToSpec();

            Assert.Equal(ReportSubject.Production, spec.Subject);
            Assert.Equal(new[] { 1, 2 }, spec.WorkerIds);
            Assert.Equal("pieces", spec.SortKey);
            Assert.Equal(10, spec.TopN);
            Assert.True(spec.CompareWithPrevious);
            Assert.Equal(3, spec.ColumnLayout!.Count);
        }

        [Fact]
        public void ThePeriodIsSavedAsAKind_NotAsTwoFixedDates()
        {
            // قالب اسمه "أجور الشهر" لازم يجيب الشهر الحالي كل مرة، مش
            // الشهر اللي اتحفظ فيه
            var spec = new ReportTemplate
            {
                Name = "أجور الشهر",
                Subject = ReportSubject.Wages,
                Period = ReportPeriodKind.ThisMonth
            }.ToSpec();

            Assert.Equal(DateTime.Today.Month, spec.From.Month);
            Assert.Equal(DateTime.Today, spec.To.Date);
        }

        [Fact]
        public void AnOldTemplateWithNoColumnChoices_StillOpens()
        {
            // القوالب المحفوظة قبل ما محرّر الأعمدة يتزوّد
            ReportTemplateStore.Save(new ReportTemplate
            {
                Name = "قالب قديم",
                Subject = ReportSubject.Production,
                GroupBy = ReportGrouping.Worker,
                Period = ReportPeriodKind.ThisWeek
            }, _path);

            var loaded = Assert.Single(ReportTemplateStore.LoadSaved(_path));

            Assert.Null(loaded.ColumnLayout);
            Assert.Null(loaded.ToSpec().ColumnLayout); // مفيش تخطيط = الأعمدة الافتراضية
        }

        [Fact]
        public void SavingTwiceWithTheSameName_Replaces_DoesNotDuplicate()
        {
            ReportTemplateStore.Save(Full("نفس الاسم"), _path);

            var second = Full("نفس الاسم");
            second.TopN = 5;
            ReportTemplateStore.Save(second, _path);

            var loaded = Assert.Single(ReportTemplateStore.LoadSaved(_path));
            Assert.Equal(5, loaded.TopN);
        }

        [Fact]
        public void RenamingATemplate_KeepsEverythingElseTheSame()
        {
            ReportTemplateStore.Save(Full("الاسم القديم"), _path);

            ReportTemplateStore.Rename("الاسم القديم", "الاسم الجديد", _path);

            var loaded = Assert.Single(ReportTemplateStore.LoadSaved(_path));
            Assert.Equal("الاسم الجديد", loaded.Name);
            Assert.Equal(ReportGrouping.Product, loaded.GroupBy);
            Assert.Equal(10, loaded.TopN);
        }

        [Fact]
        public void RenamingToAnAlreadyTakenName_IsRefused()
        {
            ReportTemplateStore.Save(Full("الأول"), _path);
            ReportTemplateStore.Save(Full("الثاني"), _path);

            Assert.Throws<InvalidOperationException>(() =>
                ReportTemplateStore.Rename("الأول", "الثاني", _path));

            // مفيش حاجة اتغيّرت — الاتنين لسه بأسمائهم
            var names = ReportTemplateStore.LoadSaved(_path).Select(t => t.Name).ToList();
            Assert.Contains("الأول", names);
            Assert.Contains("الثاني", names);
        }

        [Fact]
        public void RenamingANonExistentTemplate_IsRefused()
        {
            Assert.Throws<InvalidOperationException>(() =>
                ReportTemplateStore.Rename("مش موجود", "اسم تاني", _path));
        }

        [Fact]
        public void ABrokenTemplatesFile_NeverStopsTheScreenFromOpening()
        {
            File.WriteAllText(_path, "{ ده مش JSON أصلاً ");

            Assert.Empty(ReportTemplateStore.LoadSaved(_path));
            Assert.NotEmpty(ReportTemplateStore.Load(_path)); // الجاهزة بتفضل موجودة
        }

        [Fact]
        public void TheBuiltInTemplates_AreAllValidCombinations()
        {
            // قالب جاهز بتركيبة مرفوضة معناه المستخدم بيدوس عليه ويلاقي
            // شاشة فاضية من غير ما يفهم ليه
            foreach (var template in ReportTemplateStore.BuiltIn())
                Assert.True(
                    ReportSpec.IsAllowed(template.Subject, template.GroupBy),
                    $"القالب الجاهز \"{template.Name}\" تركيبته مرفوضة");
        }
    }
}
