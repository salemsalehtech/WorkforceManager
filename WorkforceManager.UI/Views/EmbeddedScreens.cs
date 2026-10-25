using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.Services;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// شاشة أب بتعرض شاشات تانية جواها (صفحات كروت أو أقسام). MainWindow بتستخدمه عشان
    /// تفتح الشاشة المنقولة فعلًا من غير ما تعرف شكل الأب: <see cref="Show"/> بتفتح الصفحة
    /// وبتبني الشاشة المتداخلة فورًا (مش مستنية Loaded)، و<see cref="Find"/>
    /// بترجّعها لو اتبنت — ده اللي بيخلّي هبوط البحث السريع والجولة يشتغلوا زي الأول.
    /// </summary>
    public interface IEmbeddedScreenHost
    {
        /// <summary>يفتح الصفحة/القسم اللي فيه الشاشة دي ويبنيها. null لو الأب مش بيستضيفها.</summary>
        object? Show(Type screenType);

        /// <summary>الشاشة المتداخلة لو اتبنت قبل كده، من غير ما تبنيها. null غير كده.</summary>
        object? Find(Type screenType);

        /// <summary>عنصر بالاسم (x:Name) جوّه أي شاشة متداخلة اتبنت — الجولة بتدوّر بيه، لأن كل شاشة ليها FindName مستقل</summary>
        FrameworkElement? FindElement(string name);
    }

    /// <summary>شاشة ليها صفحة كروت جواها — الأب بيرجّعها للكروت لما المستخدم يدخلها من كارت</summary>
    public interface ILandingResettable
    {
        void ShowLanding();
    }

    /// <summary>
    /// شاشة ليها صفحات كروت جواها وبتتعرض جوّه أب تاني (التسجيل اليومي جوّه إدارة الإنتاج): الأب بيعرض
    /// شريط مسار واحد فوق الاتنين بدل شريطين فوق بعض، فالشاشة بتقوله أنهي صفحة داخلية مفتوحة وبتقفلها لما يطلب.
    /// </summary>
    public interface INestedLandingHost
    {
        /// <summary>عنوان الصفحة الداخلية المفتوحة، أو null لو الشاشة على كروتها</summary>
        string? OpenPageTitle { get; }

        /// <summary>بيتنادى كل ما الصفحة الداخلية تتفتح/تتقفل</summary>
        event Action? OpenPageChanged;

        /// <summary>يرجّع الشاشة لكروتها</summary>
        void CloseOpenPage();
    }

    /// <summary>
    /// حاوية بتبني شاشة من نطاق الجلسة (MainWindow.ResolveScreen) أول ما تتطلب — أول ما
    /// الأب يضغط <see cref="EnsureCreated"/> أو أول ما الحاوية تظهر. بتتحط جوّه صفحة/قسم
    /// فالشاشة المتداخلة متتبنيش لحد ما حد يفتحها فعلًا (كل شاشة بتحمّل بياناتها في Loaded).
    /// </summary>
    public sealed class LazyScreenHost : ContentControl
    {
        public Type? ScreenType { get; set; }

        public LazyScreenHost()
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            VerticalContentAlignment = VerticalAlignment.Stretch;
            Loaded += (_, _) => EnsureCreated();
        }

        public object? EnsureCreated()
        {
            if (Content is null && ScreenType is not null && MainWindow.Current is { } window)
                Content = window.ResolveScreen(ScreenType);
            return Content;
        }
    }

    /// <summary>
    /// شاشة أب بتفتح على صفحة كروت (كارت لكل صفحة)، والدوسة على كارت بتفتح الصفحة بشريط رجوع فوقها.
    /// كل صفحة شاشة كاملة موجودة أصلًا — الأب بيستضيفها بس (LazyScreenHost) ومفيهوش منطق شغل.
    /// الصفحات المفتوحة بتفضل عايشة لحد ما الأب نفسه يتقفل (الرجوع للكروت بيخبّيها بس)، فاللي لسه
    /// مش محفوظ جواها مابيضيعش.
    /// </summary>
    public abstract class CardHubHost : UserControl, IEmbeddedScreenHost, IScreenShortcuts
    {
        /// <summary>صفحة واحدة في الأب: الشاشة، واسمها وشرحها وأيقونتها على الكارت</summary>
        protected sealed record HubPage(Type Screen, string Title, string Description, PackIconKind Icon);

        private readonly IServiceScopeFactory? _scopeFactory;
        private readonly string _hubTitle;
        private readonly HubLandingView _landing = new();
        private readonly HubPageBar _bar = new();
        private readonly Grid _pagesLayer = new() { Visibility = Visibility.Collapsed };
        private readonly List<(HubPage Page, HubCardInfo Card, LazyScreenHost Host)> _pages = new();

        protected CardHubHost(IServiceScopeFactory? scopeFactory, string title, string subtitle, params HubPage[] pages)
        {
            _scopeFactory = scopeFactory;
            _hubTitle = title;

            _pagesLayer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _pagesLayer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(_bar, 0);
            _pagesLayer.Children.Add(_bar);

            foreach (var page in pages)
            {
                var host = new LazyScreenHost { ScreenType = page.Screen, Visibility = Visibility.Collapsed };
                Grid.SetRow(host, 1);
                _pagesLayer.Children.Add(host);

                var card = new HubCardInfo
                {
                    Key = page.Screen.FullName!,
                    Title = page.Title,
                    Description = page.Description,
                    Icon = page.Icon
                };
                _pages.Add((page, card, host));
            }

            _landing.Setup(title, subtitle, _pages.Select(p => p.Card).ToList());
            _landing.CardChosen += card =>
            {
                // دخول من الكارت = الصفحة من أولها: لو الصفحة نفسها ليها صفحة كروت جواها (التسجيل اليومي)
                // ترجع لكروتها، عكس الفتح البرمجي (Show) اللي بيروح لمكان معيّن
                (OpenPage(_pages.FindIndex(p => p.Card == card)) as ILandingResettable)?.ShowLanding();
            };
            _bar.BackRequested += GoBack;

            var root = new Grid();
            root.Children.Add(_landing);
            root.Children.Add(_pagesLayer);
            Content = root;

            Loaded += async (_, _) => await RefreshStatsAsync();
        }

        /// <summary>يملّي الأرقام الحية على الكروت — كل أب بيعرف أرقامه (StatFor بترجّع null = مفيش رقم)</summary>
        protected abstract Task LoadStatsAsync(IServiceProvider services, Action<Type, string> setStat);

        private async Task RefreshStatsAsync()
        {
            if (_scopeFactory is null) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                await LoadStatsAsync(scope.ServiceProvider, (screen, text) =>
                {
                    var entry = _pages.FirstOrDefault(p => p.Page.Screen == screen);
                    if (entry.Card is not null) entry.Card.StatText = text;
                });
            }
            catch
            {
                // الأرقام زينة على الكروت — فشل تحميلها مايمنعش فتح أي صفحة، والكارت بيفضل من غير رقم
            }
        }

        public void ShowLanding()
        {
            _pagesLayer.Visibility = Visibility.Collapsed;
            _landing.Visibility = Visibility.Visible;
            _ = RefreshStatsAsync();
        }

        private object? OpenPage(int index)
        {
            if (index < 0) return null;

            _landing.Visibility = Visibility.Collapsed;
            _pagesLayer.Visibility = Visibility.Visible;
            for (var i = 0; i < _pages.Count; i++)
                _pages[i].Host.Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;

            _currentIndex = index;
            var content = _pages[index].Host.EnsureCreated();

            // صفحة ليها كروت جواها (التسجيل اليومي): نسمع تنقّلها الداخلي عشان المسار يتحدّث على نفس السطر
            if (content is INestedLandingHost nested && _hookedNested.Add(nested))
                nested.OpenPageChanged += RefreshBar;

            RefreshBar();
            return content;
        }

        private int _currentIndex = -1;
        private readonly HashSet<INestedLandingHost> _hookedNested = new();

        /// <summary>المسار في سطر واحد: الشاشة ← الصفحة ← (الصفحة الداخلية لو مفتوحة)</summary>
        private void RefreshBar()
        {
            if (_currentIndex < 0) return;

            var nested = _pages[_currentIndex].Host.Content as INestedLandingHost;
            _bar.Set(_hubTitle, _pages[_currentIndex].Page.Title, nested?.OpenPageTitle);
        }

        /// <summary>رجوع خطوة واحدة: لو الصفحة ليها صفحة داخلية مفتوحة بترجع لكروتها الأول، وإلا لكروت الأب</summary>
        private void GoBack()
        {
            if (_currentIndex >= 0 &&
                _pages[_currentIndex].Host.Content is INestedLandingHost { OpenPageTitle: not null } nested)
            {
                nested.CloseOpenPage();
                return;
            }

            ShowLanding();
        }

        /// <summary>
        /// شاشة بتتفتح كصفحة جوّه صفحة تانية من صفحات الأب (مثلًا الذاكرة جوّه التسجيل اليومي):
        /// بيرجّع الصفحة اللي هي فيها، أو null لو الشاشة دي صفحة مباشرة (أو مش بتاعت الأب).
        /// </summary>
        protected virtual Type? OwnerPageOf(Type screenType) => null;

        public object? Show(Type screenType)
        {
            var direct = _pages.FindIndex(p => p.Page.Screen == screenType);
            if (direct >= 0) return OpenPage(direct);

            if (OwnerPageOf(screenType) is not { } owner) return null;

            var content = Show(owner);
            return (content as IEmbeddedScreenHost)?.Show(screenType);
        }

        public object? Find(Type screenType)
        {
            var direct = _pages.FirstOrDefault(p => p.Page.Screen == screenType).Host?.Content;
            if (direct is not null) return direct;

            return _pages.Select(p => (p.Host.Content as IEmbeddedScreenHost)?.Find(screenType))
                         .FirstOrDefault(c => c is not null);
        }

        public FrameworkElement? FindElement(string name) =>
            _pages.Select(p => (p.Host.Content as FrameworkElement)?.FindName(name) as FrameworkElement
                               ?? (p.Host.Content as IEmbeddedScreenHost)?.FindElement(name))
                  .FirstOrDefault(e => e is not null);

        // اختصارات Ctrl+N/S/F بتتوجّه للصفحة المفتوحة دلوقتي (لو شاشتها بتدعمها)
        private IScreenShortcuts? Active =>
            _pagesLayer.Visibility == Visibility.Visible
                ? _pages.FirstOrDefault(p => p.Host.Visibility == Visibility.Visible).Host?.Content as IScreenShortcuts
                : null;

        bool IScreenShortcuts.TryQuickAdd() => Active?.TryQuickAdd() ?? false;
        bool IScreenShortcuts.TrySave() => Active?.TrySave() ?? false;
        bool IScreenShortcuts.TryFocusSearch() => Active?.TryFocusSearch() ?? false;
    }

    /// <summary>
    /// إدارة الإنتاج: المنتجات والمراحل، الخطة الشهرية، تسجيل الإنتاج اليومي (وجواه الذاكرة والرصيد الأولي
    /// والهالك). كارت التسجيل اليومي بيفتح صفحة كروت تانية جواه — مستويين كروت عن قصد، لأن التسجيل
    /// اليومي شاشة واحدة بـ view model واحد مش خمس شاشات منفصلة.
    /// </summary>
    public sealed class ProductionHubView : CardHubHost
    {
        public ProductionHubView(IServiceScopeFactory scopeFactory) : base(
            scopeFactory,
            "إدارة الإنتاج",
            "المنتجات والتخطيط والتسجيل اليومي — اختار اللي عايزه",
            new HubPage(typeof(ProductsView), "المنتجات والمراحل", "المنتجات ومراحل تصنيع كل منتج", PackIconKind.PackageVariantClosed),
            new HubPage(typeof(MonthlyPlanView), "الإنتاج الشهري للمنتجات", "المطلوب والمحقق لكل منتج خلال الشهر، والمتأخر منها", PackIconKind.ChartLine),
            new HubPage(typeof(DailyEntryView), "تسجيل يوميات العمال", "إنتاج كل عامل في اليوم، والرصيد الأولي والخطط المحفوظة والهالك", PackIconKind.AccountClockOutline))
        {
        }

        protected override Type? OwnerPageOf(Type screenType) =>
            screenType == typeof(MemoryView) ? typeof(DailyEntryView) : null;

        protected override async Task LoadStatsAsync(IServiceProvider services, Action<Type, string> setStat)
        {
            var stats = await services.GetRequiredService<HubStatsService>().GetProductionStatsAsync(DateTime.Today);

            setStat(typeof(ProductsView), $"{stats.ActiveProducts} منتج نشط");
            setStat(typeof(MonthlyPlanView), stats.MonthlyPlanAchievedPercent is { } pct ? $"المحقق {pct:0}% من الخطة" : "مفيش خطة الشهر ده لسه");
            setStat(typeof(DailyEntryView), stats.ProductionRecordsToday == 0 ? "لسه مفيش تسجيل النهارده" : $"{stats.ProductionRecordsToday} سجل النهارده");
        }
    }

    /// <summary>
    /// إدارة العمال: العمال، الحضور والغياب، الجزاءات، السلف والحوافز (كانوا في التسجيل اليومي)،
    /// تقييم ومتابعة العمال، وتقارير العمال — كروت بأرقام النهارده.
    /// </summary>
    public sealed class WorkersHubView : CardHubHost
    {
        public WorkersHubView(IServiceScopeFactory scopeFactory) : base(
            scopeFactory,
            "إدارة العمال",
            "كل حاجة تخص العمال في مكان واحد — اختار اللي عايزه",
            new HubPage(typeof(WorkersView), "العمال", "قايمة العمال وملفاتهم ومهاراتهم", PackIconKind.AccountGroup),
            new HubPage(typeof(AttendanceView), "الحضور والغياب", "تسجيل حضور وغياب العمال لليوم", PackIconKind.CalendarCheck),
            new HubPage(typeof(PenaltiesView), "الجزاءات", "تسجيل الجزاءات والخصومات", PackIconKind.Gavel),
            new HubPage(typeof(AdjustmentsView), "السلف والحوافز", "السلف والحوافز اللي بتتحسب على الأجر", PackIconKind.CashMultiple),
            new HubPage(typeof(ReportsView), "تقييم ومتابعة العمال", "لوحة حيّة بإنتاج العمال وأدائهم", PackIconKind.ChartBar),
            new HubPage(typeof(ReportBuilderView), "تقارير العمال", "تقارير الأجور والإنتاج للطباعة والتصدير", PackIconKind.FileDocumentOutline))
        {
        }

        protected override async Task LoadStatsAsync(IServiceProvider services, Action<Type, string> setStat)
        {
            var stats = await services.GetRequiredService<HubStatsService>().GetWorkersStatsAsync(DateTime.Today);

            setStat(typeof(WorkersView), $"{stats.ActiveWorkers} عامل نشط");
            setStat(typeof(AttendanceView), stats.PresentToday + stats.AbsentToday == 0
                ? "لسه متسجّلش النهارده"
                : $"{stats.PresentToday} حاضر · {stats.AbsentToday} غايب النهارده");
            setStat(typeof(PenaltiesView), stats.PenaltiesToday == 0 ? "مفيش جزاءات النهارده" : $"{stats.PenaltiesToday} جزاء النهارده");
            setStat(typeof(AdjustmentsView), stats.AdjustmentsToday == 0 ? "مفيش سلف/حوافز النهارده" : $"{stats.AdjustmentsToday} سلفة/حافز النهارده");
        }
    }
}
