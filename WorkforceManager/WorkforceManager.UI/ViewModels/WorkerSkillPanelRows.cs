using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Helpers;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Data;
using WorkforceManager.UI.Views;

namespace WorkforceManager.UI.ViewModels
{
    // لوحة بروفايل العامل: بياناته وكروت مهاراته على كل منتج ومراحله.
    // WorkerDetail هي الجذر، وجوّاها SkillProductGroup لكل منتج، وجوّاها
    // SkillStageItem لكل مرحلة في خط المنتج.

    /// <summary>تفاصيل العامل المعروضة في اللوحة الجانبية (البروفايل)</summary>
    public partial class WorkerDetail : ObservableObject
    {
        public int WorkerId { get; init; }
        public string FullName { get; init; } = "";
        public string PhoneNumber { get; init; } = "";
        public string HireDateText { get; init; } = "";
        public bool IsActive { get; init; }

        /// <summary>صورة العامل (null = تتعرض الحروف الأولى بدلها)</summary>
        public byte[]? PhotoData { get; init; }

        /// <summary>دور العامل بالساعة (null = عامل إنتاج بيتحاسب على إنتاجه)</summary>
        public Core.Enums.HourlyRole? HourlyRole { get; init; }

        /// <summary>نص الدور بالساعة للعرض في البروفايل (فاضي لعامل الإنتاج)</summary>
        public string HourlyRoleText { get; init; } = "";

        /// <summary>سعر يومية العامل بالجنيه</summary>
        public decimal DailyWageEgp { get; init; }

        /// <summary>نص سعر اليومية للعرض في البروفايل</summary>
        public string WageText { get; init; } = "";

        /// <summary>مفيش سعر يومية — بيلوّن الشارة تحذير بدل أخضر</summary>
        public bool HasNoWage => DailyWageEgp <= 0;

        /// <summary>هل هو عامل بالساعة؟ (لإظهار شارة في البروفايل)</summary>
        public bool IsHourly => HourlyRole is not null;

        /// <summary>مهارات العامل مجمّعة في كارت لكل منتج (مرتبة بالتغطية)</summary>
        public ObservableCollection<SkillProductGroup> SkillProducts { get; init; } = new();

        public ObservableCollection<WeekHistoryItem> WeeklyHistory { get; init; } = new();

        /// <summary>
        /// كل كروت المنتجات (بما فيها اللي العامل مالوش فيها ولا مهارة).
        /// SkillProducts فوق هي العرض المفلتر منها حسب الوضع الحالي.
        /// </summary>
        public List<SkillProductGroup> AllGroups { get; init; } = new();

        /// <summary>
        /// وضع الإضافة. مفيش فورم منفصل: نفس الكروت بالظبط، بس بتتوسّع لتشمل
        /// المنتجات اللي العامل مالوش فيها مهارة (بتبان 0 / 11) — يفتح المنتج
        /// ويضيف مراحله من نفس الكارت اللي اتعوّد عليه.
        /// </summary>
        [ObservableProperty]
        private bool _isAddingSkills;

        partial void OnIsAddingSkillsChanged(bool value)
        {
            ApplyGroupMode();
            OnPropertyChanged(nameof(SkillSearchHint));
        }

        public void ApplyGroupMode()
        {
            SkillProducts.Clear();

            foreach (var group in AllGroups)
            {
                var known = group.KnownCount > 0 || group.InactiveSkillCount > 0;

                // وضع الإضافة بيضيف المنتجات النشطة اللي لسه مالوش فيها حاجة.
                // منتج موقوف ومالوش فيه مهارة مبيظهرش أبدًا — مينفعش يشتغل عليه
                var show = IsAddingSkills ? known || !group.IsProductInactive : known;
                if (show) SkillProducts.Add(group);

                // في وضع الإضافة النجوم بتبان على المراحل اللي لسه مش
                // مضافة كمان: الضغط على نجمة بيضيف المهارة بالتقييم ده
                // في حركة واحدة، بدل "ضيف" وبعدين "قيّم"
                foreach (var stage in group.Stages) stage.IsAddMode = IsAddingSkills;
            }

            ApplySkillFilter();
            OnPropertyChanged(nameof(HasSkills));
            RefreshExpandState();
        }

        /// <summary>فيه كروت معروضة دلوقتي؟ (شريط البحث بيظهر بيها)</summary>
        public bool HasSkills => SkillProducts.Count > 0;

        /// <summary>العامل مالوش ولا مهارة خالص — تحذير مختلف عن "البحث مالوش نتيجة"</summary>
        public bool HasAnySkill => AllGroups.Any(g => g.KnownCount > 0 || g.InactiveSkillCount > 0);

