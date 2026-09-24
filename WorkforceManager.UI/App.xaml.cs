using System.IO;
using System.Windows;
using System.Windows.Media;
using MaterialDesignColors;
using MaterialDesignThemes.Wpf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Data;

namespace WorkforceManager.UI
{
    public partial class App : Application
    {
        /// <summary>
        /// مضيف التطبيق (Host) اللي بيدير كل الاعتماديات (Dependency Injection).
        /// ده اللي بيخلي كل طبقة (UI / Business / Data) منفصلة ومربوطة ببعض
        /// بشكل نظيف من غير ما أي طبقة تعمل "new" لطبقة تانية يدويًا.
        /// </summary>
        public static IHost AppHost { get; private set; } = null!;

        public App()
        {
            AppCulture.Pin();

            AppHost = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // مسار قاعدة البيانات: مجلد بيانات التطبيق الخاص بالمستخدم (مش داخل مجلد البرنامج نفسه)
                    Directory.CreateDirectory(AppPaths.DataFolder);

                    services.AddWorkforceManagerCore($"Data Source={AppPaths.DbPath}");
                })
                .Build();
        }

        /// <summary>
        /// قفل النسخة الواحدة: بيمنع فتح نسختين من البرنامج في نفس الوقت —
        /// نسختين بيكتبوا على نفس قاعدة الـ SQLite بيعرّضوا البيانات للتعارض
        /// والنسخ الاحتياطي للخبطة. بيفضل ممسوك طول عمر البرنامج.
        /// </summary>
        private static Mutex? _singleInstanceMutex;

        /// <summary>
        /// بيتأكد إن مجلد البيانات موجود وينفع الكتابة فيه **قبل** ما SQLite
        /// يلمسه.
        ///
        /// البيانات بقت في %ProgramData%، وصلاحية الكتابة عليه بتتحدد من ملف
        /// التثبيت. لو البرنامج اتنسخ يدوي من غير تثبيت، أو حد غيّر صلاحيات
        /// المجلد، SQLite بيقع بـ "unable to open database file" — رسالة
        /// مالهاش أي معنى للمستخدم ولا بتقوله يعمل إيه. الفحص ده بيقول
        /// المشكلة والمكان والعلاج.
        ///
        /// الكتابة الفعلية هي الاختبار الوحيد المعتبر: وجود المجلد أو قراءة
        /// صلاحياته مش بيقولوا إن الكتابة هتنفع.
        /// </summary>
        private static bool EnsureDataFolderWritable()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataFolder);

                var probe = Path.Combine(AppPaths.DataFolder, ".write-probe");
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
                return true;
            }
            catch (Exception ex)
            {
                Notify.Error(
                    "البرنامج مش قادر يكتب في مجلد البيانات:\n\n" +
                    $"{AppPaths.DataFolder}\n\n" +
                    $"({ex.Message})\n\n" +
                    "شغّل ملف التثبيت تاني، أو ادّي المجلد ده صلاحية تعديل للمستخدمين.",
                    "مشكلة في صلاحيات مجلد البيانات");
                return false;
            }
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            // قبل أي نافذة تتعمل: كل نوافذ البرنامج ترسم حادّة على
            // الشاشات المكبّرة، مش MainWindow لوحدها (شوف CrispWindows)
            CrispWindows.Enable();

            // منع تشغيل نسخة تانية من البرنامج (النسخة الأولى بتفضل هي الشغالة)
            _singleInstanceMutex = new Mutex(true, @"Local\WorkforceManager_SingleInstance", out var isFirstInstance);
            if (!isFirstInstance)
            {
                Notify.Info("البرنامج مفتوح بالفعل — استخدم النافذة المفتوحة.\n(فتح نسختين في نفس الوقت ممكن يبوّظ البيانات)", "البرنامج شغال");
                Shutdown();
                return;
            }

            // معالج أخطاء عام: أي استثناء غير متوقع يوصل لخيط الواجهة بيظهر
            // للمستخدم برسالة واضحة بدل ما البرنامج يقفل فجأة من غير سبب مفهوم
            DispatcherUnhandledException += (_, args) =>
            {
                Notify.Warn($"حصل خطأ غير متوقع:\n\n{args.Exception.Message}", "خطأ");
                args.Handled = true; // منع إغلاق البرنامج بسبب الخطأ
            };

            // قبل أي حاجة تلمس القرص — لو المجلد مش قابل للكتابة نقول السبب
            // والمكان بدل ما SQLite يقع بعطل مالهوش معنى للمستخدم
            if (!EnsureDataFolderWritable())
            {
                Shutdown();
                return;
            }

            // المظهر بيتحدد قبل ما أي شاشة تتبني: الشاشات بتقرا الألوان
            // بـ StaticResource اللي بتتحل مرة واحدة وقت التحميل، فالتبديل
            // لازم يحصل هنا مش بعدين
            ApplyTheme(AppSettingsStore.Load().DarkMode);

            try
            {
                await AppHost.StartAsync();

                using (var scope = AppHost.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    // نسخة احتياطية يومية قبل أي تعديل على قاعدة البيانات، عشان لو
                    // الـ Migration فشل لأي سبب تفضل عندنا نسخة سليمة من قبل التعديل —
                    // محليًا + خارجيًا لو المستخدم مفعّل مجلد خارجي من الإعدادات
                    var settings = AppSettingsStore.Load();

                    // المستخدم يقدر يقفل النسخة التلقائية من الإعدادات —
                    // قراره، وهو اللي بيتحمّل نتيجته
                    if (settings.AutoBackupOnStartup)
                        DatabaseBackupService.RunDailyBackup(
                            AppPaths.DbPath, settings.ExternalBackupFolder, settings.BackupRetentionDays);

                    // تطبيق أي Migration جديدة تلقائيًا (بيُنشئ قاعدة البيانات من الصفر
                    // لو مش موجودة أصلاً) — بديل EnsureCreatedAsync عشان تحديثات
                    // النماذج المستقبلية تتطبق على قاعدة بيانات العميل الحالية من
                    // غير ما نمسح بياناته
                    await db.Database.MigrateAsync();

                    // ترحيل تلقائي لأي شغل واقف كان موجود قبل فيتشر الرصيد
                    // الأولي — مرة واحدة بس (حارس idempotency داخلها)، وقبل
                    // البذر عشان تطبق على بيانات العميل الحقيقية أول ما
                    // تتوفر. جوه معاملة واحدة فمفيش حالة نص متحوّلة لو فشلت
                    await scope.ServiceProvider.GetRequiredService<HistoricalPendingMigrationService>()
                        .RunOnceAsync();

                    await DatabaseSeeder.SeedIfEmptyAsync(db);

                    // أول تشغيل: إنشاء حساب الدخول الافتراضي لو مفيش مستخدمين
                    await scope.ServiceProvider.GetRequiredService<AuthService>().EnsureDefaultUserAsync();

                    // لازم بعد EnsureDefaultUserAsync مباشرة: أول حساب دخول
                    // (admin الافتراضي على تركيب جديد، أو أقدم حساب على قاعدة
                    // عميل قديمة) بيتحوّل لأول "مدير قسم" في الحسابات الإدارية
                    await DatabaseSeeder.SeedDefaultDepartmentManagerAsync(db);
                }

                // شاشة الدخول الأول — من غير دخول ناجح البرنامج مش بيفتح.
                // أثناء شاشة الدخول بنمنع الإغلاق التلقائي (مفيش نافذة رئيسية لسه)
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var login = new Views.LoginWindow();
                if (login.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }

                // لحاق أيام ما اتوقّعتش (البرنامج قفل فجأة قبل ما توقّع
                // نهاية يومها — كرش، إغلاق قسري، انقطاع كهرباء). شرط أمان
                // مش تنظيف: **عن قصد مش جوه try/catch** زي فحوصات بدء
                // التشغيل التانية تحت — فشله لازم يوقف البرنامج بدل ما
                // يعدّي بصمت ويسيب أيام بلا أثر إقرار
                await EnsureLateSignOffsAcknowledgedAsync();

                // تنظيف سجل العمليات من اللي عدّى مدة الاحتفاظ.
                //
                // بعد النسخة الاحتياطية عن قصد (اللي فوق): أي حدث بيتمسح
                // هنا لسه موجود في نسخة النهارده، فالغلط بيرجع.
                //
                // فشله عمره ما يمنع البرنامج من الفتح — ده تنظيف، مش شرط
                // تشغيل. نفس مبدأ النسخ الخارجي.
                try
                {
                    using var purgeScope = AppHost.Services.CreateScope();
                    var settings = AppSettingsStore.Load();
                    await purgeScope.ServiceProvider.GetRequiredService<ActivityLogService>()
                        .PurgeExpiredAsync(
                            settings.ActivityLogRetentionDays,
                            settings.ActivityLogFinancialRetentionDays);

                    // الصفوف اللي اتشالت أيام ما الحذف كان بيعلّم بس —
                    // بتتنضّف بنفس قاعدة الحذف الحالية
                    await DeletedRowsCleaner.PurgeAsync(
                        purgeScope.ServiceProvider.GetRequiredService<AppDbContext>());

                    // إنتاج فعلي (ProductionStageOutput) بقى شبح كامل بسبب
                    // حذف إنتاج قبل ما هذا الإصلاح يتحط — بيتصحح مرة واحدة
                    // هنا لأي نسخة عميل قديمة، ومش بيلمس أي صف لسه له دعم جزئي
                    await purgeScope.ServiceProvider.GetRequiredService<ProductionStageOutputService>()
                        .RemoveFullyOrphanedRowsAsync();
                }
                catch
                {
                    // متجاهَل عن قصد — شوف الكومنت فوق
                }

                // تعبية حضور "حاضر" التلقائي للحسابات الإدارية (مدير/رئيس
                // قسم) — البرنامج مالوش نظام مهام مجدولة، فده مكانها
                // الوحيد. فشلها عمره ما يمنع البرنامج من الفتح، زي التنظيف فوق.
                try
                {
                    using var deptScope = AppHost.Services.CreateScope();
                    await deptScope.ServiceProvider.GetRequiredService<DepartmentAttendanceService>()
                        .EnsureDailyPresenceAsync();
                }
                catch
                {
                    // متجاهَل عن قصد — شوف الكومنت فوق
                }

                // تسجيل ألقاب "أحسن عامل" الأسبوعية/الشهرية اللي فترتها
                // قفلت من آخر تشغيل — نفس مبدأ التعبية فوق (مفيش نظام مهام
                // مجدولة، فبدء التشغيل هو نقطة اللحاق الوحيدة)
                try
                {
                    using var recognitionScope = AppHost.Services.CreateScope();
                    await recognitionScope.ServiceProvider.GetRequiredService<WorkerRecognitionService>()
                        .AwardTitlesForClosedPeriodsAsync();
                }
                catch
                {
                    // متجاهَل عن قصد — شوف الكومنت فوق
                }

                var mainWindow = StartSession();

                // تذكيرات الذاكرة بعد ما النافذة تبان فعلاً: التذكير
                // بياخدها Owner (وOwner على نافذة لسه ماتعرضتش بيرمي)،
                // وكمان المستخدم لازم يشوف البرنامج ورا التذكير مش
                // نافذة معلّقة في الفراغ
                // التجاهل هنا لـ DispatcherOperation مش لـ Task، فمش
                // بيخالف قاعدة "متستخدمش ‎_ = SomeAsync()": الشغل غير
                // المتزامن نفسه جوّه SafeAsync.Run، فأي فشل بيظهر
                // للمستخدم مش بيضيع في صمت
                //
                // جولة "إيه الجديد" بعد التذكيرات عن قصد — مفيش حاجتين
                // بيتنافسوا على انتباه المستخدم في نفس اللحظة
                _ = mainWindow.Dispatcher.BeginInvoke(
                    new Action(() => ViewModels.SafeAsync.Run(async () =>
                    {
                        await ShowDueMemoryRemindersAsync(mainWindow);
                        await OfferAppTourIfNewAsync(mainWindow);
                        OfferLearnFeaturesIfNew(mainWindow);
                    })),
                    System.Windows.Threading.DispatcherPriority.Background);

                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                // فشل بدء التشغيل (قاعدة بيانات مقفولة/تالفة، مساحة قرص،
                // خطأ في بناء الواجهة...): نعرض السبب ونقفل بأمان بدل ما
                // البرنامج يختفي من غير رسالة.
                //
                // **Notify.Error مش Warn**: ده فشل، والفشل لازم يوقف
                // ويستنى — والإشعار الطاير بيروح لوحده.
                var logPath = WriteCrashLog(ex);

                Notify.Error(
                    $"تعذّر بدء تشغيل البرنامج:\n\n{ex.Message}\n\n" +
                    $"التفاصيل الكاملة اتكتبت في:\n{logPath}",
                    "خطأ في بدء التشغيل");

                Shutdown(-1);
            }
        }

        /// <summary>
        /// لحاق أيام ما اتوقّعتش عند بدء التشغيل — بيفتح ديالوج حاجز
        /// (<see cref="Views.LateSignOffCatchUpDialog"/>) لو فيه أي يوم
        /// فايت عليه نشاط ومحدش وقّعه، ومايرجعش لحد ما المستخدم يقرّ
        /// بباسورد عملياته. مفيش حاجة تحصل لو مفيش أيام معلّقة (الحالة
        /// العادية كل مرة البرنامج بيتقفل بشكل طبيعي عن طريق زرار "حفظ
        /// نهائي" أو منع الإغلاق في MainWindow).
        /// </summary>
        /// <summary>
        /// نطاق الجلسة الحالية — النافذة الرئيسية وكل اللي جوّاها بيتولدوا
        /// منه. تسجيل الخروج بيتخلص منه بالكامل، فمفيش حاجة من حساب
        /// بتعيش لحساب بعده.
        /// </summary>
        private static IServiceScope? _sessionScope;

        /// <summary>
        /// بيفتح جلسة جديدة: نطاق نضيف، نافذة رئيسية جديدة منه، وعرضها.
        ///
        /// <see cref="ShutdownMode"/> بيترجّع لـ OnMainWindowClose هنا —
        /// كان OnExplicitShutdown أثناء شاشة الدخول (ومن تسجيل الخروج)
        /// عشان قفل النافذة القديمة أو إلغاء الدخول ما يقفلش البرنامج
        /// في اللحظة الغلط.
        /// </summary>
        private static MainWindow StartSession()
        {
            _sessionScope = AppHost.Services.CreateScope();

            var window = _sessionScope.ServiceProvider.GetRequiredService<MainWindow>();

            Current.MainWindow = window;
            Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Show();

            return window;
        }

        /// <summary>
        /// تسجيل خروج: بيقفل الجلسة الحالية بالكامل ويرجّع لشاشة الدخول.
        ///
        /// **بوابة التوقيع بتتنفّذ في المنادي قبل ما يوصل هنا** — الدالة
        /// دي بتنفّذ الخروج مش بتقرره.
        ///
        /// الترتيب مهم: ShutdownMode بيتحوّل الأول، لأن قفل النافذة
        /// الرئيسية وهو على OnMainWindowClose بيقفل البرنامج كله بدل ما
        /// يوصل لشاشة الدخول.
        /// </summary>
        public static void SignOutAndRestartSession()
        {
            Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            AppHost.Services.GetRequiredService<CurrentUserContext>().SignOut();

            // النافذة بتتقفل فعليًا مش بتتخبى — ومعاها كل نوافذها التابعة
            if (Current.MainWindow is { } old)
            {
                Current.MainWindow = null;
                old.Close();
            }

            // ودي اللي بتصفّي الـ ViewModels والـ DbContexts بتوع الجلسة
            _sessionScope?.Dispose();
            _sessionScope = null;

            if (new Views.LoginWindow().ShowDialog() != true)
            {
                Current.Shutdown();
                return;
            }

            StartSession();
        }

        /// <summary>
        /// تذكيرات خطط الذاكرة المستحقة عند بدء التشغيل — كل خطة نشطة
        /// تاريخ تذكيرها النهارده أو قبله.
        ///
        /// **واحد ورا التاني مش مكوّمين** (قرار مؤكد): نوافذ فوق بعض
        /// بتخلي المستخدم يقفلهم كلهم من غير ما يقرا ولا واحدة.
        ///
        /// القايمة بتتقرا مرة واحدة في الأول: لو المستخدم دوس "ابدأ الآن"
        /// على أول خطة، إحنا بنسيب الباقي لأن الشاشة اتفتحت خلاص على شغل
        /// تاني — التذكيرات المتبقية هتظهر تاني أول تشغيل جاي زي أي
        /// تذكير متأخر (وإشعار بسيط بيقول عددها، عشان المستخدم يعرف إنها
        /// لسه موجودة مش ضاعت — شارة العدد على أيقونة "الذاكرة" كمان
        /// بتعكس نفس الرقم لحد ما التشغيلة الجاية).
        /// </summary>
        private static async Task ShowDueMemoryRemindersAsync(Window owner)
        {
            List<ProductionMemoryDto> due;

            using (var scope = AppHost.Services.CreateScope())
                due = (await scope.ServiceProvider.GetRequiredService<ProductionMemoryService>()
                    .GetDueAsync(DateTime.Today)).ToList();

            for (var i = 0; i < due.Count; i++)
            {
                var memory = due[i];
                var choice = await ShowMemoryReminderAsync(owner, memory, beforeStart: () =>
                {
                    // باقي التذكيرات هتستخبى لحد التشغيلة الجاية (كومنت الميثود
                    // فوق) — نطمّن المستخدم إنها لسه موجودة، مش ضاعت
                    var remaining = due.Count - i - 1;
                    if (remaining > 0)
                        Notify.Info($"في {remaining} خطة كمان مستنياك في الذاكرة", "تذكيرات تانية");
                });

                if (choice == Views.MemoryReminderChoice.Start)
                    return; // الشاشة اتفتحت — باقي التذكيرات لبكرة
            }
        }

        /// <summary>
        /// تذكير خطة ذاكرة واحدة بالكامل: الديالوج + "أجّل" (PostponeAsync) أو
        /// "ابدأ الآن" (StartMemorySessionAsync). **مكان واحد** بيستخدمه ديالوج
        /// بدء التشغيل وكارت الذاكرة في الرئيسية، عشان الزرارين يتصرفوا
        /// بنفس الطريقة بالظبط من المكانين.
        /// </summary>
        /// <param name="beforeStart">بيتنفذ قبل فتح الجلسة لو المستخدم اختار "ابدأ الآن"</param>
        internal static async Task<Views.MemoryReminderChoice> ShowMemoryReminderAsync(
            Window owner, ProductionMemoryDto memory, Action? beforeStart = null)
        {
            var dialog = Views.MemoryReminderDialog.Show(owner, memory);

            if (dialog.Choice == Views.MemoryReminderChoice.Postpone)
            {
                using var scope = AppHost.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ProductionMemoryService>()
                    .PostponeAsync(memory.Id, dialog.NewRemindOn);
            }
            else if (dialog.Choice == Views.MemoryReminderChoice.Start)
            {
                beforeStart?.Invoke();
                await StartMemorySessionAsync(memory);
            }

            return dialog.Choice;
        }

        /// <summary>
        /// بيعرض جولة "إيه الجديد" (<see cref="Tour.AppTourContent"/>) مرة
        /// واحدة بس لكل نسخة محتوى — مش لكل تشغيلة. بيتسجّل "شافها" حتى لو
        /// رفض، زي أي تذكير تاني في البرنامج (مش المفروض يتكرر السؤال).
        /// </summary>
        private static async Task OfferAppTourIfNewAsync(MainWindow owner)
        {
            var settings = AppSettingsStore.Load();
            if (settings.LastSeenTourVersion == Tour.AppTourContent.Version) return;

            if (Notify.Ask(
                "في حاجات جديدة في البرنامج — عايز جولة سريعة توريك إيها؟", "إيه الجديد؟"))
            {
                await owner.RunTourAsync(Tour.AppTourContent.Steps);
            }

            settings.LastSeenTourVersion = Tour.AppTourContent.Version;
            AppSettingsStore.Save(settings);
        }

        /// <summary>
        /// بيعرض "تعلم مميزات التحديث" مرة واحدة بس لكل رقم إصدار حقيقي
        /// (AppVersion.Current — مش Tour.AppTourContent.Version، اللي عدّاد
        /// محتوى منفصل تمامًا وله علَمه الخاص فوق). علَم منفصل ومقارنة
        /// منفصلة عن جولة "إيه الجديد" عن قصد — العرضين مستقلّين، مش
        /// المفروض واحد يسكت التاني.
        ///
        /// لو مفيش محتوى "تعلم مميزات" لإصدار البرنامج الحالي أصلًا (لسه
        /// مضافش)، مفيش عرض خالص — مش رسالة فاضية.
        /// </summary>
        private static void OfferLearnFeaturesIfNew(MainWindow owner)
        {
            var settings = AppSettingsStore.Load();
            if (!Tour.LearnFeaturesContent.ShouldOffer(settings.LastSeenLearnVersion, AppVersion.Current)) return;

            if (Notify.Ask(
                "فيه ميزات جديدة اتضافت في النسخة دي — عايز تتعلمها بالتطبيق العملي؟",
                "تعلم مميزات التحديث"))
            {
                owner.NavHelpItem.IsChecked = true; // أحدث إصدار مفتوح افتراضيًا في المحتوى نفسه، فمفيش داعي لعلَم "افتحله" إضافي
            }

            settings.LastSeenLearnVersion = AppVersion.Current;
            AppSettingsStore.Save(settings);
        }

        /// <summary>
        /// بيفتح شاشة الإنتاج اليومي على خطة.
        ///
        /// **الخطة بتتعلّم "منجزة" لما يتحفظ إنتاج حقيقي بس، مش هنا.**
        /// كان القرار القديم إنها تتعلّم منجزة بمجرد فتح الشاشة — اتغيّر
        /// لأن ده بيخلي خطط تظهر "اتبدأت" في شاشة الذاكرة والمستخدم أصلاً
        /// ماسجّلش حاجة (اتلاحظ فعليًا: خطة اتفتح لها الشاشة وماتسجلش
        /// فيها إنتاج، وفضلت ظاهرة منجزة). دلوقتي بنمرر معرّف الخطة
        /// لـ`FlowSessionViewModel` (شوف `ArmMemoryOrderAsync`)، وهي اللي
        /// بتنادي `MarkStartedAsync` بعد أول حفظ فعلي للرحلة.
        ///
        /// `GetStageOrderForSessionAsync` بيفضل يتنادى هنا الأول عشان
        /// يرمي لو الخطة بقت مش صالحة قبل ما نفتح شاشة على جلسة مكسورة.
        /// </summary>
        internal static async Task StartMemorySessionAsync(ProductionMemoryDto memory)
        {
            IReadOnlyList<int> stageOrder;

            try
            {
                using var scope = AppHost.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ProductionMemoryService>();

                stageOrder = await service.GetStageOrderForSessionAsync(memory.Id);
            }
            catch (InvalidOperationException ex)
            {
                Notify.Warn(ex.Message);
                return;
            }

            if (Current?.MainWindow is not MainWindow main) return;

            await main.OpenDailyEntryForMemoryAsync(memory.Id, memory.ProductId, stageOrder);
        }

        private static async Task EnsureLateSignOffsAcknowledgedAsync()
        {
            using var scope = AppHost.Services.CreateScope();
            var signOff = scope.ServiceProvider.GetRequiredService<DailyOperationsSignOffService>();

            var dates = await signOff.GetUnsignedPastDatesAsync(DateTime.Today);
            if (dates.Count == 0) return;

            var gate = scope.ServiceProvider.GetRequiredService<OperationsPasswordService>();
            var passwordRequired = await gate.IsConfiguredAsync();

            var dialog = new Views.LateSignOffCatchUpDialog(dates, passwordRequired, async password =>
            {
                using var ackScope = AppHost.Services.CreateScope();
                await ackScope.ServiceProvider
                    .GetRequiredService<DailyOperationsSignOffService>()
                    .AcknowledgeLateAsync(dates, password);
            });

            dialog.ShowDialog();
        }

        /// <summary>
        /// بيدمج ملف الوضع الليلي فوق الألوان الفاتحة، أو بيشيله.
        ///
        /// الدمج بيدوس على المفاتيح اللي في App.xaml بنفس الأسماء — يعني
        /// أي شاشة بتقرا BrandBrush بتاخد النسخة الداكنة من غير ما تعرف
        /// إن فيه وضع تاني أصلاً.
        /// </summary>
        /// <summary>
        /// بيبدّل لوحة الألوان **في اللحظة** من غير إعادة تشغيل.
        ///
        /// بيشتغل لأن كل الأنماط الجديدة بتشاور على الألوان بـ
        /// DynamicResource: الربط بيفضل حيّ، فتبديل الملف بيتنقل
        /// للشاشات المفتوحة فورًا. كان محتاج إعادة تشغيل أيام ما كانت
        /// StaticResource (بتتحل مرة واحدة وقت تحميل الشاشة).
        ///
        /// التبديل في **مكان اللوحة القديمة** مش إضافة فوقها: الإضافة
        /// كانت بتخلي الملفين محمّلين والذاكرة تكبر مع كل تبديل.
        /// </summary>
        public static void ApplyTheme(bool darkMode)
        {
            var app = Current;
            if (app is null) return;

            ApplyPalette(app, darkMode);
            ApplyMaterialDesignBaseTheme(darkMode);

            // شريط عنوان النافذة برّه WPF، فمفيش Brush بيوصله — لازم
            // يتلوّن بنداء صريح مع كل تبديل ثيم، وإلا بيفضل أبيض فوق
            // برنامج كله أسود
            foreach (Window window in app.Windows)
                WindowChromeColors.Apply(window);
        }

        /// <summary>لوحة ألوان الهوية — الملف بيتبدّل في مكانه.</summary>
        private static void ApplyPalette(Application app, bool darkMode)
        {
            var dictionaries = app.Resources.MergedDictionaries;

            var index = -1;
            for (var i = 0; i < dictionaries.Count; i++)
                if (dictionaries[i].Source?.OriginalString.Contains("Palette.") == true)
                {
                    index = i;
                    break;
                }

            if (index < 0) return; // اللوحة مش موجودة — مفيش حاجة تتبدّل

            var wanted = darkMode ? "Themes/Palette.Dark.xaml" : "Themes/Palette.Light.xaml";
            if (dictionaries[index].Source?.OriginalString == wanted) return;

            dictionaries[index] = new ResourceDictionary { Source = new Uri(wanted, UriKind.Relative) };
        }

        /// <summary>
        /// بيخلي ثيم MaterialDesign يمشي مع ثيم البرنامج — الخلفية/السطح
        /// (BaseTheme) **ولون التمييز** (Primary/Secondary) مع بعض.
        ///
        /// من غير الخطوة الأولى، البرنامج بيفضل على BaseTheme="Light"
        /// المكتوب في App.xaml مهما اتغيّرت لوحتنا — و**دي كانت أكبر
        /// مشكلة في الوضع الليلي**: 22 ComboBox و39 DataGrid و10
        /// DatePicker بيرسموا نفسهم من ثيم المكتبة مش من لوحتنا، فكانوا
        /// بيطلعوا بنص غامق على سطح غامق وقوايم منسدلة بيضا.
        ///
        /// **الخطوة التانية (PrimaryLight/Mid/Dark) اتضافت بعد ما اتلاقى
        /// إن التقويم بتاع DatePicker وعناصر تانية لسه بترسم بالنيلي
        /// الافتراضي (#3F51B5) حتى بعد كل ده.** السبب: نسخة المكتبة
        /// المستخدمة (5.1.0) بقت بترسم عناصرها من مفاتيح موارد جديدة
        /// (MaterialDesign.Brush.Primary وأخواتها) بدل الأسماء القديمة
        /// (PrimaryHueMidBrush...) اللي App.xaml كانت بتغلبها — فالتجاوز
        /// القديم بقى **كود ميت من يوم ما المكتبة اتحدّثت**، ومحدش لاحظ
        /// لأن أغلب عناصر الواجهة معمولة بستايلات خاصة بينا مش بستايلات
        /// المكتبة الافتراضية. `PaletteHelper` مالوش طريقة تاخد Hex حر
        /// عن طريق BundledTheme.PrimaryColor (بياخد أسماء ألوان Material
        /// بس)، فالطريقة الصحيحة دلوقتي هي كائن Theme نفسه: PrimaryLight/
        /// Mid/Dark وSecondaryLight/Mid/Dark كل واحد ColorPair (لون +
        /// لون النص فوقه)، بتتقرا من نفس ألوان اللوحة الحالية (بعد
        /// ApplyPalette فوق) فبتتقلب مع الثيم زي أي حاجة تانية.
        ///
        /// الفشل هنا مش سبب لإيقاف تبديل الثيم: أسوأ حاجة إن عناصر
        /// المكتبة تفضل بالمظهر القديم، والباقي بيتبدّل عادي.
        /// </summary>
        private static void ApplyMaterialDesignBaseTheme(bool darkMode)
        {
            try
            {
                var helper = new PaletteHelper();
                var theme = helper.GetTheme();

                theme.SetBaseTheme(darkMode ? BaseTheme.Dark : BaseTheme.Light);

                // لازم تتقرا بعد ApplyPalette فوق عشان تجيب لون الثيم
                // الحالي — الدالة دي بتتنادى من ApplyTheme اللي بيبدّل
                // اللوحة قبلها
                Color Palette(string key) => (Color)Application.Current.Resources[key];
                var inkOnAccent = Palette("InkOnAccentColor");

                theme.PrimaryLight = new ColorPair(Palette("GoldLineColor"), inkOnAccent);
                theme.PrimaryMid = new ColorPair(Palette("GoldColor"), inkOnAccent);
                theme.PrimaryDark = new ColorPair(Palette("GoldDeepColor"), inkOnAccent);
                theme.SecondaryLight = new ColorPair(Palette("GoldLineColor"), inkOnAccent);
                theme.SecondaryMid = new ColorPair(Palette("GoldColor"), inkOnAccent);
                theme.SecondaryDark = new ColorPair(Palette("GoldDeepColor"), Palette("SurfaceColor"));

                helper.SetTheme(theme);
            }
            catch
            {
                // تغيّر في واجهة المكتبة ميوقفش تبديل الثيم
            }
        }

        /// <summary>
        /// بيكتب تفاصيل العطل الكاملة في ملف جنب قاعدة البيانات.
        ///
        /// رسالة الاستثناء لوحدها مبتكفيش: العطل اللي بيحصل وقت بناء
        /// الواجهة رسالته عامة، والسبب الحقيقي بيبقى في
        /// InnerException — وأحيانًا في التالت. الملف بيكتبهم كلهم مع
        /// مكان العطل بالظبط، فبدل "البرنامج مش بيفتح" يبقى فيه سطر
        /// بيقول العطل فين.
        /// </summary>
        internal static string WriteCrashLog(Exception ex)
        {
            var path = Path.Combine(AppPaths.DataFolder, "crash.txt");

            try
            {
                var text = new System.Text.StringBuilder();
                text.AppendLine($"وقت العطل: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                text.AppendLine();

                for (var current = ex; current is not null; current = current.InnerException)
                {
                    text.AppendLine($"[{current.GetType().Name}] {current.Message}");
                    text.AppendLine(current.StackTrace);
                    text.AppendLine(new string('-', 60));
                }

                File.WriteAllText(path, text.ToString());
            }
            catch
            {
                // فشل كتابة السجل عمره ما يحجب رسالة العطل الأصلية
            }

            return path;
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            await AppHost.StopAsync();
            AppHost.Dispose();
            base.OnExit(e);
        }
    }
}
