namespace WorkforceManager.UI.Tour
{
    /// <summary>
    /// محتوى شاشة "الدليل" — موضوع واحد لكل شاشة في القائمة الجانبية، ما عدا
    /// "تسجيل الإنتاج اليومي" اللي اتفكّك لـ7 مواضيع (واحد لكل تبويب داخلي،
    /// فيه تفاصيل كتير جدًا عشان يتغطّى في موضوع واحد بمعنى حقيقي).
    ///
    /// كل خطوة مبنية على عنصر وميزة حقيقيين في الكود (أمر/Binding حقيقي)،
    /// مش وصف عام. الخطوات بتركّز على الميزات اللي فعلًا بتتستخدم كتير —
    /// مش كل عنصر موجود في الشاشة.
    ///
    /// **صيانة**: أي شاشة جديدة تتضاف للبرنامج لازم تاخد موضوع هنا كمان —
    /// وإلا الدليل يبقى ناقص من غير ما حد ياخد باله، لأن مفيش فحص آلي
    /// بيقارن عدد عناصر القائمة الجانبية بعدد المواضيع هنا.
    /// </summary>
    public static class HelpTopics
    {
        public static IReadOnlyList<HelpTopic> All { get; } = new List<HelpTopic>
        {
            new()
            {
                Title = "العمال",
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
                        Description = "بدل صف أزرار تاني، فلاتر المرحلة والمنتج والنجوم والحضور كلها هنا وبتتراكب مع بعض.",
                        TargetElementName = "FilterToggle",
                        Screen = TourScreen.Workers
                    },
                    new()
                    {
                        Title = "عامل جديد",
                        Description = "إضافة عامل جديد بصورته وبياناته الأساسية.",
                        TargetElementName = "AddWorkerButton",
                        Screen = TourScreen.Workers
                    },
                    new()
                    {
                        Title = "محتاج انتباه",
                        Description = "لو فيه عمال إنتاجهم قلّ أو محتاجين متابعة، الزرار ده بيعرضهم لوحدهم — بيختفي لو مفيش حد محتاج انتباه دلوقتي.",
                        TargetElementName = "ShowNeedsAttentionButton",
                        Screen = TourScreen.Workers
                    }
                }
            },
            new()
            {
                Title = "المنتجات والمراحل",
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
                        Description = "بالإفتراض بتشوف المنتجات اللي اشتغلت الفترة دي بس — بدّل الفترة لو عايز تشوف كل المنتجات.",
                        TargetElementName = "PeriodToggle",
                        Screen = TourScreen.Products
                    },
                    new()
                    {
                        Title = "منتج جديد",
                        Description = "إضافة منتج جديد — بعدها تقدر تضيفله مراحل إنتاجه بالترتيب.",
                        TargetElementName = "AddProductButton",
                        Screen = TourScreen.Products
                    }
                }
            },
            new()
            {
                Title = "الذاكرة",
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
                        Description = "اختار المنتج، رتّب مراحله لو عايز ترتيب مختلف عن خط الإنتاج العادي، وحدّد يوم التذكير.",
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
                        Description = "من كارت الخطة نفسه: ابدأها دلوقتي، أجّل التذكير، احذفها، أو رجّعها نشطة لو اتعلّمت منجزة غلط.",
                        TargetElementName = "MemoryListsPanel",
                        Screen = TourScreen.Memory
                    }
                }
            },
            new()
            {
                Title = "التقييم والمتابعة",
                Description =
                    "لوحة متابعة حيّة تتقرا على الشاشة، مش للتصدير أو الطباعة (شوف \"التقارير\" " +
                    "لده). فيها إنتاج اليوم/الأسبوع/الشهر لكل منتج مع الهالك، رسم بياني لإنتاج " +
                    "المنتجات بمرور الوقت، ومتوسط إنتاج كل عامل — مع تنبيه لأي عامل إنتاجه قلّ عن معدّله المعتاد.",
                TourSteps = new List<AppTourStep>
                {
                    new()
                    {
                        Title = "تجميع الفترة",
                        Description = "شوف أرقام اليوم، أو اجمعها بالأسبوع أو الشهر.",
                        TargetElementName = "OutputGrainRow",
                        Screen = TourScreen.Evaluation
                    },
                    new()
                    {
                        Title = "ملخص سريع",
                        Description = "قطعة خلصت الخط كامل، قطعة دخلت الخط، وقطعة هالك — أهم 3 أرقام في الفترة المختارة.",
                        TargetElementName = "KpiSummaryRow",
                        Screen = TourScreen.Evaluation
                    },
                    new()
                    {
                        Title = "فلترة بمنتج",
                        Description = "ضيّق الرسم البياني والأرقام على منتج معيّن بدل كل المنتجات مع بعض.",
                        TargetElementName = "ProductToggle",
                        Screen = TourScreen.Evaluation
                    }
                }
            },
            new()
            {
                Title = "التقارير",
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
                        Description = "أسرع طريقة لتقرير بتعمله بانتظام — اختار قالب محفوظ وكل الاختيارات بتتملى لوحدها.",
                        TargetElementName = "TemplateComboBox",
                        Screen = TourScreen.Reports
                    },
                    new()
                    {
                        Title = "الموضوع",
                        Description = "أول وأهم اختيار — كل حاجة تانية في الشاشة بتتغيّر حسبه.",
                        TargetElementName = "SubjectComboBox",
                        Screen = TourScreen.Reports
                    },
                    new()
                    {
                        Title = "المعاينة الحية",
                        Description = "الجدول ده بيتحدّث لحظيًا مع كل اختيار قبل ما تصدّر أي حاجة.",
                        TargetElementName = "PreviewGrid",
                        Screen = TourScreen.Reports
                    },
                    new()
                    {
                        Title = "تصدير Excel",
                        Description = "التقرير جاهز؟ يتصدّر ملف Excel تقدر تفتحه أو تطبعه على طول.",
                        TargetElementName = "ExportButton",
                        Screen = TourScreen.Reports
                    }
                }
            },
            new()
            {
                Title = "سجل العمليات",
                Description =
                    "أرشيف لكل عملية حساسة حصلت في البرنامج — حذف، تعديل مالي (أجر/جزاء/سلفة)، " +
                    "تصحيح إنتاج — بتاريخها وسببها ومين عملها. الأحداث القديمة بتتمسح تلقائيًا بعد " +
                    "مدة معيّنة (تقدر تظبطها من الإعدادات).",
                TourSteps = new List<AppTourStep>
                {
                    new()
                    {
                        Title = "قايمة الأحداث",
                        Description = "كل عملية حساسة حصلت في البرنامج، بأيقونة وتاريخ وسبب واضح.",
                        TargetElementName = "ActivityLogList",
                        Screen = TourScreen.ActivityLog
                    },
                    new()
                    {
                        Title = "فلترة بالنوع",
                        Description = "دوّر على حركة فلوس بس، أو حذف بس، بدل ما تعدّي على كل حدث في الشهر.",
                        TargetElementName = "EventGroupComboBox",
                        Screen = TourScreen.ActivityLog
                    },
                    new()
                    {
                        Title = "فترات جاهزة",
                        Description = "النهارده أو آخر 30 يوم بضغطة واحدة، بدل ما تختار تاريخين من التقويم.",
                        TargetElementName = "QuickRangeButton",
                        Screen = TourScreen.ActivityLog
                    }
                }
            },
            new()
            {
                Title = "الإعدادات والنسخ",
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
                        Description = "بتحمي الحذف والتعديلات المالية — أهم إعداد حماية في البرنامج كله.",
                        TargetElementName = "SetPasswordButton",
                        Screen = TourScreen.Settings
                    },
                    new()
                    {
                        Title = "النسخ الاحتياطي المحلي",
                        Description = "نسخة تلقائية كل يوم — من هنا تقدر تاخد نسخة دلوقتي أو تفتح مجلد النسخ.",
                        TargetElementName = "BackupCard",
                        Screen = TourScreen.Settings
                    },
                    new()
                    {
                        Title = "النسخ الخارجي",
                        Description = "نسخة على فلاشة أو قرص تاني — النسخة المحلية بيانها بتتحمي من تلف الهارد نفسه بالنسخة دي بس.",
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
                        Description = "زرار مدير القسم بس — بيضيف رئيس/مدير قسم جديد بتسجيل دخول خاص بيه.",
                        TargetElementName = "AddAccountButton",
                        Screen = TourScreen.DepartmentAccounts
                    },
                    new()
                    {
                        Title = "أنهي وضع أنت فيه",
                        Description = "العنوان هنا بيوضّحلك: بتشوف كل الحسابات كمدير قسم، ولا بروفايلك أنت بس.",
                        TargetElementName = "ScreenTitleRow",
                        Screen = TourScreen.DepartmentAccounts
                    }
                }
            },
            new()
            {
                Title = "تسجيل الإنتاج اليومي — تسجيل الإنتاج",
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
                        Description = "كل منتج بتشتغل عليه النهارده بيبقى له رحلة منفصلة هنا — تقدر تفتح أكتر من منتج مع بعض.",
                        TargetElementName = "AddFlowSessionButton",
                        Screen = TourScreen.DailyEntry,
                        TabIndex = 0
                    }
                }
            },
            new()
            {
                Title = "تسجيل الإنتاج اليومي — الرصيد الأولي",
                Description =
                    "أي إنتاج ماوصلش لآخر مرحلة في الخط بيتحول تلقائيًا لـ\"رصيد أولي\" — شغل واقف " +
                    "منتظر يكمّل. هنا تديره: تسحب منه لما تكمّل الشغل، تحوّله لهالك لو اتعطّب، أو " +
                    "تراجع رصيد اتسحب بالكامل من السجل.",
                TourSteps = new List<AppTourStep>
                {
                    new()
                    {
                        Title = "رصيد أولي جديد",
                        Description = "لو عندك شغل واقف من غير ما يتحول تلقائي (نادر، بس ممكن)، تقدر تضيفه يدوي من هنا.",
                        TargetElementName = "AddInitialBalanceButton",
                        Screen = TourScreen.DailyEntry,
                        TabIndex = 1
                    },
                    new()
                    {
                        Title = "السجل",
                        Description = "الأرصدة اللي خلصت واستُخدمت بالكامل بتتخبّى من القايمة العادية — بتلاقيها هنا للمراجعة.",
                        TargetElementName = "InitialBalanceHistoryToggle",
                        Screen = TourScreen.DailyEntry,
                        TabIndex = 1
                    }
                }
            },
            new()
            {
                Title = "تسجيل الإنتاج اليومي — سجلات اليوم",
                Description =
                    "مراجعة وتصحيح إنتاج اتسجّل قبل كده — بفترة (يوم/أسبوع/شهر) مستقلة عن تاريخ " +
                    "التسجيل فوق. تقدر تعدّل عدد قطع سجل، تمسحه، أو تتراجع عن آخر عملية بـCtrl+Z.",
                TourSteps = new List<AppTourStep>
                {
                    new()
                    {
                        Title = "سجلات اليوم",
                        Description = "كل سجل إنتاج في الفترة المختارة، بأزرار تعديل وحذف لكل صف.",
                        TargetElementName = "RecordsTabRoot",
                        Screen = TourScreen.DailyEntry,
                        TabIndex = 2
                    },
                    new()
                    {
                        Title = "فترة العرض",
                        Description = "يوم، أسبوع، أو شهر — مستقلة تمامًا عن تاريخ التسجيل في باقي التبويبات.",
                        TargetElementName = "RecordsGrainRow",
                        Screen = TourScreen.DailyEntry,
                        TabIndex = 2
                    }
                }
            },
            new()
            {
                Title = "تسجيل الإنتاج اليومي — الحضور والغياب",
                Description =
                    "الحضور بيتسجّل تلقائي لأي عامل شارك في تسجيل إنتاج النهارده، بس تقدر من هنا " +
                    "تراجع وتعدّل: حاضر، غايب بعذر، غايب من غير عذر (بيسحب نص يوم تلقائي)، أو لسه " +
                    "مش متسجّل.",
                TourSteps = new List<AppTourStep>
                {
                    new()
                    {
                        Title = "ملخص الحضور",
                        Description = "عدد الحاضرين والغائبين بعذر ومن غير عذر — دوس على أي رقم عشان تفلتر القايمة عليه.",
                        TargetElementName = "AttendanceSummaryRow",
                        Screen = TourScreen.DailyEntry,
                        TabIndex = 3
                    },
                    new()
                    {
                        Title = "حفظ الحضور",
                        Description = "أي تعديل يدوي على حالة عامل لازم يتحفظ من هنا.",
                        TargetElementName = "SaveAttendanceButton",
                        Screen = TourScreen.DailyEntry,
                        TabIndex = 3
                    }
                }
            },
            new()
            {
                Title = "تسجيل الإنتاج اليومي — الجزاءات",
                Description =
                    "تسجيل جزاء (خصم يوم أو جزء منه) على عامل، بسبب مكتوب — غياب من غير عذر بيسجّل " +
                    "جزاءه التلقائي هنا، وتقدر تضيف جزاءات يدوية لأسباب تانية.",
                TourSteps = new List<AppTourStep>
                {
                    new()
                    {
                        Title = "تسجيل جزاء",
                        Description = "اختار العامل، مقدار الخصم، واكتب السبب.",
                        TargetElementName = "AddPenaltyButton",
                        Screen = TourScreen.DailyEntry,
                        TabIndex = 4
                    },
                    new()
                    {
                        Title = "جزاءات اليوم",
                        Description = "كل جزاء اتسجّل النهارده — يدوي كان أو تلقائي من غياب — بتفاصيله.",
                        TargetElementName = "PenaltiesGrid",
                        Screen = TourScreen.DailyEntry,
                        TabIndex = 4
                    }
                }
            },
            new()
            {
                Title = "تسجيل الإنتاج اليومي — السلف والحوافز",
                Description =
                    "سلفة (مبلغ بيتخصم من أجر العامل في كشف الفترة) أو حافز (مبلغ بيتزاد على أجره) " +
                    "— بمبلغ بالجنيه وملاحظة اختيارية.",
                TourSteps = new List<AppTourStep>
                {
                    new()
                    {
                        Title = "تسجيل سلفة أو حافز",
                        Description = "اختار العامل والنوع (سلفة تتخصم، أو حافز يتزاد) والمبلغ.",
                        TargetElementName = "AddAdjustmentButton",
                        Screen = TourScreen.DailyEntry,
                        TabIndex = 5
                    },
                    new()
                    {
                        Title = "حركات اليوم",
                        Description = "كل سلفة وحافز اتسجّل النهارده، بلون مختلف حسب النوع.",
                        TargetElementName = "AdjustmentsGrid",
                        Screen = TourScreen.DailyEntry,
                        TabIndex = 5
                    }
                }
            },
            new()
            {
                Title = "تسجيل الإنتاج اليومي — الهالك",
                Description =
                    "قطع اتشالت ومش هتتكمّل. الهالك على آخر مرحلة بيتخصم من الإنتاج التام، واللي في " +
                    "نص الخط بيتشال من الشغل الواقف (الرصيد الأولي) — مالوش أي تأثير على الأجور.",
                TourSteps = new List<AppTourStep>
                {
                    new()
                    {
                        Title = "تسجيل هالك",
                        Description = "اختار المرحلة وعدد القطع وسبب الهالك.",
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
        };
    }
}
