using WorkforceManager.Core.Enums;

namespace WorkforceManager.Data.Seed
{
    /// <summary>
    /// رابط مهارة واحد: مرحلة معينة في منتج معين (StageName محدد)، أو كل
    /// مراحل المنتج (StageName == null)، مع استثناءات اختيارية (Exclude)
    /// لحالة "كل المراحل ماعدا كذا".
    /// </summary>
    public record SkillLink(string ProductName, string? StageName, SkillLevel Level, string[]? Exclude = null);

    /// <summary>
    /// ربط مهارات الـ 46 عامل الحقيقيين (اسماء الصنفرة) بمراحل الإنتاج
    /// الفعلية — مبني على تفسير ملاحظات كل عامل النصية (SkillsNotes في
    /// RealDataSeed) حسب القواعد المتفق عليها:
    ///
    /// - "الكوبشه" = كل منتجات الكبشة السبعة مع بعض (ستار/مروحه/مثلثه/
    ///   تاج/مشتمل/جلو/الماني).
    /// - "اللوازم" = مجموعة التوابع: كوع بسن + وصله تي + حنفيه بزبوز.
    /// - "المحابس"/"صنفره المحابس" = المنتجات الأربعة: GRS + MG + ماكس
    ///   فتيل + ماجيك.
    /// - درجات الإتقان: جيد = مبتدئ، جيد جدا = محترف، ممتاز = خبير.
    ///   من غير درجة مذكورة = محترف (الافتراضي).
    /// - "عامل تحت التدريب"/"عامل رص"/"عامل جوده" وصف عام مش مهارة مرحلة
    ///   — العمال دول من غير أي روابط (ملاحظتهم النصية بتفضل زي ما هي).
    /// - أجزاء غامضة في بعض الملاحظات (زي "كوع بسن رايش" — مفيش مرحلة
    ///   بالاسم ده في كوع بسن، و"دبلتين" — مفيش مرحلة بالاسم ده في أي
    ///   منتج) اتسابت من غير ربط بدل التخمين الغلط. لو حد شافها لازم
    ///   تتربط، تضاف يدويًا من شاشة العمال.
    /// </summary>
    public static class WorkerSkillsSeed
    {
        private static readonly string[] KabshaFamily =
            { "كبشه ستار", "كبشه مروحه", "كبشه مثلثه", "كبشه تاج", "كبشه مشتمل", "كبشه جلو", "كبشه الماني" };

        private static readonly string[] LawazemFamily = { "كوع بسن", "وصله تي", "حنفيه بزبوز" };

        /// <summary>كل مراحل منتج معين (مع استثناءات اختيارية)</summary>
        private static IEnumerable<SkillLink> All(string product, SkillLevel level = SkillLevel.Proficient, params string[] exclude)
            => new[] { new SkillLink(product, null, level, exclude.Length > 0 ? exclude : null) };

        /// <summary>كل مراحل كل منتجات عائلة معينة (زي عائلة الكبشة)</summary>
        private static IEnumerable<SkillLink> AllFamily(IEnumerable<string> family, SkillLevel level = SkillLevel.Proficient)
            => family.Select(p => new SkillLink(p, null, level));

        /// <summary>مراحل محددة بالاسم داخل منتج واحد</summary>
        private static IEnumerable<SkillLink> Stages(string product, params string[] stageNames)
            => stageNames.Select(s => new SkillLink(product, s, SkillLevel.Proficient));

