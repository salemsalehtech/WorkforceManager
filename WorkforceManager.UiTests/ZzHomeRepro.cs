// مؤقت — إعادة إنتاج عطل تحميل الرئيسية على نسخة من القاعدة الحقيقية
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.Services;
using WorkforceManager.UI;
using WorkforceManager.UI.ViewModels;
using Xunit;

namespace WorkforceManager.UiTests
{
    [Collection("WPF")]
    public class ZzHomeRepro
    {
        [Fact]
        public void Repro()
        {
            var src = @"C:\ProgramData\WorkforceManager\workforce.db";
            if (!File.Exists(src) || Environment.GetEnvironmentVariable("HOME_REPRO") is not { } outFile) return;
            var copy = Path.Combine(Path.GetTempPath(), $"wfm-repro-{Guid.NewGuid():N}.db");
            File.Copy(src, copy);
            var services = new ServiceCollection();
            typeof(App).Assembly.GetType("WorkforceManager.UI.AppServiceRegistration")!
                .GetMethod("AddWorkforceManagerCore", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(null, new object[] { services, $"Data Source={copy}" });
            var sp = services.BuildServiceProvider();

            WpfThread.Run(() =>
            {
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var vm = new HomeViewModel(sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<CurrentUserContext>());
                var view = new UI.Views.HomeView(vm);
                var win = new Window { Content = view, Width = 1200, Height = 700, Left = -6000, ShowActivated = false, FlowDirection = FlowDirection.RightToLeft };
                string result = "OK";
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
                {
                    try { await vm.LoadAsync(); }
                    catch (Exception ex) { result = ex.ToString(); }
                    frame.Continue = false;
                });
                win.Show();
                Dispatcher.PushFrame(frame);
                File.WriteAllText(outFile, result);
                win.Close();
                return 0;
            });
        }
    }
}
