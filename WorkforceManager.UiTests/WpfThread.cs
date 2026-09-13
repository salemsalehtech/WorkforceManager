using System.Windows;
using System.Windows.Threading;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// خيط STA واحد بـ Dispatcher شغال، بيتعمل مرة واحدة للعملية كلها،
    /// وكل اختبار بيلمس WPF بيشتغل عليه.
    ///
    /// **ليه خيط واحد مشترك مش خيط لكل اختبار**: موارد التطبيق مش كلها
    /// متجمّدة — الفرش اللي جواها DynamicResource بتفضل حية، والحي في
    /// WPF بيبقى مملوك للخيط اللي عمله. أول ما اختبار يبني نافذة على خيط
    /// تاني غير اللي الـ App اتعملت عليه، أي StaticResource بيرمي
    /// "The calling thread cannot access this object". الخيط الواحد
    /// بيحل ده وبيحل كمان إن WPF مبيسمحش بأكتر من Application في العملية.
    ///
    /// الـ Dispatcher لازم يفضل شغال (مش مجرد خيط STA بينتهي) عشان
    /// ShowDialog تلاقي حلقة رسايل تشتغل جواها.
    /// </summary>
    public static class WpfThread
    {
        private static Dispatcher? _dispatcher;
        private static readonly object Gate = new();

        public static T Run<T>(Func<T> work)
        {
            Ensure();
            return _dispatcher!.Invoke(work);
        }

        private static void Ensure()
        {
            lock (Gate)
            {
                if (_dispatcher is not null) return;

                var ready = new ManualResetEventSlim();

                var thread = new Thread(() =>
                {
                    _dispatcher = Dispatcher.CurrentDispatcher;

                    // بناء الـ App بيدمج قواميس الموارد (Themes + App.xaml)
                    // زي التشغيل العادي — من غير كده كل StaticResource
                    // هيفشل بالغلط. الـ Constructor بيسجّل الاعتماديات بس؛
                    // اللي بيفتح قاعدة البيانات هو OnStartup ومبيتنداش
                    // غير مع Run.
                    var app = new UI.App();
                    app.InitializeComponent();

                    ready.Set();
                    Dispatcher.Run();
                })
                {
                    // عشان العملية تقدر تخرج والـ Dispatcher لسه شغال
                    IsBackground = true
                };

                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                ready.Wait();
            }
        }
    }
}