        /// <summary>نص خانة البحث بيتغير حسب الوضع</summary>
        public string SkillSearchHint => IsAddingSkills ? "دوّر على منتج أو مرحلة…" : "دوّر في مهاراته…";

        // ------- البحث جوّه مهارات العامل نفسه -------

        /// <summary>
        /// بحث في كروت المهارات. مع ٦٩ مهارة موزعة على منتجات كتير، الوصول
        /// لمرحلة معينة بالتمرير بطيء. البحث بيطابق اسم المنتج أو اسم المرحلة،
        /// وبيفتح الكروت المطابقة تلقائيًا عشان النتيجة تبان من غير دوسة زيادة.
        /// </summary>
        [ObservableProperty]
        private string _skillSearch = string.Empty;

        partial void OnSkillSearchChanged(string value) => ApplySkillFilter();

        public void ApplySkillFilter()
        {
            var query = SkillSearch?.Trim() ?? "";

            foreach (var group in SkillProducts)
            {
                if (query.Length == 0)
                {
                    foreach (var stage in group.Stages) stage.IsVisible = true;
                    group.IsVisible = true;
                    group.IsExpanded = false; // البحث الفاضي بيرجّع اللوحة لحالتها المرتبة
                    continue;
                }

                var productMatches = ArabicSearch.Contains(group.ProductName, query);

                // منتج مطابق بالاسم = كل مراحله تبان، مش المطابقة منها بس
                foreach (var stage in group.Stages)
                    stage.IsVisible = productMatches || ArabicSearch.Contains(stage.StageName, query);

                group.IsVisible = productMatches || group.Stages.Any(s => s.IsVisible);
                group.IsExpanded = group.IsVisible;
            }

            OnPropertyChanged(nameof(HasVisibleSkills));
        }

        /// <summary>البحث مالوش نتيجة — الفرق بين "مفيش مهارات" و"مفيش نتيجة"</summary>
        public bool HasVisibleSkills => SkillProducts.Any(g => g.IsVisible);

        /// <summary>كل الكروت مفتوحة دلوقتي؟ (بيقلب نص وأيقونة زرار فتح/قفل الكل)</summary>
        public bool AllExpanded => SkillProducts.Count > 0 && SkillProducts.All(g => g.IsExpanded);

        public void RefreshExpandState() => OnPropertyChanged(nameof(AllExpanded));

        /// <summary>
        /// بيحدّث عدّادات كارت المنتج اللي المرحلة دي تبعه، بعد إضافة
        /// مهارة من غير إعادة تحميل البروفايل — عشان الكارت المفتوح
        /// والبحث الجاري ميضيعوش من تحت إيد المستخدم.
        /// </summary>
        public void RefreshCoverage(int stageId)
        {
            var group = SkillProducts.FirstOrDefault(g => g.Stages.Any(s => s.StageId == stageId));
            group?.RefreshCounters();

            OnPropertyChanged(nameof(HasAnySkill));
        }
    }

    /// <summary>
    /// كارت منتج واحد في لوحة مهارات العامل. العامل ممكن يكون عنده ٦٩ مهارة،
    /// وعرضها كقائمة مسطّحة بيخلي البروفايل جدار نص مقروش. الكارت بيتقفل على
    /// اسم المنتج وتغطيته، وبيتفتح على مراحل الخط بترتيبها.
    /// </summary>
    public partial class SkillProductGroup : ObservableObject
    {
        public int ProductId { get; init; }
        public string ProductName { get; init; } = "";

        /// <summary>المنتج نفسه موقوف؟ (مهاراته باقية بس مش هتشتغل)</summary>
        public bool IsProductInactive { get; init; }

        /// <summary>كل مراحل الخط — اللي بيعرفها واللي لأ، بترتيب الخط</summary>
        public ObservableCollection<SkillStageItem> Stages { get; init; } = new();

        /// <summary>
        /// التغطية بتتحسب على المراحل النشطة بس. مرحلة موقوفة مش بتزوّد
        /// التغطية ولا بتنقّصها — هي خارج الحساب أصلاً عشان متديش إحساس
        /// كاذب إن العامل مغطي الخط.
        /// </summary>
        public int KnownCount => Stages.Count(s => s.IsKnown && !s.IsStageInactive);
        public int ActiveCount => Stages.Count(s => !s.IsStageInactive);

        public string CoverageText => $"{RtlSafeText.Ratio(KnownCount, ActiveCount)} مرحلة";

