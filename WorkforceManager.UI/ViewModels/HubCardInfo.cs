using CommunityToolkit.Mvvm.ComponentModel;
using MaterialDesignThemes.Wpf;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// كارت واحد في صفحة كروت (إدارة العمال، تسجيل الإنتاج اليومي، الإعدادات): أيقونة + اسم + سطر شرح
    /// + رقم حي اختياري (StatText بيتملي بعد ما الصفحة تظهر — فاضي = مش بيتعرض).
    /// Key هو اللي HubLandingView.CardChosen بيرجّعه عشان الأب يعرف أنهي صفحة تتفتح.
    /// </summary>
    public partial class HubCardInfo : ObservableObject
    {
        public required string Key { get; init; }
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required PackIconKind Icon { get; init; }

        [ObservableProperty]
        private string _statText = string.Empty;
    }
}
