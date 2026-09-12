using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.UI.ViewModels;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// عمر كائنات الجلسة — الضمانة اللي تسجيل الخروج النضيف قايم عليها.
    ///
    /// شاشة التسجيل اليومي بتمسك توزيع عمال لسه مش محفوظ. كانت
    /// <c>Singleton</c>، يعني بتعيش عبر تسجيلات الدخول، وتسجيل الخروج كان
    /// بيصفّرها **بإيده** بنداء صريح — وده كان بينسى أي حالة تتضاف بعد
    /// كده. بقت <c>Scoped</c> على نطاق الجلسة، فالحساب الجديد بياخد نسخة
    /// جديدة من غير ما حد يفتكر.
    ///
    /// الاختبار ده بيحرس السلوك ده: لو حد رجّعها Singleton، بيانات حساب
    /// هتوصل لحساب بعده **في صمت** ومفيش اختبار تاني في المشروع بيشوف ده.
    ///
    /// بنختبر الـ ViewModel مش MainWindow: بناء النافذة بيقرا من قاعدة
    /// البيانات الحقيقية (الهوية، عداد السجل)، والـ ViewModel بتاخد
    /// IServiceScopeFactory بس — نفس التسجيل، من غير أثر جانبي.
    /// </summary>
    [Collection("WPF")]
    public class SessionLifetimeTests
    {
        [Fact]
        public void نفس_الجلسة_بتدي_نفس_الشاشة()
        {
            var (first, second) = WpfThread.Run(() =>
            {
                using var session = Host.Services.CreateScope();

                // التنقّل لشاشة تانية والرجوع لازم يلاقي نفس النسخة —
                // ده اللي بيحافظ على رحلة إنتاج لسه مش محفوظة
                return (session.ServiceProvider.GetRequiredService<DailyEntryViewModel>(),
                        session.ServiceProvider.GetRequiredService<DailyEntryViewModel>());
            });

            Assert.Same(first, second);
        }

        [Fact]
        public void جلسة_جديدة_بتدي_شاشة_جديدة()
        {
            var (before, after) = WpfThread.Run(() =>
            {
                // جلستين منفصلتين = تسجيل دخول، خروج، ودخول تاني
                using var firstLogin = Host.Services.CreateScope();
                var a = firstLogin.ServiceProvider.GetRequiredService<DailyEntryViewModel>();

                using var secondLogin = Host.Services.CreateScope();
                var b = secondLogin.ServiceProvider.GetRequiredService<DailyEntryViewModel>();

                return (a, b);
            });

            // لو دول بقوا نفس الكائن، الحساب الجديد بيلاقي شغل الحساب
            // اللي قبله على الشاشة
            Assert.NotSame(before, after);
        }

        private static Microsoft.Extensions.Hosting.IHost Host
        {
            get
            {
                // WpfThread بيبني الـ App مرة واحدة، والـ App constructor
                // هو اللي بيركّب الـ AppHost
                WpfThread.Run(() => 0);
                return WorkforceManager.UI.App.AppHost;
            }
        }
    }
}
