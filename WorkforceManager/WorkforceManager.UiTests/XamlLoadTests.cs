using System.Collections;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Markup;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// بيحمّل كل ملف XAML في البرنامج تحميلًا حقيقيًا.
    ///
    /// ليه ده موجود: أخطاء XAML كتير **مبتظهرش في البناء ولا في باقي
    /// الاختبارات** — بتظهر أول ما الشاشة تتفتح عند المستخدم:
    ///   • اسم أيقونة (PackIconKind) غلط
    ///   • مفتاح StaticResource مش موجود
    ///   • BasedOn بـ DynamicResource (خاصية CLR مش DependencyProperty)
    ///   • x:Name متكرر في نفس النطاق، أو TargetName بره نطاقه
    /// كل دول بيرموا وقت التحميل. وبما إن MainWindow بتبني WorkersView في
    /// الـ Constructor بتاعها، غلطة زي دي في أي شاشة افتراضية بتمنع البرنامج
    /// من إنه يفتح أصلًا.
    ///
    /// الاختبار مش بيقرا مسارات ملفات — بيعدّي على موارد الـ BAML المتولدة
    /// جوه المجمّع نفسه، فأي ملف XAML جديد بيتغطى تلقائيًا من غير ما حد
    /// يفتكر يضيفه هنا.
    /// </summary>
    public class XamlLoadTests
    {
        [Fact]
        public void كل_ملفات_XAML_بتتحمل_من_غير_أخطاء()
        {
            var failures = RunOnStaThread(LoadEveryXamlFile);

            Assert.True(failures.Count == 0,
                "ملفات XAML دي مبتتحملش:\n" + string.Join("\n", failures));
        }

        private static List<string> LoadEveryXamlFile()
        {
            var assembly = typeof(UI.App).Assembly;

            // بناء الـ App بيدمج قواميس الموارد (Themes + App.xaml) زي
            // التشغيل العادي — من غير كده كل StaticResource هيفشل بالغلط.
            // الـ Constructor بيسجّل الاعتماديات بس؛ اللي بيفتح قاعدة
            // البيانات هو OnStartup ومبيتنداش غير مع Run.
            var app = new UI.App();
            app.InitializeComponent();

            var failures = new List<string>();

            foreach (var xaml in GetCompiledXamlPaths(assembly))
            {
                try
                {
                    LoadOne(assembly, xaml);
                }
                catch (Exception ex)
                {
                    var chain = Unwrap(ex).ToList();

                    // InitializeComponent هو أول حاجة في أي Constructor، فلو
                    // العطل مش XamlParseException يبقى الـ XAML اتحمّل خلاص
                    // والباقي إن الـ Constructor عايز ViewModel حقيقي.
                    if (!chain.Any(e => e is XamlParseException)) continue;

                    // Application.ResourceAssembly بتتثبّت على المجمّع اللي
                    // بيشغّل (مضيف الاختبارات) ومينفعش تتغيّر، فأي مورد
                    // بمسار نسبي (زي أيقونة النافذة) مش هيتلاقى هنا مع إنه
                    // موجود فعلًا وقت التشغيل.
                    if (chain.Any(e => e is IOException io &&
                        io.Message.Contains("Cannot locate resource"))) continue;

                    failures.Add($"  {xaml}\n" + string.Join("\n",
                        chain.Select(e => $"      [{e.GetType().Name}] {e.Message}")));
                }
            }

            return failures;
        }

        /// <summary>
        /// بيحمّل ملف واحد. الشاشات بتاخد ViewModel من الـ DI، والـ Constructor
        /// بيعمل InitializeComponent الأول وبعدين بيحطه في DataContext — فتمرير
        /// null بيشغّل تحميل الـ XAML، وهو اللي بنختبره، من غير قاعدة بيانات.
        /// القواميس (Themes) مالهاش نوع فبتتحمّل بـ LoadComponent مباشرة.
        /// </summary>
        private static void LoadOne(Assembly assembly, string xamlPath)
        {
            var typeName = "WorkforceManager.UI." +
                Path.ChangeExtension(xamlPath, null)!.Replace('/', '.');

            var type = assembly.GetType(typeName, throwOnError: false, ignoreCase: true);

            if (type is null)
            {
                Application.LoadComponent(new Uri("/WorkforceManager;component/" + xamlPath, UriKind.Relative));
                return;
            }

            var constructor = type
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .OrderBy(c => c.GetParameters().Length)
                .First();

            constructor.Invoke(new object?[constructor.GetParameters().Length]);
        }

        /// <summary>
        /// مسارات كل XAML اتجمّع جوه المجمّع، من جدول موارد الـ BAML.
        /// المصدر ده هو اللي بيتشحن فعلًا، فمفيش ملف بيفوت الاختبار.
        /// </summary>
        private static IEnumerable<string> GetCompiledXamlPaths(Assembly assembly)
        {
            using var stream = assembly.GetManifestResourceStream("WorkforceManager.g.resources")
                ?? throw new InvalidOperationException("مش لاقي موارد الواجهة المتولدة");

            using var reader = new ResourceReader(stream);

            return reader.Cast<DictionaryEntry>()
                .Select(entry => (string)entry.Key)
                .Where(name => name.EndsWith(".baml", StringComparison.OrdinalIgnoreCase))
                .Select(name => Path.ChangeExtension(name, ".xaml")!)
                // App.xaml اتحمّل مع بناء الـ App فوق، وقواميس Themes معاه
                .Where(name => !name.Equals("app.xaml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name)
                .ToList();
        }

        /// <summary>سلسلة الأعطال من برّا لجوّه، من غير غلاف الـ Reflection.</summary>
        private static IEnumerable<Exception> Unwrap(Exception ex)
        {
            for (Exception? e = ex; e is not null; e = e.InnerException)
            {
                if (e is TargetInvocationException) continue;
                yield return e;
            }
        }

        /// <summary>
        /// WPF بيتطلب خيط STA، وxUnit بيشغّل على MTA — فبنعمل الخيط بنفسنا
        /// بدل ما نضيف حزمة كاملة عشان سمة واحدة.
        /// </summary>
        private static T RunOnStaThread<T>(Func<T> work)
        {
            T result = default!;
            ExceptionDispatchInfo? failure = null;

            var thread = new Thread(() =>
            {
                try { result = work(); }
                catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            failure?.Throw();
            return result;
        }
    }
}