        /// <summary>
        /// شرح شارة التغطية. بتحمل رسالة نجمة "بيغطي الخط كله" اللي اتشالت
        /// من جنب اسم المنتج — الشارة بتخضرّ في نفس الحالة، فالأيقونة كانت
        /// بتقول نفس الكلام مرتين وبتاخد مساحة.
        /// </summary>
        public string CoverageTooltip => CoversWholeLine
            ? $"بيعرف كل مراحل {ProductName} — يقدر يمسك المنتج لوحده"
            : $"بيعرف {KnownCount} من {ActiveCount} مرحلة في {ProductName}";

        // ------- تقييم العامل على المنتج ده (بيتعرض على رأس الكارت) -------

        /// <summary>
        /// متوسط نجومه على مراحل المنتج **اللي بيعرفها**.
        ///
        /// الحساب بيتنادى من <see cref="SkillRatingService.ProductStars"/> مش
        /// محلي هنا: القاعدة (المراحل اللي مالوش فيها مهارة مبتتحسبش صفر،
        /// عشان المتخصص في 3 مراحل من 11 ميبانش ضعيف) عايشة في مكان واحد.
        ///
        /// النتيجة متخزّنة لأن ست خصائص معروضة بتقرا منها، ومن غير التخزين
        /// كانت هتتحسب من الأول مع كل رسمة للكارت. بتتصفّر في
        /// <see cref="RefreshRating"/> بعد أي تعديل مهارة أو نجوم.
        ///
        /// null = مالوش ولا مهارة في المنتج ده، فمفيش تقييم يتعرض.
        /// </summary>
        public decimal? AverageStars => _averageStars ??= SkillRatingService.ProductStars(
            Stages.Where(s => s.IsKnown)
                  .Select(s => new Core.Models.WorkerSkill { Stars = s.Stars }));

        private decimal? _averageStars;

        public bool HasRating => AverageStars is not null;

        /// <summary>المتوسط مقرّب لأقرب نجمة للعرض</summary>
        public int RoundedStars => AverageStars is null
            ? 0
            : Math.Clamp((int)Math.Round(AverageStars.Value, MidpointRounding.AwayFromZero), 1, 5);

        public string StarsText => HasRating
            ? RtlSafeText.Stars(RoundedStars)
            : "";

        /// <summary>"ممتاز" / "كويس جدًا" / … — النص من SkillRatingService</summary>
        public string RatingLabel => HasRating ? SkillRatingService.StarsLabel(RoundedStars) : "";

        /// <summary>
        /// مفتاح فرشاة الشارة حسب المستوى — مش كود لون (شوف
        /// <see cref="ThemeBrush"/>). اللون بيخلي المستوى يتقري من غير
        /// ما العين تعدّ نجوم.
        ///
        /// الممتاز والكويس جدًا الاتنين دهبي عن قصد: الدهبي هو "أيوه"
        /// في الهوية دي، والفرق بينهم النجوم نفسها. اللي كان بيفرّقهم
        /// قبل كده درجتين أخضر متقاربين — فرق محدش كان بيشوفه.
        /// </summary>
        public string RatingColor => RoundedStars switch
        {
            5 => "GoodBrush",      // ممتاز
            4 => "GoldDeepBrush",  // كويس جدًا
            3 => "InkSoftBrush",   // عادي
            2 => "WarnBrush",      // ضعيف
            _ => "DangerBrush"     // ضعيف جدًا
        };

        public string RatingBackground => RoundedStars switch
        {
            5 => "GoodTintBrush",
            4 => "GoldTintBrush",
            3 => "SurfaceAltBrush",
            2 => "WarnTintBrush",
            _ => "DangerTintBrush"
        };

        /// <summary>شرح الشارة — من غيره الرقم مالوش سياق</summary>
        public string RatingTooltip => HasRating
            ? $"متوسط تقييمك على {KnownCount} مرحلة في {ProductName}: {RatingLabel} ({AverageStars:0.#}/5)"
            : "";

        private void RefreshRating()
        {
            _averageStars = null; // يتحسب من الأول عند أول قراءة جاية

            OnPropertyChanged(nameof(AverageStars));
            OnPropertyChanged(nameof(HasRating));
            OnPropertyChanged(nameof(RoundedStars));
            OnPropertyChanged(nameof(StarsText));
            OnPropertyChanged(nameof(RatingLabel));
            OnPropertyChanged(nameof(RatingColor));
            OnPropertyChanged(nameof(RatingBackground));
            OnPropertyChanged(nameof(RatingTooltip));
        }

