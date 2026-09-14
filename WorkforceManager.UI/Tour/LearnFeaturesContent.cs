using CommunityToolkit.Mvvm.ComponentModel;

namespace WorkforceManager.UI.Tour
{
    /// <summary>
    /// نسخة واحدة من "تعلم مميزات التحديث" — كل ميزة فيها HelpTopic عادي
    /// (نفس نوع كروت "الدليل" بالظبط، بوصفه وTourSteps بتاعته) بدل نوع
    /// جديد موازي. ObservableObject بس عشان IsExpanded (أكورديون
    /// الإصدارات في شاشة الدليل).
    /// </summary>
    public partial class LearnFeaturesVersion : ObservableObject
    {
        public required string Version { get; init; }
        public required IReadOnlyList<HelpTopic> Features { get; init; }

        [ObservableProperty]
        private bool _isExpanded;
    }

    /// <summary>
    /// محتوى "تعلم مميزات التحديث" — قايمة بالإصدارات، الأحدث أول (بيظهر
    /// فوق تلقائي في شاشة الدليل، وهو اللي App.OfferLearnFeaturesIfNewAsync
    /// بيقارنه بـ AppVersion.Current لقرار العرض التلقائي مرة واحدة).
    ///
    /// **صيانة — إزاي تضيف محتوى تحديث جديد مستقبلًا**: لما تزوّد
    /// &lt;Version&gt; في Directory.Build.props وقت أي إصدار جديد، ضيف
    /// عنصر LearnFeaturesVersion جديد هنا (فوق القايمة، قبل الإصدارات
    /// الأقدم) بنفس رقم الإصدار بالظبط، وHelpTopic واحد لكل ميزة حقيقية
    /// جديدة فيه. مفيش كود تاني مطلوب — العرض التلقائي أول مرة والتصفّح
    /// الدائم في شاشة الدليل بيشتغلوا لوحدهم بمجرد إضافة العنصر.
    /// </summary>
    public static class LearnFeaturesContent
    {
        public static IReadOnlyList<LearnFeaturesVersion> Versions { get; } = new List<LearnFeaturesVersion>
        {
            new()
            {
                Version = "1.6.5",
                IsExpanded = true, // أحدث إصدار — مفتوح افتراضيًا عشان يبان أول ما الشاشة تفتح
                Features = new List<HelpTopic>
                {
                    new()
                    {
                        Title = "تعديل وحذف وسجل الرصيد الأولي",
                        Description =
                            "قبل كده الرصيد الأولي كان بيتضاف بس من غير تعديل أو حذف حقيقي. دلوقتي تقدر تعدّل كمية " +
                            "أو نطاقات أي رصيد (الجزء المسحوب فعلًا مايقلّش عن حده)، تمسحه لو مالوش استخدام، أو " +
                            "تراجع \"سجل الرصيد\" اللي بيوريك كل عامل ومرحلة اشتغلوا على السحب.",
                        TourSteps = new List<AppTourStep>
                        {
                            new()
                            {
                                Title = "تعديل وحذف وسجل الرصيد الأولي",
                                Description = "من تبويب \"الرصيد الأولي\" في تسجيل الإنتاج اليومي.",
                                TargetElementName = "InitialBalanceHistoryToggle",
                                Screen = TourScreen.DailyEntry,
                                TabIndex = 1
                            }
                        }
                    },
                    new()
                    {
                        Title = "تحويل رصيد أولي لهالك مباشرة",
                        Description =
                            "شغل واقف اتعطّب ومش هيكمّل؟ تقدر تحوّله لهالك بكمية تختارها انت مباشرة من كارت الرصيد، " +
                            "من غير ما تعدّي بتسجيل إنتاج وهمي الأول.",
                        TourSteps = new List<AppTourStep>
                        {
                            new()
                            {
                                Title = "تحويل رصيد أولي لهالك",
                                Description = "زرار \"تحويل لهالك\" على كارت أي رصيد في تبويب \"الرصيد الأولي\".",
                                TargetElementName = "InitialBalanceHistoryToggle",
                                Screen = TourScreen.DailyEntry,
                                TabIndex = 1
                            }
                        }
                    },
                    new()
                    {
                        Title = "توقيع العمليات اليومية",
                        Description =
                            "بدل كلمة سر عند كل عملية حساسة، دلوقتي كلمة سر واحدة آخر اليوم بتغطّي كل حاجة حصلت — " +
                            "\"حفظ نهائي\" بيراجعك عليها قبل ما تقفل البرنامج. لو فاتك يوم، هتلاقي تذكير يجمّعهم لك " +
                            "أول ما تفتح البرنامج تاني.",
                        TourSteps = new List<AppTourStep>
                        {
                            new()
                            {
                                Title = "توقيع العمليات اليومية",
                                Description = "زرار \"حفظ نهائي\" في آخر القايمة الجانبية.",
                                TargetElementName = "FinalSaveButton",
                                Screen = TourScreen.None
                            }
                        }
                    },
                    new()
                    {
                        Title = "إلغاء \"قفل اليوم\"",
                        Description =
                            "ميزة قفل يوم معيّن ضد أي تعديل اتشالت خالص — أي يوم قابل للتعديل دلوقتي في أي وقت، " +
                            "حتى لو كان \"مقفول\" قبل كده. التوقيع اليومي فوق حاجة تانية خالص: مراجعة وتوثيق، مش قفل.",
                        TourSteps = new List<AppTourStep>
                        {
                            new()
                            {
                                Title = "كل يوم قابل للتعديل دايمًا",
                                Description = "من تسجيل الإنتاج اليومي — اختار أي تاريخ فات وعدّل فيه بحرية.",
                                TargetElementName = "DateNavPanel",
                                Screen = TourScreen.DailyEntry,
                                TabIndex = 0
                            }
                        }
                    },
                    new()
                    {
                        Title = "شاشة \"الذاكرة\" الجديدة",
                        Description =
                            "خطط إنتاج مؤجّلة بالكامل: اكتب \"عايز أعمل المنتج ده يوم كذا\" والبرنامج يفكّرك لما اليوم " +
                            "ده ييجي — بترتيب مراحل مخصّص لو محتاج، وتذكير بيظهر تلقائي أول ما تسجّل دخول.",
                        TourSteps = new List<AppTourStep>
                        {
                            new()
                            {
                                Title = "خطط إنتاج مؤجّلة",
                                Description = "شاشة \"الذاكرة\" جنب تسجيل الإنتاج اليومي في القايمة الجانبية.",
                                TargetElementName = "MemoryFormPanel",
                                Screen = TourScreen.Memory
                            }
                        }
                    },
                    new()
                    {
                        Title = "رسايل موحّدة بهوية البرنامج",
                        Description =
                            "كل رسالة أو تأكيد في البرنامج بقى بنفس التصميم الدهبي والعربي، بدل نافذة ويندوز البيضا " +
                            "بأزرار إنجليزي — تغيير مش بيبان كميزة لوحده، بس هتلاحظه في كل رسالة بتشوفها.",
                        TourSteps = new List<AppTourStep>
                        {
                            new()
                            {
                                Title = "رسايل موحّدة بهوية البرنامج",
                                Description = "جرّب أي زرار حذف أو حفظ حساس في أي شاشة، وشوف شكل الرسالة الجديد.",
                                TargetElementName = "SidebarLogo",
                                Screen = TourScreen.None
                            }
                        }
                    },
                    new()
                    {
                        Title = "اعتماد المصمّم وتاريخ الإصدار",
                        Description = "اسم المصمّم، رقم الإصدار الحالي، وتاريخ أول وآخر إصدار — في آخر شاشة الإعدادات.",
                        TourSteps = new List<AppTourStep>
                        {
                            new()
                            {
                                Title = "معلومات الإصدار والاعتماد",
                                Description = "آخر سطر في شاشة الإعدادات.",
                                TargetElementName = "AppInfoFooter",
                                Screen = TourScreen.Settings
                            }
                        }
                    },
                    new()
                    {
                        Title = "إصلاح تسجيل الخروج",
                        Description =
                            "تسجيل الخروج بقى بيقفل جلستك فعليًا ويفتح شاشة دخول نضيفة — أي حساب بيسجّل دخول بعدك " +
                            "بيشوف بياناته هو بس، من غير ما يلاقي حاجة متسربة من الحساب اللي قبله.",
                        TourSteps = new List<AppTourStep>
                        {
                            new()
                            {
                                Title = "تسجيل خروج نضيف لكل حساب",
                                Description = "زرار تسجيل الخروج جنب اسمك في آخر القايمة الجانبية.",
                                TargetElementName = "AccountCard",
                                Screen = TourScreen.None
                            }
                        }
                    },
                    new()
                    {
                        Title = "بحث سريع من أي شاشة",
                        Description = "دوّر على أي عامل أو منتج من أي شاشة انت فيها، من غير ما تفتح شاشته الأول.",
                        TourSteps = new List<AppTourStep>
                        {
                            new()
                            {
                                Title = "بحث سريع",
                                Description = "زرار \"بحث سريع\" فوق القايمة الجانبية.",
                                TargetElementName = "GlobalSearchButton",
                                Screen = TourScreen.None
                            }
                        }
                    },
                    new()
                    {
                        Title = "جولة \"إيه الجديد\" وشاشة \"الدليل\"",
                        Description =
                            "جولة سبوت لايت قصيرة بتظهر مرة واحدة لكل تحديث حقيقي، وشاشة \"الدليل\" اللي انت فاتحها " +
                            "دلوقتي — مرجع دائم لكل ميزة في البرنامج، ترجع لها في أي وقت.",
                        TourSteps = new List<AppTourStep>
                        {
                            new()
                            {
                                Title = "الدليل — مرجع دائم",
                                Description = "آخر عنصر في القايمة الجانبية، زي أي \"مساعدة\" في أي برنامج.",
                                TargetElementName = "NavHelpItem",
                                Screen = TourScreen.None
                            }
                        }
                    },
                    new()
                    {
                        Title = "وضوح أعلى على كل الشاشات",
                        Description =
                            "الشاشة والنوافذ بقوا بيتظبطوا لوحدهم مع أي حجم أو دقّة شاشة — النصوص والحدود بقت حادة " +
                            "وواضحة بدل ما تبان مغبّشة على بعض الأجهزة، من غير ما تحتاج تعمل أي حاجة.",
                        TourSteps = new List<AppTourStep>
                        {
                            new()
                            {
                                Title = "وضوح أعلى تلقائيًا",
                                Description = "تحسين شامل مش مربوط بشاشة معيّنة — هتلاحظه في كل حاجة بتفتحها.",
                                TargetElementName = "SidebarLogo",
                                Screen = TourScreen.None
                            }
                        }
                    },
                    new()
                    {
                        Title = "الحسابات الإدارية",
                        Description =
                            "دخول منفصل لرؤساء ومديري الأقسام، بصلاحيات مختلفة عن باقي العمال — مدير القسم بس بيضيف " +
                            "أو يعدّل أو يمسح، وأي حساب تاني بيشوف بروفايله هو بس.",
                        TourSteps = new List<AppTourStep>
                        {
                            new()
                            {
                                Title = "الحسابات الإدارية",
                                Description = "شاشة \"الحسابات الإدارية\" في القايمة الجانبية.",
                                TargetElementName = "AccountsListCard",
                                Screen = TourScreen.DepartmentAccounts
                            }
                        }
                    },
                    new()
                    {
                        Title = "ألقاب \"أحسن عامل\" الرسمية",
                        Description =
                            "لقب أسبوعي وشهري رسمي بيتحسب لوحده ويفضل معلّق على العامل لحد ما عامل تاني ياخده — " +
                            "منفصل عن كارت \"أحسن 3 عمال\" اللحظي في شاشة العمال.",
                        TourSteps = new List<AppTourStep>
                        {
                            new()
                            {
                                Title = "أحسن عامل الأسبوع/الشهر",
                                Description = "بتشوفه كلقب دهبي تحت اسم العامل في قايمة شاشة العمال.",
                                TargetElementName = "BestWorkerCardsRow",
                                Screen = TourScreen.Workers
                            }
                        }
                    },
                    new()
                    {
                        Title = "فورمات قسايم أجر محفوظة",
                        Description =
                            "احفظ مجموعات بنود قسيمة باسم (\"الفورمات الكامل\"، \"مختصر\"...) وبدّل بينها بضغطة، " +
                            "وقسايم الفريق كله بتتطبع مع بعض على نفس الورقة جاهزة للتقصيص.",
                        TourSteps = new List<AppTourStep>
                        {
                            new()
                            {
                                Title = "فورمات قسايم الأجر",
                                Description = "من تقرير \"أجور\" في شاشة التقارير.",
                                TargetElementName = "PayslipFieldToggle",
                                Screen = TourScreen.Reports
                            }
                        }
                    },
                    new()
                    {
                        Title = "وضع تجربة تفاعلي — \"جرّبها بنفسك\"",
                        Description =
                            "أعمق طريقة تعلّم في الدليل: العنصر الحقيقي بيبقى قابل للدوس فعليًا وانت بتتفرّج، " +
                            "والخطوة بتكمّل لوحدها لما تعملها بجد — على بيانات وهمية معزولة، مش بيانات مصنعك الحقيقية.",
                        TourSteps = new List<AppTourStep>
                        {
                            new()
                            {
                                Title = "جرّبها بنفسك",
                                Description = "دوّر على قسم \"تمارين عملية\" جوّه أي كارت شاشة في الدليل ده.",
                                TargetElementName = "ToggleAddSkillsButton",
                                Screen = TourScreen.Workers,
                                SelectFirstWorker = true
                            }
                        }
                    },
                    new()
                    {
                        Title = "تنقّل أسرع بالتاريخ في التسجيل اليومي",
                        Description = "سهمين يوم فات/جاي، وزرار \"النهارده\" يرجعك على طول — بدل ما تفتح التقويم كل مرة.",
                        TourSteps = new List<AppTourStep>
                        {
                            new()
                            {
                                Title = "تنقّل التاريخ",
                                Description = "فوق كل تبويبات تسجيل الإنتاج اليومي.",
                                TargetElementName = "DateNavPanel",
                                Screen = TourScreen.DailyEntry,
                                TabIndex = 0
                            }
                        }
                    },
                    new()
                    {
                        Title = "حماية من الحفظ المزدوج",
                        Description =
                            "دوسة سريعتين بالغلط على زرار حفظ أو حذف مبقتش بتسجّل العملية مرتين — الزرار بيتعطّل " +
                            "لوحده لحد ما العملية الأولى تخلص.",
                        TourSteps = new List<AppTourStep>
                        {
                            new()
                            {
                                Title = "حماية من الحفظ المزدوج",
                                Description = "تحسين شامل على كل زرار حفظ/حذف في البرنامج — مش مربوط بشاشة معيّنة.",
                                TargetElementName = "SidebarLogo",
                                Screen = TourScreen.None
                            }
                        }
                    },
                    new()
                    {
                        Title = "سلّم يوميات العمال بالساعة",
                        Description =
                            "عمال بالساعة (رص، جودة، تدريب) بقى ليهم سلّم يوميات واضح حسب وقت انصرافهم — شيفت عادي، " +
                            "لحد 8م، أو لحد 12، وكل واحد بياخد عدد يوميات مختلف.",
                        TourSteps = new List<AppTourStep>
                        {
                            new()
                            {
                                Title = "شيفتات العمال بالساعة",
                                Description = "من تبويب \"الحضور والغياب\" — كل عامل بالساعة عنده شرايط شيفت بدل حالة حضور عادية.",
                                TargetElementName = "AttendanceSummaryRow",
                                Screen = TourScreen.DailyEntry,
                                TabIndex = 3
                            }
                        }
                    }
                }
            }
        };
    }
}
