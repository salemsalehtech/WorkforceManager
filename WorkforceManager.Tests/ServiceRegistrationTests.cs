using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// كل خدمة في طبقة الأعمال لازم يكون ليها **مستخدم واحد على الأقل**
    /// ولازم تكون مسجّلة في الاختبارات زي ما هي مسجّلة في البرنامج.
    ///
    /// الاختبارات دي بتمسك نوعين من التعفّن بيتراكموا بصمت في أي مشروع
    /// بيكبر:
    ///
    /// 1. **خدمة مسجّلة ومحدش بينده عليها.** لقينا اتنين كده:
    ///    `PerformanceEvaluationService` (اتشال مع تبويب التقييم) و
    ///    `WeeklyReportExcelService` (428 سطر تصدير Excel، مُنشئ التقارير
    ///    بدّله ومحدش شال القديم). الكود الميت ده مش بس مساحة — أي حد
    ///    بيقرا البرنامج بعدين بيفتكره جزء شغّال ويحاول يصونه.
    ///
    /// 2. **خدمة في البرنامج ومش في الاختبارات.** ده بيخلي الخدمة تعدّي
    ///    من غير أي تغطية، وبيظهر كـ"No service registered" في أول اختبار
    ///    بيلمسها — بعد ما تكون اتكتبت خلاص (حصل مع ProductionChartService).
    ///
    /// الاختبارين بيقروا **الملفات المصدرية** مش الميتاداتا، لأن التسجيل
    /// نفسه هو اللي بيتقارن.
    /// </summary>
    public class ServiceRegistrationTests
    {
        /// <summary>جذر الحل — بيتلاقى بالطلوع من مجلد الاختبارات</summary>
        private static string SolutionRoot()
        {
            var dir = new DirectoryInfo(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);

            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WorkforceManager.sln")))
                dir = dir.Parent;

            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static string Read(string relative) =>
            File.ReadAllText(Path.Combine(SolutionRoot(), relative));

        /// <summary>أسماء الأنواع المسجّلة في ملف إعداد الحاويات</summary>
        private static HashSet<string> Registrations(string source) =>
            Regex.Matches(source, @"Add(?:Scoped|Singleton|Transient)<(?:[\w\.]+,\s*)?([\w]+)>")
                .Select(m => m.Groups[1].Value)
                .ToHashSet();

        [Fact]
        public void EveryServiceTheAppRegisters_IsAlsoRegisteredForTests()
        {
            // TestDatabase بيقلّد تسجيلات App.xaml.cs — لو خدمة اتضافت في
            // واحد بس، أول اختبار بيلمسها بيقع برسالة عن الحاوية مش عن
            // السلوك، والوقت بيضيع في الترجمة
            var app = Registrations(Read(@"WorkforceManager.UI\App.xaml.cs"));
            var tests = Registrations(Read(@"WorkforceManager.Tests\TestDatabase.cs"));

            // الشاشات والنوافذ محتاجة WPF فمش بتتسجّل في اختبارات الأعمال
            var uiOnly = app.Where(n =>
                n.EndsWith("View") || n.EndsWith("ViewModel") || n == "MainWindow").ToHashSet();

            var missing = app.Except(uiOnly).Except(tests).OrderBy(n => n).ToList();

            Assert.True(missing.Count == 0,
                "خدمات مسجّلة في البرنامج ومش في TestDatabase: " + string.Join("، ", missing));
        }

        [Fact]
        public void NoBusinessServiceIsRegisteredWithoutAnyCaller()
        {
            // خدمة مسجّلة ومحدش بينده عليها = كود ميت بيتصان بالغلط
            var root = SolutionRoot();
            var app = Read(@"WorkforceManager.UI\App.xaml.cs");

            var services = Registrations(app)
                .Where(n => n.EndsWith("Service"))
                .ToList();

            Assert.NotEmpty(services); // لو الـ regex اتكسر الاختبار ميعديش صامت

            // كل الكود ما عدا ملف التسجيل نفسه وملفات البناء
            var sources = Directory
                .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(@"\bin\") && !f.Contains(@"\obj\"))
                .Where(f => !f.EndsWith(@"App.xaml.cs"))
                .Where(f => !f.EndsWith(@"TestDatabase.cs"))
                .Where(f => !f.EndsWith(@"ServiceRegistrationTests.cs"))
                .Select(File.ReadAllText)
                .ToList();

            var orphans = services
                .Where(name => !sources.Any(s =>
                    s.Contains($"GetRequiredService<{name}>")
                    || s.Contains($"{name} ")      // حقن بالمُنشئ
                    || s.Contains($"{name}(")      // إنشاء مباشر
                    || s.Contains($"<{name}>")))
                .OrderBy(n => n)
                .ToList();

            Assert.True(orphans.Count == 0,
                "خدمات مسجّلة ومحدش بينده عليها: " + string.Join("، ", orphans));
        }
    }
}