        /// <summary>بيعرف كل مراحل الخط النشطة — يقدر يمسك المنتج لوحده</summary>
        public bool CoversWholeLine => ActiveCount > 0 && KnownCount == ActiveCount;

        /// <summary>عنده مهارة على مرحلة موقوفة — بتتعلّم بعلامة تحذير</summary>
        public int InactiveSkillCount => Stages.Count(s => s.IsKnown && s.IsStageInactive);
        public bool HasInactiveSkills => InactiveSkillCount > 0;
        public string InactiveSkillsText => $"{InactiveSkillCount} مرحلة موقوفة";

        /// <summary>مراحل نشطة مش بيعرفها — بيظهر زرار "ضيفهم كلهم" لما تبقى موجودة</summary>
        public int MissingCount => Stages.Count(s => !s.IsKnown && !s.IsStageInactive);
        public bool HasMissing => MissingCount > 0;
        public string AddAllText => $"ضيف الـ {MissingCount} مرحلة الناقصة";

        /// <summary>مالوش ولا مهارة في المنتج ده (بيبان في وضع الإضافة بس)</summary>
        public bool IsUntouched => KnownCount == 0 && InactiveSkillCount == 0;

        [ObservableProperty]
        private bool _isExpanded;

        /// <summary>مطابق للبحث دلوقتي؟ (البحث في مهارات العامل نفسه)</summary>
        [ObservableProperty]
        private bool _isVisible = true;

        /// <summary>بيتنادى بعد أي إضافة/إزالة مهارة عشان الأرقام على الكارت تتحدث</summary>
        public void RefreshCounters()
        {
            OnPropertyChanged(nameof(KnownCount));
            OnPropertyChanged(nameof(ActiveCount));
            OnPropertyChanged(nameof(CoverageText));
            OnPropertyChanged(nameof(CoverageTooltip));
            OnPropertyChanged(nameof(CoversWholeLine));
            OnPropertyChanged(nameof(InactiveSkillCount));
            OnPropertyChanged(nameof(HasInactiveSkills));
            OnPropertyChanged(nameof(InactiveSkillsText));
            OnPropertyChanged(nameof(MissingCount));
            OnPropertyChanged(nameof(HasMissing));
            OnPropertyChanged(nameof(AddAllText));
            OnPropertyChanged(nameof(IsUntouched));

            // إضافة/إزالة مهارة بتغيّر المتوسط، والشارة على رأس الكارت
            // لازم تتحدّث معاها
            RefreshRating();
        }
    }

    /// <summary>
    /// مرحلة واحدة جوّه كارت المنتج. بتتعرض حتى لو العامل مش بيعرفها —
    /// الفجوة نفسها معلومة مفيدة، والزرار اللي جنبها بيسدّها في مكانها.
    /// </summary>
    public partial class SkillStageItem : ObservableObject
    {
        public int StageId { get; init; }
        public int ProductId { get; init; }
        public string StageName { get; init; } = "";

        /// <summary>ترتيب المرحلة في خط الإنتاج (نفس الترتيب في شاشة المنتجات)</summary>
        public int Position { get; init; }

        /// <summary>المرحلة موقوفة — المهارة عليها مش هتنفع في أي رحلة إنتاج</summary>
        public bool IsStageInactive { get; init; }

        /// <summary>العامل بيعرف المرحلة دي؟ (بيتقلب بزرار جنبها)</summary>
        [ObservableProperty]
        private bool _isKnown;

        partial void OnIsKnownChanged(bool value)
        {
            OnPropertyChanged(nameof(ShowStars));
            OnPropertyChanged(nameof(RatingTooltip));
            RefreshStarFlags();
        }

        /// <summary>اللوحة في وضع "ضيف مهارات" دلوقتي؟</summary>
        [ObservableProperty]
        private bool _isAddMode;

        partial void OnIsAddModeChanged(bool value) => OnPropertyChanged(nameof(ShowStars));

        /// <summary>
        /// النجوم بتبان للمهارات اللي بيعرفها (عشان يعدّل تقييمه)، وكمان
        /// في وضع الإضافة للمراحل اللي لسه مش مضافة — وساعتها الضغط على
        /// نجمة بيضيف المهارة بالتقييم ده على طول.
        /// </summary>
        public bool ShowStars => IsKnown || IsAddMode;

        /// <summary>مطابق للبحث دلوقتي؟</summary>
        [ObservableProperty]
        private bool _isVisible = true;

        // ------- التقييم -------