        public static Dictionary<string, List<SkillLink>> BuildLinks()
        {
            var map = new Dictionary<string, List<SkillLink>>();

            // W001 مصطفى محمد مهدي عبد الحفيظ محمود — ملاحظات فاضية، مفيش معلومة تتربط

            // "جميع مراحل صنفره المحابس و اللوازم" — نفس النص بالظبط عند 4 عمال
            var sandingAllMahabesAndLawazem = All("GRS").Concat(All("MG")).Concat(All("ماكس فتيل")).Concat(All("ماجيك"))
                .Concat(All("كوع بسن")).Concat(All("وصله تي")).Concat(All("حنفيه بزبوز")).ToList();
            map["W002"] = sandingAllMahabesAndLawazem; // + "/لمعه لوازم" تكرار مغطّى بالفعل
            map["W003"] = sandingAllMahabesAndLawazem;
            map["W004"] = sandingAllMahabesAndLawazem;
            map["W015"] = sandingAllMahabesAndLawazem;

            // W005 ياسر كامل صوفان
            map["W005"] = Stages("GRS", "لفه صغيره", "رقبه", "دبله", "بطحتين", "بطن خشن", "بطن ناعم")
                .Concat(Stages("MG", "رقبه", "دبله"))
                .Concat(All("كوع بسن"))
                .Concat(All("ماجيك"))
                .ToList();

            // "طقم عقله/دبله محبس" — نفس النص عند 5 عمال (W006, W025, W044, W046 + جزء من W006)
            var takmOqlaPlusDabla = All("طقم عقله").Concat(Stages("GRS", "دبله")).Concat(Stages("MG", "دبله")).ToList();
            map["W006"] = takmOqlaPlusDabla;
            map["W025"] = takmOqlaPlusDabla;
            map["W044"] = takmOqlaPlusDabla;
            map["W046"] = takmOqlaPlusDabla;

            // W007 تامر جاد محمد علي
            map["W007"] = All("GRS", exclude: "بطحتين")
                .Concat(Stages("MG", "ضربه", "رقبه"))
                .Concat(All("كوع بسن")).Concat(All("وصله تي")).Concat(All("حنفيه بزبوز"))
                .Concat(Stages("ماجيك", "لفه صغيره 600", "لفه ناعم"))
                .ToList();

            // W008 حسام محمد عبد الجواد عبد الله
            map["W008"] = All("كوع بسن").Concat(All("وصله تي")).Concat(All("حنفيه بزبوز"))
                .Concat(All("GRS", exclude: "بطحتين"))
                .Concat(Stages("ماجيك", "لفه صغيره 600", "لفه ناعم", "بطن خشن", "بطن ناعم"))
                .ToList();

            // W009 خيري فراج احمد شحاتة
            map["W009"] = All("GRS", exclude: "بطحتين")
                .Concat(Stages("MG", "ضربه", "رقبه"))
                .Concat(All("كوع بسن"))
                .Concat(Stages("وصله تي", "بطن خشن", "3 لفات خشن", "3 لفات ناعم"))
                .Concat(All("ماجيك", exclude: new[] { "بطحتين 400", "بطحتين ناعم" }))
                .ToList();

            // W010–W014 و W029–W031: "جميع مراحل الكوبشه" بدرجات إتقان مختلفة
            map["W010"] = AllFamily(KabshaFamily, SkillLevel.Beginner).ToList();    // جيد
            map["W011"] = AllFamily(KabshaFamily, SkillLevel.Proficient).ToList();  // جيد جدا
            map["W012"] = AllFamily(KabshaFamily, SkillLevel.Expert).ToList();      // ممتاز
            map["W013"] = AllFamily(KabshaFamily, SkillLevel.Expert).ToList();      // ممتاز
            map["W014"] = AllFamily(KabshaFamily, SkillLevel.Beginner).ToList();    // جيد

            // W016 عبدالسلام عابدين عبد السلام
            map["W016"] = Stages("GRS", "دبله").Concat(Stages("MG", "دبله"))
                .Concat(Stages("ماجيك", "لفه صغيره 600", "لفه ناعم"))
                .ToList();

            // W017 اسلام سعد الدين محمد
            map["W017"] = All("GRS", exclude: "بطحتين")
                .Concat(Stages("MG", "لفه 400", "لفه 800", "رقبه", "ضربه"))
                .Concat(Stages("ماجيك", "بطن خشن", "بطن ناعم", "لفه صغيره 600", "لفه ناعم"))
                .ToList();

            // W018 محمود محمد محمود
            map["W018"] = Stages("GRS", "بطن خشن", "بطن ناعم", "ضربتين", "لفه 400", "لفه 600").ToList();

            // W019 محمد جمال مصطفى
            map["W019"] = All("ماجيك")
                .Concat(Stages("وصله تي", "3 لفات خشن", "3 لفات ناعم"))
                .Concat(Stages("GRS", "دبله", "رقبه", "بطن خشن", "بطن ناعم", "ضربتين", "لفه 400", "لفه 600"))
                .Concat(Stages("MG", "دبله", "ضربه", "عريض 800", "بطن 600"))
                .ToList();

            // W020 اشرف محمد اسماعيل
            map["W020"] = Stages("GRS", "بطن خشن", "بطن ناعم", "دبله")
                .Concat(Stages("وصله تي", "بطن خشن", "3 لفات خشن", "3 لفات ناعم"))
                .ToList();

            // W021 محمد مصطفى احمد — "عامل تحت التدريب" — مفيش روابط

            // W022 عبدالله احمد محمد
            map["W022"] = Stages("GRS", "رقبه", "دبله")
                .Concat(Stages("MG", "رقبه", "دبله"))
                .Concat(Stages("ماكس فتيل", "رقبه"))
                .ToList();

            // W023 رجب حسان محمد
            map["W023"] = Stages("كوع بسن", "عريض لمعه")
                .Concat(Stages("حنفيه بزبوز", "لفه لمعه", "عريض لمعه", "بوز لمعه"))
                .ToList();

            // W024 يوسف احمد عبدالصمد
            map["W024"] = All("كوع بسن").Concat(All("وصله تي")).Concat(All("حنفيه بزبوز"))
                .Concat(All("MG"))
                .Concat(Stages("GRS", "دبله", "رقبه", "بطن خشن", "بطن ناعم", "ضربتين", "لفه 400", "لفه 600", "بطحتين"))
                .ToList();

            // W026 جمال الصدام جمال — "رايش" و"رايش وش" عبر عائلة الكبشة
            map["W026"] = KabshaFamily.Select(p => new SkillLink(p, "رايش", SkillLevel.Proficient))
                .Concat(new[] { "كبشه مروحه", "كبشه مثلثه", "كبشه مشتمل", "كبشه جلو" }
                    .Select(p => new SkillLink(p, "رايش وش", SkillLevel.Proficient)))
                .ToList();

            // W027, W028, W036: "طقم عقله" بس (من غير "دبله محبس")
            var takmOqlaOnly = All("طقم عقله").ToList();
            map["W027"] = takmOqlaOnly;
            map["W028"] = takmOqlaOnly;
            map["W036"] = takmOqlaOnly;

            map["W029"] = AllFamily(KabshaFamily, SkillLevel.Proficient).ToList(); // جيد جدا
            map["W030"] = AllFamily(KabshaFamily, SkillLevel.Proficient).ToList(); // جيد جدا
            map["W031"] = AllFamily(KabshaFamily, SkillLevel.Proficient).ToList(); // جيد جدا

            // W032 اسماعيل محمد اسماعيل
            map["W032"] = All("ماجيك")
                .Concat(Stages("وصله تي", "3 لفات خشن", "3 لفات ناعم"))
                .Concat(Stages("GRS", "دبله", "بطن خشن", "بطن ناعم"))
                .ToList();

            // W033 وليد احمد محمد
            map["W033"] = All("كوع بسن").Concat(All("وصله تي")).Concat(All("حنفيه بزبوز"))
                .Concat(Stages("GRS", "لفه 800"))
                .Concat(All("ماجيك"))
                .ToList();

            // W034 بدر عبدالعزيز السيد
            map["W034"] = All("كوع بسن").Concat(All("وصله تي")).Concat(All("حنفيه بزبوز"))
                .Concat(Stages("GRS", "لفه 600"))
                .Concat(All("ماجيك"))
                .ToList();

            // W035 احمد عاطف خيري
            map["W035"] = Stages("GRS", "لفه صغيره", "رقبه", "دبله", "بطن خشن", "بطن ناعم")
                .Concat(Stages("MG", "بطن 400", "رقبه", "دبله"))
                .ToList();

            // W037, W038, W039, W040, W041, W042 — كلهم وصفيين (تدريب/رص/جودة) مفيش روابط

            // W043 مصطفى محمود فهيم
            map["W043"] = Stages("كوع بسن", "عريض خشن", "عريض ناعم")
                .Concat(Stages("وصله تي", "بطن خشن", "3 لفات خشن", "3 لفات ناعم"))
                .Concat(Stages("GRS", "دبله", "رقبه", "ضربتين"))
                .Concat(Stages("MG", "دبله", "رقبه"))
                .Concat(Stages("ماكس فتيل", "رقبه", "ضربتين"))
                .Concat(Stages("ماجيك", "بطن خشن", "بطن ناعم", "لفه صغيره 600", "لفه ناعم"))
                .ToList();

            // W045 زياد عبدالرازق
            map["W045"] = All("GRS", exclude: "بطحتين")
                .Concat(Stages("كوع بسن", "عريض لمعه"))
                .Concat(Stages("وصله تي", "3 لفات خشن", "3 لفات ناعم"))
                .Concat(Stages("ماجيك", "لفه صغيره 600", "لفه ناعم", "بطن خشن", "بطن ناعم"))
                .Concat(Stages("MG", "ضربه", "دبله", "لفه 400", "لفه 800"))
                .ToList();

            return map;
        }
    }
}
