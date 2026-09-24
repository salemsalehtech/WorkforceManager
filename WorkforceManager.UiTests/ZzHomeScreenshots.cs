// مؤقت — لقطات شاشة الرئيسية، بيتمسح بعد المراجعة
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.UI.ViewModels;
using WorkforceManager.UI.Views;
using Xunit;

namespace WorkforceManager.UiTests
{
    [Collection("WPF")]
    public class ZzHomeScreenshots
    {
        private static readonly string Out = Environment.GetEnvironmentVariable("HOME_SHOTS") ?? "";

        [Fact]
        public void Shots()
        {
            if (Out == "") return;
            WpfThread.Run(() =>
            {
                foreach (var dark in new[] { false, true })
                foreach (var (w, h) in new[] { (848, 560), (1400, 900) })
                {
                    Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    SetPalette(dark);
                    Render(Full(), $"full_{(dark ? "dark" : "light")}_{w}.png", w, h, 2400);
                    Render(Empty(), $"empty_{(dark ? "dark" : "light")}_{w}.png", w, h, 2400);
                }
                return 0;
            });
        }

        private static void SetPalette(bool dark)
        {
            var md = Application.Current.Resources.MergedDictionaries;
            for (var i = 0; i < md.Count; i++)
                if (md[i].Source?.OriginalString.Contains("Palette.") == true)
                    md[i] = new ResourceDictionary { Source = new Uri($"pack://application:,,,/{typeof(UI.App).Assembly.GetName().Name};component/Themes/Palette.{(dark ? "Dark" : "Light")}.xaml") };
        }

        private static HomeViewModel Base()
        {
            var user = new CurrentUserContext();
            user.UpdateDisplayName("salem", "سالم صالح");
            var vm = new HomeViewModel(null!, user);
            vm.WelcomeText = "مرحبًا، سالم صالح";
            vm.TodayText = "الخميس، 24 سبتمبر 2026";
            vm.WeekRangeText = "أسبوع الشغل من 24 سبتمبر إلى 30 سبتمبر";
            vm.DaysLeftInWeekText = "باقي 6 أيام";
            vm.MotivationalMessage = "الفريق شغال كويس — يلا نكمل بنفس الحماس.";
            return vm;
        }

        private static HomeViewModel Empty()
        {
            var vm = Base();
            vm.StreakNumberText = "0";
            vm.StreakCaption = "النهارده بداية سلسلة جديدة — يوم من غير غياب بدون إذن ويبدأ العدّ";
            return vm;
        }

        private static HomeViewModel Full()
        {
            var vm = Base();
            vm.TotalPiecesThisWeek = 1840; vm.PiecesTrend = TrendBadge.For(15);
            vm.ActiveWorkersThisWeek = 23; vm.ActiveWorkersTrend = TrendBadge.For(0);
            vm.NetWorkdaysThisWeek = 96.5m; vm.NetWorkdaysTrend = TrendBadge.For(-4);
            vm.UnexcusedAbsencesThisWeek = 3; vm.AbsencesTrend = TrendBadge.For(-40, higherIsBetter: false);
            vm.AttendanceRateText = "نسبة الحضور 94%";
            vm.BestWorker = new HomeWorkerCard(1, "محمد عبد الرحمن", "مع", 412, 6, TrendBadge.For(12));
            vm.WorstWorker = new HomeWorkerCard(2, "أحمد سمير", "أس", 38, 1.5m, TrendBadge.For(-22));
            vm.TopProduct = new HomeProductCard(1, "قميص بولو", 920, TrendBadge.For(18));
            vm.BottomProduct = new HomeProductCard(2, "جاكيت شتوي", 45, TrendBadge.For(-30));
            vm.HasStreak = true; vm.StreakNumberText = "12";
            vm.StreakCaption = "يوم شغل متتالي من غير غياب بدون إذن 👏";
            vm.StaleBalanceCount = 2;
            var today = new DateTime(2026, 9, 24);
            vm.DueMemories.Add(new HomeMemoryItem(new ProductionMemoryDto { ProductName = "بنطلون جينز", Notes = "نبدأ بالقص قبل الخياطة — القماش وصل", RemindOn = today }, "النهارده", true));
            vm.DueMemories.Add(new HomeMemoryItem(new ProductionMemoryDto { ProductName = "تيشيرت قطن", RemindOn = today.AddDays(2) }, "بعد 2 أيام — السبت 26 سبتمبر", false));
            vm.HasDueMemories = true;
            var pts = new List<ProductOutputPointDto>();
            var vals = new[] { 310, 280, 0, 350, 420, 0, 0 };
            for (var i = 0; i < 5; i++)
            {
                var d = today.AddDays(i);
                if (vals[i] == 0) continue;
                pts.Add(new() { BucketStart = d, BucketEnd = d, ProductId = 1, ProductName = "قميص بولو", CompletedPieces = vals[i] * 2 / 3, ScrapPieces = 4 });
                pts.Add(new() { BucketStart = d, BucketEnd = d, ProductId = 2, ProductName = "جاكيت شتوي", CompletedPieces = vals[i] / 3 });
            }
            var chart = ProductOutputChartBuilder.Build(pts, today, today.AddDays(6), ChartGrain.Day, new Dictionary<int, int>(), HomeViewModel.WeekChartMaxBarHeight);
            foreach (var b in chart.Buckets) vm.WeekChartBuckets.Add(b);
            vm.WeekChartHasData = chart.HasData;
            return vm;
        }

        private static void Render(HomeViewModel vm, string name, int w, int h, int ms)
        {
            var view = new HomeView(vm) { FlowDirection = FlowDirection.RightToLeft };
            var host = new System.Windows.Controls.Border { Child = view, Background = (Brush)Application.Current.FindResource("GroundBrush") };
            var win = new Window
            {
                Content = host, Width = w, Height = h, Left = -6000, Top = 0, ShowActivated = false,
                WindowStyle = WindowStyle.None, ShowInTaskbar = false
            };
            win.Show();
            var frame = new DispatcherFrame();
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (_, _) => { t.Stop(); frame.Continue = false; };
            t.Start();
            Dispatcher.PushFrame(frame);

            win.Content = null;
            var root = (FrameworkElement)view.FindName("RootPanel");
            var fullH = Math.Max(h, root.ActualHeight + root.Margin.Top + root.Margin.Bottom);
            host.Measure(new Size(w, fullH));
            host.Arrange(new Rect(0, 0, w, fullH)); host.UpdateLayout();
            var rtb = new RenderTargetBitmap(w, (int)fullH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(host);
            // RenderTargetBitmap بيطلّع محتوى RTL معكوس أفقيًا — بنرجّعه
            var flipped = new TransformedBitmap(rtb, new ScaleTransform(-1, 1));
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(flipped));
            using var fs = File.Create(Path.Combine(Out, name));
            enc.Save(fs);
            win.Close();
        }
    }
}