        /// <summary>تقييم المدير من 1 لـ 5 — بيتغيّر بالضغط على النجمة</summary>
        [ObservableProperty]
        private int _stars = SkillRatingService.DefaultStars;

        /// <summary>إنتاجه الفعلي ÷ الكوتة (0 = لسه مافيش قياس)</summary>
        public decimal MeasuredRatio { get; set; }

        /// <summary>عدد أيام الشغل اللي القياس اتبنى عليها</summary>
        public int MeasuredDays { get; set; }

        partial void OnStarsChanged(int value)
        {
            OnPropertyChanged(nameof(StarsLabel));
            OnPropertyChanged(nameof(RatingTooltip));
            OnPropertyChanged(nameof(HasGapWithReality));
            OnPropertyChanged(nameof(StarsPercentText));
            RefreshStarFlags();
        }

        /// <summary>وصف التقييم بالعربي (ممتاز / كويس جدًا / عادي ...)</summary>
        public string StarsLabel => SkillRatingService.StarsLabel(Stars);

        /// <summary>فيه قياس فعلي؟</summary>
        public bool HasMeasurement => MeasuredDays > 0;

        /// <summary>الأداء المقاس كنسبة ("115%")</summary>
        public string MeasuredText => HasMeasurement ? $"{MeasuredRatio * 100:0}%" : "";

        /// <summary>نسبة تقييم النجوم = (النجوم ÷ 5) × 100 — موجودة دايمًا، من رأي المدير مباشرة</summary>
        public string StarsPercentText => $"{Math.Round(Stars / 5m * 100m):0}%";

        /// <summary>
        /// تقييم المدير بعيد عن الأداء الفعلي — بيتعلّم عشان يراجعه.
        /// ده اللي بيخلي المدير يشوف الفجوة من غير ما يفتح شاشة المراجعة.
        /// </summary>
        public bool HasGapWithReality =>
            HasMeasurement && SkillRatingService.StarsForRatio(MeasuredRatio) != Stars;

        public string RatingTooltip =>
            !IsKnown
                ? "دوس على عدد النجوم اللي شايفه — هيتضاف للعامل بالمستوى ده"
                : HasMeasurement
                    ? $"تقييمك: {StarsLabel} ({Stars}/5)\nإنتاجه الفعلي: {MeasuredText} من الكوتة على مدار {MeasuredDays} يوم"
                    : $"تقييمك: {StarsLabel} ({Stars}/5)\nلسه مافيش إنتاج كفاية للقياس";

        // ------- حالة كل نجمة (للعرض والضغط) -------
        // خمس خصائص منفصلة عشان الـ XAML يربط عليها مباشرة من غير
        // محوّلات ولا قوايم متداخلة

        // مرحلة لسه مش مضافة بتتعرض بنجوم فاضية كلها: القيمة الافتراضية
        // (3) هي مبدئية مش تقييم، وعرضها مليانة كان هيوحي إن المدير قيّمها
        public bool Star1 => IsKnown && Stars >= 1;
        public bool Star2 => IsKnown && Stars >= 2;
        public bool Star3 => IsKnown && Stars >= 3;
        public bool Star4 => IsKnown && Stars >= 4;
        public bool Star5 => IsKnown && Stars >= 5;

        // معامل الأمر جاهز كنص من هنا مش من StringFormat في الـ XAML:
        // StringFormat على CommandParameter مبيحوّلش فعليًا — WPF بيبعت
        // الرقم زي ما هو والأمر اللي بياخد string بيرفضه، فالضغطة كانت
        // بترمي استثناء بدل ما تشتغل
        public string Star1Param => $"{StageId}:1";
        public string Star2Param => $"{StageId}:2";
        public string Star3Param => $"{StageId}:3";
        public string Star4Param => $"{StageId}:4";
        public string Star5Param => $"{StageId}:5";

        private void RefreshStarFlags()
        {
            OnPropertyChanged(nameof(Star1));
            OnPropertyChanged(nameof(Star2));
            OnPropertyChanged(nameof(Star3));
            OnPropertyChanged(nameof(Star4));
            OnPropertyChanged(nameof(Star5));
        }
    }

    /// <summary>
    /// كارت أسبوع واحد في هستوري العامل. مقفول بيوري الرقمين اللي بيتسألوا
    /// عنهم فعلاً (الصافي والأجر)، وبيتفتح على التفاصيل كصفوف — قبل كده كانت
    /// التفاصيل سطر نص واحد بيلف ("GRS/دبله: 9999 قطعة، GRS/رقبه: ...")
    /// ومستحيل تقرا منه رقم مرحلة معينة.
    /// </summary>
}
