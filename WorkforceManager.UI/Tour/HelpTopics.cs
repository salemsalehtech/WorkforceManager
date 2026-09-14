using System.Linq;

namespace WorkforceManager.UI.Tour
{
    /// <summary>
    /// محتوى شاشة "الدليل" — موضوع واحد لكل شاشة في القائمة الجانبية،
    /// بنفس ترتيبها بالظبط (<see cref="Topics"/>، 9 عناصر). "تسجيل الإنتاج
    /// اليومي" وحده بيحمل <see cref="HelpTopic.SubTopics"/> — 7 مواضيع
    /// متداخلة، واحد لكل تبويب داخلي، فيه تفاصيل كتير جدًا عشان يتغطّى في
    /// موضوع واحد بمعنى حقيقي.
    ///
    /// كل خطوة مبنية على عنصر وميزة حقيقيين في الكود (أمر/Binding حقيقي)،
    /// والوصف فيه مثال ملموس مش جملة عامة — "مثلاً: اختار أحمد، اكتب نص
    /// يوم، السبب..." بدل "سجّل جزاء على عامل".
    ///
    /// **صيانة**: أي شاشة جديدة تتضاف للبرنامج لازم تاخد موضوع في
    /// <see cref="Topics"/> (أو مواضيع فرعية في <see cref="HelpTopic.SubTopics"/>
    /// لو زيها في التعقيد) — وإلا الدليل يبقى ناقص من غير ما حد ياخد باله،
    /// لأن مفيش فحص آلي بيقارن عدد عناصر القائمة الجانبية بعدد المواضيع هنا.
    /// </summary>
    public static class HelpTopics
    {
        public static IReadOnlyList<HelpTopic> Topics { get; } = new List<HelpTopic>
        {
            new()
            {
                Title = "العمال",
                Icon = "AccountGroup",
                Description =
                    "قايمة كل العمال ببطاقات: صورة، حالة، مهارات. بحث فوري بالاسم، وزرار \"فلاتر " +
                    "وترتيب\" بيجمع فلاتر بتتراكب مع بعض (المرحلة، المنتج، الحد الأدنى للنجوم، " +
                    "حضور النهارده) بدل صف تانٍ من الأزرار. كارت \"أفضل عامل الأسبوع\" بيتحسب " +
                    "لوحده من إنتاج الأسبوع. من هنا كمان بتضيف عامل جديد، تعدّل بياناته، تفعّل/توقف " +
                    "حسابه، أو تراجع تقييمات مهاراته على كل مرحلة.",
                TourSteps = new List<AppTourStep>
                {
                    new()
                    {
                        Title = "فلاتر وترتيب",
                        Description =
                            "بدل صف أزرار تاني، فلاتر المرحلة والمنتج والنجوم والحضور كلها هنا وبتتراكب مع بعض. مثلاً: " +
                            "عايز تشوف عمال \"الخياطة\" بس اللي تقييمهم 4 نجوم فأكتر وحاضرين النهارده — دوس الزرار، " +
                            "اختار الثلاثة فلاتر مع بعض، والقايمة بتتظبط فورًا.",
                        TargetElementName = "FilterToggle",
                        Screen = TourScreen.Workers
                    },
                    new()
                    {
                        Title = "فلاتر سريعة",
                        Description =
                            "جنب زرار \"فلاتر وترتيب\"، 4 شرايط بضغطة واحدة: الكل، بالإنتاج (عمال بالقطعة)، بالساعة، " +
                            "وموقوفين — أسرع من فتح نافذة الفلاتر لو كل اللي عايزه تفصل نوع عمال عن نوع.",
                        TargetElementName = "QuickFilterChipsPanel",
                        Screen = TourScreen.Workers
                    },
                    new()
                    {
                        Title = "عامل جديد",
                        Description =
                            "إضافة عامل جديد بصورته وبياناته الأساسية. اكتب الاسم وسعر اليومية، ارفع صورة لو عندك، واحفظ " +
                            "— بعدها تقدر تضيفله مهارات على مراحل محدّدة من شاشة المنتجات.",
                        TargetElementName = "AddWorkerButton",
                        Screen = TourScreen.Workers
                    },
                    new()
                    {
                        Title = "تعديل، إيقاف، أو حذف عامل",
                        Description =
                            "من بروفايل أي عامل: \"تعديل\" بيفتح نفس فورم الإضافة معبّى ببياناته (تغيير سعر اليومية " +
                            "بالذات بيتطلب كلمة سر العمليات). \"إيقاف العامل\" بيخفيه من كل القوايم من غير ما يمسح " +
                            "سجلاته القديمة، وتقدر ترجّعه بـ\"إعادة تفعيل\" في أي وقت. \"حذف\" أقوى خطوة — بتتطلب " +
                            "كلمة سر العمليات وسبب مكتوب، وبتتسجّل في سجل العمليات.",
                        TargetElementName = "WorkerActionsRow",
                        Screen = TourScreen.Workers,
                        SelectFirstWorker = true
                    },
                    new()
                    {
                        Title = "محتاج انتباه",
                        Description =
                            "لو فيه عمال إنتاجهم قلّ عن معدّلهم المعتاد أو محتاجين متابعة لأي سبب تاني، الزرار ده بيعرضهم " +
                            "لوحدهم على طول من غير ما تدوّر عليهم وسط كل العمال. بيختفي لو مفيش حد محتاج انتباه دلوقتي. " +
                            "منفصل عن شارة \"إنتاجه قلّ اليوم\" اللي بتظهر على كارت عامل بعينه لو إنتاج النهارده بس أقل " +
                            "بشكل ملحوظ من متوسطه المعتاد — إشارة يومية سريعة، مش نفس معنى \"محتاج انتباه\" العام.",
                        TargetElementName = "ShowNeedsAttentionButton",
                        Screen = TourScreen.Workers
                    },
                    new()
                    {
                        Title = "الفترة اللي بتحكم في الأرقام",
                        Description =
                            "أسبوع، شهر، أو مدة مخصوصة — الاختيار هنا بيتحكم في حضور/غياب/صافي كل عامل في القايمة " +
                            "تحت **وفي كارت أحسن 3 عمال سوا**، مش مستقلين عن بعض. بدّل الفترة وشوف الأرقام تتغيّر في " +
                            "المكانين مرة واحدة.",
                        TargetElementName = "PeriodControlCard",
                        Screen = TourScreen.Workers
                    },
                    new()
                    {
                        Title = "كارت أحسن 3 عمال",
                        Description =
                            "بيتحسب لوحده من إنتاج الفترة المختارة فوق (أسبوع أو شهر): إنتاج العامل + تنوّع المراحل اللي " +
                            "اشتغل عليها + حضوره + جزاءاته — مش من تقييم النجوم بتاعه (ده حاجة تانية خالص، شوف \"منطق " +
                            "النجوم\" تحت). لو الفترة اتغيّرت، الترتيب بيتغيّر معاها فورًا. **دي إشارة مختلفة عن التاج " +
                            "الدهبي اللي بيظهر جنب اسم عامل في القايمة**: التاج فوري وبيتبع الفترة المختارة دلوقتي، " +
                            "بينما لقب \"🏆 أحسن عامل الأسبوع/الشهر\" الرسمي تحت اسم العامل بيفضل معلّق عليه لحد ما " +
                            "عامل تاني ياخده — لقب ثابت اتقفل رسميًا، مش لحظي زي التاج.",
                        TargetElementName = "BestWorkerCardsRow",
                        Screen = TourScreen.Workers
                    },
                    new()
                    {
                        Title = "فتح تفاصيل الفوز أو البروفايل من الكارت",
                        Description =
                            "دوس على أي كارت فايز: في وضع \"أسبوع\" بيفتحلك \"ليه فاز؟\" — تفصيل إنتاجه وحضوره وجزاءاته " +
                            "بالأسبوع ده، وجوّاه زرار \"فتح بروفايل العامل الكامل\". في وضع \"شهر\" أو فترة مخصّصة، الدوسة " +
                            "بتودّيك على طول لبروفايله الكامل من غير ما تعدّي بالتفصيل الأسبوعي (مش متاح غير لأسبوع حقيقي).",
                        TargetElementName = "BestWorkerCardsRow",
                        Screen = TourScreen.Workers
                    },
                    new()
                    {
                        Title = "منطق النجوم",
                        Description =
                            "النجوم رأي المدير الشخصي في مهارة العامل على المرحلة دي — هو بس اللي بيحددها، والبرنامج " +
                            "مايغيّرهاش لوحده أبدًا. جنب كل مهارة بتشوف كمان نسبة الأداء الفعلي، محسوبة من الإنتاج " +
                            "الحقيقي للعامل على المرحلة دي — لو فيه فرق كبير بين تقييمك والأداء الفعلي، بيظهر تحذير صغير " +
                            "يفكّرك تراجع التقييم (مثلًا عامل قيّمته 5 نجوم بس أداءه الفعلي أقل من النص).",
                        TargetElementName = "SkillsSectionHeader",
                        Screen = TourScreen.Workers,
                        SelectFirstWorker = true
                    },
                    new()
                    {
                        Title = "تذكير مراجعة التقييمات الشهرية",
                        Description =
                            "بانر دهبي بيظهر فوق الشاشة مرة كل شهر تقريبًا، وبس لو فعلًا فيه فرق واضح بين تقييمات " +
                            "نجومك وأداء العمال الحقيقي — مش تذكير روتيني. دوس \"راجع دلوقتي\" يوريك الاقتراحات " +
                            "وتقدر تقبلها أو تتجاهلها واحد واحد. بيختفي لوحده لو مفيش اقتراحات فعلية.",
                        TargetElementName = "SkillReviewCard",
                        Screen = TourScreen.Workers
                    },
                    new()
                    {
                        Title = "الهستوري الأسبوعي",
                        Description =
                            "آخر 8 أسابيع شغل للعامل، كارت لكل أسبوع. دوس على أي أسبوع يتمدد ويوريك: قطع أنتجها، أيام " +
                            "غياب، جزاءات، وصافي يوميّاته، + تفصيل أنهي مراحل من أنهي منتجات اشتغل عليها الأسبوع ده " +
                            "بالظبط. أسابيع مفيهاش شغل خالص بتتعلّم كده وميتفتحوش.",
                        TargetElementName = "WeeklyHistoryHeader",
                        Screen = TourScreen.Workers,
                        SelectFirstWorker = true
                    },
                    new()
                    {
                        Title = "ترتيب العمال",
                        Description =
                            "دوس الزرار، هيفتحلك شاشة فيها كل العمال بترتيبهم الحالي — وده نفس الترتيب اللي هيظهر بيه " +
                            "العمال في كل شاشات البرنامج. جوّاها 3 طرق ترتيب: اسحب أي عامل لمكانه الجديد بالماوس، دوس " +
                            "زراري فوق/تحت جنب اسمه، أو اكتب رقم ترتيبه مباشرة في الصندوق. أي تغيير بيتحفظ فورًا من غير " +
                            "زرار حفظ منفصل.",
                        TargetElementName = "OpenWorkerOrderButton",
                        Screen = TourScreen.Workers
                    }
                },
                GuidedFlows = new List<GuidedPracticeFlow>
                {
                    new()
                    {
                        Title = "إضافة مهارة لعامل — تجربة عملية",
                        Description =
                            "جرّب الخطوات الحقيقية على بيانات تجريبية: افتح وضع الإضافة، افتح كارت منتج، وقيّم مرحلة " +
                            "بالنجوم. دوس على العنصر المضوّي بنفسك — مش هتشرحلك بس، هتعملها فعلًا.",
                        Screen = TourScreen.Workers,
                        SelectFirstWorker = true,
                        Steps = new List<GuidedPracticeStep>
                        {
                            new()
                            {
                                Title = "افتح وضع الإضافة",
                                Description = "دوس على زرار \"إضافة مهارات\" عشان تفتح كروت المنتجات.",
                                TargetElementName = "ToggleAddSkillsButton",
                                IsComplete = vm => vm is ViewModels.WorkersViewModel { Detail.IsAddingSkills: true }
                            },
                            new()
                            {
                                Title = "افتح كارت منتج",
                                Description = "دوس على أي كارت منتج تحت عشان يتفتح ويوريك مراحله.",
                                TargetElementName = "SkillsListPanel",
                                IsComplete = vm => vm is ViewModels.WorkersViewModel { Detail: { } d } &&
                                    d.SkillProducts.Any(g => g.IsExpanded),
                                WatchSelectors = vm => vm is ViewModels.WorkersViewModel { Detail: { } d }
                                    ? d.SkillProducts.Cast<object>()
                                    : Array.Empty<object>()
                            },
                            new()
                            {
                                Title = "قيّم مرحلة بـ3 نجوم أو أكتر",
                                Description =
                                    "دوس على النجمة التالتة (أو اللي بعدها) على أي مرحلة ناقصة — بتضيفها للعامل بالتقييم " +
                                    "ده على طول.",
                                TargetElementName = "SkillsListPanel",
                                IsComplete = vm => vm is ViewModels.WorkersViewModel { Detail: { } d } &&
                                    d.SkillProducts.Any(g => g.IsExpanded && g.Stages.Any(s => s.IsKnown && s.Stars >= 3)),
                                WatchSelectors = vm => vm is ViewModels.WorkersViewModel { Detail: { } d }
                                    ? d.SkillProducts.SelectMany(g => g.Stages).Cast<object>()
                                    : Array.Empty<object>()
                            }
                        }
                    }
                }
            },
            new()
            {
                Title = "المنتجات والمراحل",
                Icon = "PackageVariantClosed",
                Description =
                    "هنا بتعرّف كل منتج بيعمله المصنع وترتيب مراحل إنتاجه (خط الإنتاج). كل مرحلة " +
                    "ليها عمال مؤهّلين بيها بس (شوف تقييمات المهارات في شاشة العمال). تقدر توقف " +
                    "منتج أو مرحلة من غير ما تمسحها — بيختفي من الاختيار في التسجيل اليومي لكن " +
                    "سجلاته القديمة تفضل زي ما هي.",
                TourSteps = new List<AppTourStep>
                {
                    new()
                    {
                        Title = "فلاتر وترتيب",
                        Description = "دوّر أو رتّب المنتجات من هنا — نفس فكرة فلاتر شاشة العمال بالظبط.",
                        TargetElementName = "FilterToggle",
                        Screen = TourScreen.Products
                    },
                    new()
                    {
                        Title = "فترة العرض",
                        Description =
                            "بالإفتراض بتشوف المنتجات اللي اشتغلت الفترة دي بس، عشان القايمة متفضلش مليانة منتجات " +
                            "متوقفة من شهور. عايز تشوف منتج قديم مش شغال دلوقتي؟ بدّل الفترة لـ\"كل المنتجات\".",
                        TargetElementName = "PeriodToggle",
                        Screen = TourScreen.Products
                    },
                    new()
                    {
                        Title = "منتج جديد",
                        Description =
                            "إضافة منتج جديد. مثلاً: منتج اسمه \"شنطة جلد\" — بعد ما تضيفه، تدخل عليه وتضيف مراحله " +
                            "بالترتيب (قص ← خياطة ← تشطيب)، وده اللي هيحدد ترتيب التسجيل في شاشة الإنتاج اليومي.",
                        TargetElementName = "AddProductButton",
                        Screen = TourScreen.Products
                    },
                    new()
                    {
                        Title = "إضافة مرحلة وترتيب الخط",
                        Description =
                            "\"إضافة مرحلة\" بتضيف خطوة جديدة في آخر الخط باسمها وكوتتها (قطع/يوميّة). كل مرحلة عندها " +
                            "سهمين فوق/تحت ينقّلوها في الترتيب — الترتيب مهم فعلًا، لأنه اللي نطاقات التسجيل (\"من " +
                            "مرحلة كذا لكذا\") بتتحسب عليه. تقدر توقف مرحلة أو تمسحها من غير ما تلمس سجلات إنتاجها " +
                            "القديمة.",
                        TargetElementName = "AddStageButton",
                        Screen = TourScreen.Products
                    },
                    new()
                    {
                        Title = "مين يعرف يعمل المرحلة دي؟",
                        Description =
                            "على كل كارت مرحلة، زرار 👤🔍 بيفتح قايمة كل العمال المؤهّلين للمرحلة دي، مرتّبين من " +
                            "الأحسن تقييمًا — نفس الترتيب اللي هتشوفه لما تختار عامل في شاشة تسجيل الإنتاج اليومي.",
                        TargetElementName = "ProductionLineHeader",
                        Screen = TourScreen.Products
                    },
                    new()
                    {
                        Title = "معامل الصعوبة",
                        Description =
                            "شارة صغيرة (أيقونة رافع أثقال) بتظهر على مرحلة لو معامل صعوبتها مختلف عن الافتراضي. ده " +
                            "بيأثّر بس على ترتيب لوحة \"أحسن 3 عمال\" في شاشة العمال — مالوش أي تأثير على الأجور أو " +
                            "حساب اليوميات.",
                        TargetElementName = "ProductionLineHeader",
                        Screen = TourScreen.Products
                    },
                    new()
                    {
                        Title = "مرحلة الرص",
                        Description =
                            "زرار بيظهر مرة واحدة بس لكل منتج (يختفي بعد ما تستخدمه) — بيضيف مرحلة خاصة آخر الخط " +
                            "لعامل الرص، من غير كوتة قطع ومن غير تسجيل عدد في شاشة الإنتاج اليومي، وعامل الرص بيتكلّف " +
                            "عليها بدوره بالساعة مش بمهارة مسجّلة — عشان كده مالهاش زرار \"مين يعرف يعملها؟\".",
                        TargetElementName = "AddRackingStageButton",
                        Screen = TourScreen.Products
                    },
                    new()
                    {
                        Title = "تنبيهات مهمة",
                        Description =
                            "منتج من غير أي مرحلة (\"لسه من غير مراحل\") مش هينفع يتسجّل عليه إنتاج خالص — لازم تضيف " +
                            "مرحلة الأول. وأي مرحلة مالهاش عمال مؤهّلين خالص بتظهر بلون تحذير على كارتها، لأن شاشة " +
                            "تسجيل الإنتاج بتعرض العمال المؤهّلين بس — مرحلة بصفر عمال مستحيل تتسجّل عليها.",
                        TargetElementName = "NoStagesWarning",
                        Screen = TourScreen.Products
                    }
                }
            },
            new()
            {
                Title = "تسجيل الإنتاج اليومي",
                Icon = "ClipboardEditOutline",
                Description =
                    "القلب الحقيقي للبرنامج: هنا بتسجّل إنتاج كل يوم. سبع تبويبات داخلية — من تسجيل " +
                    "الإنتاج نفسه لحد الهالك — دوس على أي تبويب تحت عشان تشوف ميزاته بالتفصيل.",
                TourSteps = new List<AppTourStep>(),
                SubTopics = new List<HelpTopic>
                {
                    new()
                    {
                        Title = "تسجيل الإنتاج",
                        Description =
                            "القلب الحقيقي للبرنامج: هنا بتسجّل إنتاج كل يوم. تضيف منتج، توزّع عماله على " +
                            "مراحله، تكتب القطع في نطاقات (من مرحلة لمرحلة)، وتحفظ. الحضور بيتسجّل تلقائي " +
                            "لأي عامل شارك في التسجيل — مفيش داعي تسجّله يدوي.",
                        TourSteps = new List<AppTourStep>
                        {
                            new()
                            {
                                Title = "ملخص اليوم وإضافة منتج",
                                Description = "أرقام اليوم (قطع/يوميات) بتظهر هنا أول ما تسجّل حاجة، وزرار \"إضافة منتج\" بيفتح رحلة تسجيل جديدة.",
                                TargetElementName = "ProductionSummaryBar",
                                Screen = TourScreen.DailyEntry,
                                TabIndex = 0
                            },
                            new()
                            {
                                Title = "إضافة منتج",
                                Description =
                                    "مثال: النهارده شغالين على \"شنطة\" و\"دبلة\" مع بعض؟ دوس الزرار ده مرتين — كل منتج بياخد رحلة " +
                                    "منفصلة، تختار فيها المنتج وتوزّع عماله على مراحله وتكتب القطع، من غير ما تتلخبط برحلة المنتج التاني.",
                                TargetElementName = "AddFlowSessionButton",
                                Screen = TourScreen.DailyEntry,
                                TabIndex = 0
                            }
                        }
                    },
                    new()
                    {
                        Title = "الرصيد الأولي",
                        Description =
                            "أي إنتاج ماوصلش لآخر مرحلة في الخط بيتحول تلقائيًا لـ\"رصيد أولي\" — شغل واقف " +
                            "منتظر يكمّل. هنا تديره: تسحب منه لما تكمّل الشغل، تحوّله لهالك لو اتعطّب، أو " +
                            "تراجع رصيد اتسحب بالكامل من السجل.",
                        TourSteps = new List<AppTourStep>
                        {
                            new()
                            {
                                Title = "رصيد أولي جديد",
                                Description = "لو عندك شغل واقف من قبل التحديث ده (يعني ماتحوّلش تلقائي)، تقدر تضيفه يدوي من هنا مرة واحدة.",
                                TargetElementName = "AddInitialBalanceButton",
                                Screen = TourScreen.DailyEntry,
                                TabIndex = 1
                            },
                            new()
                            {
                                Title = "السجل",
                                Description = "مثلاً رصيد \"شنطة\" اتسحب منه كله واتسجّل؟ بيتخبّى من القايمة العادية تلقائي، وتلاقيه هنا في \"السجل\" للمراجعة بس.",
                                TargetElementName = "InitialBalanceHistoryToggle",
                                Screen = TourScreen.DailyEntry,
                                TabIndex = 1
                            }
                        }
                    },
                    new()
                    {
                        Title = "سجلات اليوم",
                        Description =
                            "مراجعة وتصحيح إنتاج اتسجّل قبل كده — بفترة (يوم/أسبوع/شهر) مستقلة عن تاريخ " +
                            "التسجيل فوق. تقدر تعدّل عدد قطع سجل، تمسحه، أو تتراجع عن آخر عملية بـCtrl+Z.",
                        TourSteps = new List<AppTourStep>
                        {
                            new()
                            {
                                Title = "سجلات اليوم",
                                Description = "مثال: سجّلت 50 قطعة غلط بدل 40؟ دوّر على السجل هنا وعدّله بزرار التعديل، من غير ما تحذف وتسجّل من الأول.",
                                TargetElementName = "RecordsTabRoot",
                                Screen = TourScreen.DailyEntry,
                                TabIndex = 2
                            },
                            new()
                            {
                                Title = "فترة العرض",
                                Description = "يوم، أسبوع، أو شهر — مستقلة تمامًا عن تاريخ التسجيل في باقي التبويبات، عشان تقدر تراجع أسبوع فات وانت لسه بتسجّل النهارده.",
                                TargetElementName = "RecordsGrainRow",
                                Screen = TourScreen.DailyEntry,
                                TabIndex = 2
                            }
                        }
                    },
                    new()
                    {
                        Title = "الحضور والغياب",
                        Description =
                            "الحضور بيتسجّل تلقائي لأي عامل شارك في تسجيل إنتاج النهارده، بس تقدر من هنا " +
                            "تراجع وتعدّل: حاضر، غايب بعذر، غايب من غير عذر (بيسحب نص يوم تلقائي)، أو لسه " +
                            "مش متسجّل.",
                        TourSteps = new List<AppTourStep>
                        {
                            new()
                            {
                                Title = "ملخص الحضور",
                                Description = "عدد الحاضرين والغائبين بعذر ومن غير عذر. دوس على رقم \"غايب من غير عذر\" مثلاً، والقايمة تحتيه بتتفلتر عليهم بس.",
                                TargetElementName = "AttendanceSummaryRow",
                                Screen = TourScreen.DailyEntry,
                                TabIndex = 3
                            },
                            new()
                            {
                                Title = "حفظ الحضور",
                                Description = "غيّرت حالة عامل يدوي (من حاضر لغايب بعذر مثلاً)؟ التعديل ده لازم يتحفظ من هنا عشان يتسجّل فعلًا.",
                                TargetElementName = "SaveAttendanceButton",
                                Screen = TourScreen.DailyEntry,
                                TabIndex = 3
                            }
                        }
                    },
                    new()
                    {
                        Title = "الجزاءات",
                        Description =
                            "تسجيل جزاء (خصم يوم أو جزء منه) على عامل، بسبب مكتوب — غياب من غير عذر بيسجّل " +
                            "جزاءه التلقائي هنا، وتقدر تضيف جزاءات يدوية لأسباب تانية.",
                        TourSteps = new List<AppTourStep>
                        {
                            new()
                            {
                                Title = "تسجيل جزاء",
                                Description = "مثال: عامل اتأخر ساعتين؟ اختاره من البحث، اختار مقدار الخصم (نص يوم مثلًا)، اكتب السبب \"اتأخر\"، ودوس تسجيل.",
                                TargetElementName = "AddPenaltyButton",
                                Screen = TourScreen.DailyEntry,
                                TabIndex = 4
                            },
                            new()
                            {
                                Title = "جزاءات اليوم",
                                Description = "كل جزاء اتسجّل النهارده — يدوي كان أو تلقائي من غياب من غير عذر — بتفاصيله وسببه.",
                                TargetElementName = "PenaltiesGrid",
                                Screen = TourScreen.DailyEntry,
                                TabIndex = 4
                            }
                        }
                    },
                    new()
                    {
                        Title = "السلف والحوافز",
                        Description =
                            "سلفة (مبلغ بيتخصم من أجر العامل في كشف الفترة) أو حافز (مبلغ بيتزاد على أجره) " +
                            "— بمبلغ بالجنيه وملاحظة اختيارية.",
                        TourSteps = new List<AppTourStep>
                        {
                            new()
                            {
                                Title = "تسجيل سلفة أو حافز",
                                Description = "مثال: عامل طلب سلفة 300 جنيه؟ اختاره، اختار النوع \"سلفة\"، اكتب 300، ودوس تسجيل — هتتخصم من كشفه في الفترة دي.",
                                TargetElementName = "AddAdjustmentButton",
                                Screen = TourScreen.DailyEntry,
                                TabIndex = 5
                            },
                            new()
                            {
                                Title = "حركات اليوم",
                                Description = "كل سلفة وحافز اتسجّل النهارده، بلون مختلف حسب النوع (السلفة والحافز واضحين من بعض بصريًا).",
                                TargetElementName = "AdjustmentsGrid",
                                Screen = TourScreen.DailyEntry,
                                TabIndex = 5
                            }
                        }
                    },
                    new()
                    {
                        Title = "الهالك",
                        Description =
                            "قطع اتشالت ومش هتتكمّل. الهالك على آخر مرحلة بيتخصم من الإنتاج التام، واللي في " +
                            "نص الخط بيتشال من الشغل الواقف (الرصيد الأولي) — مالوش أي تأثير على الأجور.",
                        TourSteps = new List<AppTourStep>
                        {
                            new()
                            {
                                Title = "تسجيل هالك",
                                Description = "مثال: 5 قطع اتعطبت في مرحلة الخياطة؟ اختار المرحلة، اكتب 5، والسبب \"عيب في القماش\" مثلًا.",
                                TargetElementName = "AddScrapButton",
                                Screen = TourScreen.DailyEntry,
                                TabIndex = 6
                            },
                            new()
                            {
                                Title = "هالك اليوم",
                                Description = "إجمالي الهالك وتفاصيل كل سجل — المنتج، المرحلة، العدد، والسبب.",
                                TargetElementName = "ScrapGrid",
                                Screen = TourScreen.DailyEntry,
                                TabIndex = 6
                            }
                        }
                    }
                }
            },
            new()
            {
                Title = "الذاكرة",
                Icon = "BookmarkMultipleOutline",
                Description =
                    "خطط إنتاج مؤجّلة: تكتب \"عايز أعمل المنتج ده بالترتيب ده يوم كذا\" والبرنامج " +
                    "يفكّرك لما اليوم ده ييجي. الخطة تفضل \"نشطة\" لحد ما تسجّل إنتاج حقيقي فيها — " +
                    "مجرد فتح الشاشة من التذكير مبيعلّمهاش منجزة. من كارت أي خطة تقدر تبدأها دلوقتي، " +
                    "تأجّل تذكيرها، أو ترجّعها نشطة لو اتعلّمت منجزة غلط.",
                TourSteps = new List<AppTourStep>
                {
                    new()
                    {
                        Title = "خطة جديدة",
                        Description =
                            "مثال: عايز تبدأ منتج \"الدبلة\" الأسبوع الجاي بترتيب مراحل مختلف عن المعتاد؟ اختار المنتج، " +
                            "رتّب مراحله من هنا لو محتاج ترتيب مختلف، اكتب أي ملاحظة، وحدّد يوم التذكير — هيفكّرك بيها " +
                            "أول ما اليوم ده ييجي.",
                        TargetElementName = "MemoryFormPanel",
                        Screen = TourScreen.Memory
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
                        Description =
                            "من كارت الخطة نفسه: \"ابدأها دلوقتي\" بدل ما تستنى التذكير، \"أجّل\" لو مش وقتها لسه، " +
                            "أو \"رجّعها نشطة\" لو اتعلّمت منجزة غلط (فتحت شاشتها بس ماسجّلتش إنتاج فعلي فيها).",
                        TargetElementName = "MemoryListsPanel",
                        Screen = TourScreen.Memory
                    }
                }
            },
            new()
            {
                Title = "التقييم والمتابعة",
                Icon = "ChartBar",
                Description =
                    "لوحة متابعة حيّة تتقرا على الشاشة، مش للتصدير أو الطباعة (شوف \"التقارير\" " +
                    "لده). فيها إنتاج اليوم/الأسبوع/الشهر لكل منتج مع الهالك، رسم بياني لإنتاج " +
                    "المنتجات بمرور الوقت، ومتوسط إنتاج كل عامل — مع تنبيه لأي عامل إنتاجه قلّ عن معدّله المعتاد.",
                TourSteps = new List<AppTourStep>
                {
                    new()
                    {
                        Title = "تجميع الفترة",
                        Description = "شوف أرقام اليوم، أو اجمعها بالأسبوع أو الشهر — لو عايز تقارن إنتاج الأسبوع ده بالي فات مثلاً.",
                        TargetElementName = "OutputGrainRow",
                        Screen = TourScreen.Evaluation
                    },
                    new()
                    {
                        Title = "ملخص سريع",
                        Description = "قطعة خلصت الخط كامل، قطعة دخلت الخط، وقطعة هالك — أهم 3 أرقام تعرفهم أول ما تفتح الشاشة.",
                        TargetElementName = "KpiSummaryRow",
                        Screen = TourScreen.Evaluation
                    },
                    new()
                    {
                        Title = "فلترة بمنتج",
                        Description = "ضيّق الرسم البياني والأرقام على منتج معيّن، زي \"الشنطة\" بس، بدل كل المنتجات مع بعض.",
                        TargetElementName = "ProductToggle",
                        Screen = TourScreen.Evaluation
                    }
                }
            },
            new()
            {
                Title = "التقارير",
                Icon = "FileDocumentOutline",
                Description =
                    "بناء تقرير مخصّص يتصدّر Excel: اختار الموضوع (إنتاج/حضور/هالك/مهارات/أجور)، " +
                    "طريقة التجميع، الفترة، وأي فلاتر. الأعمدة نفسها تقدر تظهرها أو تخفيها أو تعيد " +
                    "ترتيبها، والمعاينة بتتحدّث فورًا مع كل اختيار. الإعدادات اللي بتستخدمها كتير " +
                    "تقدر تحفظها كقالب جاهز.",
                TourSteps = new List<AppTourStep>
                {
                    new()
                    {
                        Title = "قالب جاهز",
                        Description =
                            "مثلاً بتعمل كشف أجور آخر كل شهر بنفس الفلاتر بالظبط؟ احفظه قالب مرة واحدة، وبعدها اختاره من " +
                            "هنا وكل الاختيارات بتتملى لوحدها.",
                        TargetElementName = "TemplateComboBox",
                        Screen = TourScreen.Reports
                    },
                    new()
                    {
                        Title = "الموضوع",
                        Description = "أول وأهم اختيار — إنتاج ولا حضور ولا أجور ولا هالك ولا مهارات. كل حاجة تانية في الشاشة بتتغيّر حسبه.",
                        TargetElementName = "SubjectComboBox",
                        Screen = TourScreen.Reports
                    },
                    new()
                    {
                        Title = "المعاينة الحية",
                        Description = "الجدول ده بيتحدّث لحظيًا مع كل اختيار (فلتر جديد، تجميع مختلف) قبل ما تصدّر أي حاجة — تتأكد إن الأرقام صح الأول.",
                        TargetElementName = "PreviewGrid",
                        Screen = TourScreen.Reports
                    },
                    new()
                    {
                        Title = "تصدير Excel",
                        Description = "التقرير جاهز في المعاينة؟ يتصدّر ملف Excel تقدر تفتحه أو تطبعه على طول.",
                        TargetElementName = "ExportButton",
                        Screen = TourScreen.Reports
                    }
                }
            },
            new()
            {
                Title = "سجل العمليات",
                Icon = "ClipboardTextClockOutline",
                Description =
                    "أرشيف لكل عملية حساسة حصلت في البرنامج — حذف، تعديل مالي (أجر/جزاء/سلفة)، " +
                    "تصحيح إنتاج — بتاريخها وسببها ومين عملها. الأحداث القديمة بتتمسح تلقائيًا بعد " +
                    "مدة معيّنة (تقدر تظبطها من الإعدادات).",
                TourSteps = new List<AppTourStep>
                {
                    new()
                    {
                        Title = "قايمة الأحداث",
                        Description = "كل عملية حساسة حصلت في البرنامج، بأيقونة وتاريخ وسبب واضح — مثلاً \"مين غيّر أجر أحمد وامتى\".",
                        TargetElementName = "ActivityLogList",
                        Screen = TourScreen.ActivityLog
                    },
                    new()
                    {
                        Title = "فلترة بالنوع",
                        Description = "دوّر على حركة فلوس بس، أو حذف بس، بدل ما تعدّي على كل حدث تسجيل إنتاج في الشهر.",
                        TargetElementName = "EventGroupComboBox",
                        Screen = TourScreen.ActivityLog
                    },
                    new()
                    {
                        Title = "فترات جاهزة",
                        Description = "النهارده أو آخر 30 يوم بضغطة واحدة، بدل ما تختار تاريخين من التقويم كل مرة.",
                        TargetElementName = "QuickRangeButton",
                        Screen = TourScreen.ActivityLog
                    }
                }
            },
            new()
            {
                Title = "الإعدادات والنسخ",
                Icon = "CogOutline",
                Description =
                    "كل إعدادات البرنامج في مكان واحد، مرتّبة بالأهمية: كلمة سر العمليات الحساسة " +
                    "أولًا، بعدها النسخ الاحتياطي (محلي تلقائي كل يوم، وخارجي اختياري لفلاشة أو " +
                    "قرص تاني)، وآخر حاجة استرجاع نسخة احتياطية (مقصود إنها آخر حاجة — دي أخطر " +
                    "إعدادات في الشاشة).",
                TourSteps = new List<AppTourStep>
                {
                    new()
                    {
                        Title = "كلمة سر العمليات",
                        Description = "بتحمي الحذف والتعديلات المالية — أهم إعداد حماية في البرنامج كله. حطّها الأول قبل أي حاجة تانية.",
                        TargetElementName = "SetPasswordButton",
                        Screen = TourScreen.Settings
                    },
                    new()
                    {
                        Title = "النسخ الاحتياطي المحلي",
                        Description = "نسخة تلقائية كل يوم — من هنا تقدر تاخد نسخة دلوقتي بنفسك (مثلاً قبل تعديل كبير) أو تفتح مجلد النسخ.",
                        TargetElementName = "BackupCard",
                        Screen = TourScreen.Settings
                    },
                    new()
                    {
                        Title = "النسخ الخارجي",
                        Description = "نسخة على فلاشة أو قرص تاني — لو الهارد نفسه اتلف، النسخة المحلية بتضيع معاه، والنسخة الخارجية بس اللي بتحميك من ده.",
                        TargetElementName = "ChooseExternalFolderButton",
                        Screen = TourScreen.Settings
                    },
                    new()
                    {
                        Title = "استرجاع نسخة احتياطية",
                        Description = "لو حصلت مشكلة كبيرة في البيانات، ترجع لأي نسخة سابقة من هنا — خطوة حساسة، مقصود إنها آخر حاجة في الشاشة.",
                        TargetElementName = "RestoreBackupButton",
                        Screen = TourScreen.Settings
                    }
                }
            },
            new()
            {
                Title = "الحسابات الإدارية",
                Icon = "AccountTie",
                Description =
                    "حسابات رؤساء ومديري الأقسام — عندهم تسجيل دخول منفصل عن باقي العمال. مدير " +
                    "القسم بس هو اللي يقدر يضيف حساب جديد، يعدّل بيانات أي حساب، يوقفه، أو يمسحه. " +
                    "أي حساب تاني بيشوف بروفايله هو بس، ولو عدّل بياناته الشخصية مقدرش يغيّر مسمّاه " +
                    "الوظيفي ولا راتبه.",
                TourSteps = new List<AppTourStep>
                {
                    new()
                    {
                        Title = "قايمة الحسابات",
                        Description = "كل حساب إداري وحالته — والصلاحيات المتاحة تفرق حسب مين بيشوف الشاشة.",
                        TargetElementName = "AccountsListCard",
                        Screen = TourScreen.DepartmentAccounts
                    },
                    new()
                    {
                        Title = "إضافة حساب",
                        Description = "زرار مدير القسم بس — بيضيف رئيس/مدير قسم جديد بتسجيل دخول خاص بيه (يوزر وباسورد).",
                        TargetElementName = "AddAccountButton",
                        Screen = TourScreen.DepartmentAccounts
                    },
                    new()
                    {
                        Title = "أنهي وضع أنت فيه",
                        Description = "العنوان هنا بيوضّحلك: بتشوف كل الحسابات كمدير قسم، ولا بروفايلك أنت بس كرئيس قسم.",
                        TargetElementName = "ScreenTitleRow",
                        Screen = TourScreen.DepartmentAccounts
                    }
                }
            }
        };
    }
}
