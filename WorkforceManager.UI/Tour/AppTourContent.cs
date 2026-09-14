namespace WorkforceManager.UI.Tour
{
    /// <summary>
    /// محتوى جولة "إيه الجديد" — بيتعرض مرة واحدة بس لكل قيمة من
    /// <see cref="Version"/> (شوف App.OfferAppTourIfNewAsync).
    ///
    /// **صيانة**: أي ميزة جديدة جاية تستاهل تتعلّم للمستخدم، لازم تتضاف
    /// خطوة هنا **و** يتزوّد <see cref="Version"/> — وإلا المستخدم اللي
    /// شاف نسخة قديمة مش هيشوف الجديد، لأن المقارنة بالنسخة بس.
    ///
    /// النسخة دي (1.6.5) بترجع لكل حاجة اتضافت من v1.5.6 لحد دلوقتي —
    /// مش رحلة زمنية بترجّع شاشات قديمة (معظمها مبقاش موجود أصلاً)، لكن
    /// خمس خطوات بتغطي الحاجات اللي المستخدم العادي ممكن يكون فاته.
    /// </summary>
    public static class AppTourContent
    {
        public const string Version = "1.6.5";

        public static IReadOnlyList<AppTourStep> Steps { get; } = new List<AppTourStep>
        {
            new()
            {
                Title = "بحث سريع",
                Description = "دوّر على أي عامل أو منتج من أي شاشة، من غير ما تفتح شاشته الأول.",
                TargetElementName = "GlobalSearchButton",
                Screen = TourScreen.None
            },
            new()
            {
                Title = "الذاكرة بقت بتفكّرك",
                Description = "الرقم الأحمر ده بيقولك كام خطة في \"الذاكرة\" مستحقة أو متأخرة، من غير ما تفتح الشاشة أصلًا.",
                TargetElementName = "MemoryBadge",
                Screen = TourScreen.None
            },
            new()
            {
                Title = "بحث في الذاكرة",
                Description = "دوّر باسم المنتج على طول لو عندك خطط كتير.",
                TargetElementName = "MemorySearchBox",
                Screen = TourScreen.Memory
            },
            new()
            {
                Title = "تحكّم أسرع في كل خطة",
                Description = "من كارت الخطة نفسه: ابدأها دلوقتي، أجّل التذكير، أو لو اتعلّمت \"منجزة\" غلط رجّعها نشطة تاني.",
                TargetElementName = "MemoryListsPanel",
                Screen = TourScreen.Memory
            },
            new()
            {
                Title = "تنقّل أسهل بين الأيام",
                Description = "يوم فات، يوم جاي، أو ارجع للنهارده بضغطة واحدة — من غير ما تفتح التقويم كل مرة.",
                TargetElementName = "DateNavPanel",
                Screen = TourScreen.DailyEntry
            }
        };
    }
}
