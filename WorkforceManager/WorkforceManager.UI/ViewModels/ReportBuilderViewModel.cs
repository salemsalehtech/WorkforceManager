using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Interfaces;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// شاشة التقارير: المستخدم بيختار التقرير عن إيه، ولمدة إيه،
    /// ومتفصّل إزاي — ويشوفه على الشاشة قبل ما يصدّره.
    ///
    /// الشاشة دي بدّلت أربع تبويبات كان كل واحد فيهم تقرير مكتوب
    /// بإيده. الأربعة بقوا قوالب جاهزة جوّاها، فمفيش حاجة اتشالت من
    /// تحت إيد المستخدم — بالعكس بقى يقدر يعدّلهم ويعمل زيّهم.
    ///
    /// **المعاينة قبل التصدير مقصودة**: من غيرها المستخدم بيصدّر ملف،
    /// يفتحه في Excel، يلاقيه مش اللي هو عايزه، ويعيد. المعاينة بتخلي
    /// دورة التجربة تانية واحدة بدل نص دقيقة.
    /// </summary>
    public partial class ReportBuilderViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public ReportBuilderViewModel(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;

            Subjects = Enum.GetValues<ReportSubject>()
                .Select(s => new SubjectOption(s, ReportSpec.SubjectName(s)))
                .ToList();

            Periods = Enum.GetValues<ReportPeriodKind>()
                .Select(p => new PeriodOption(p, ReportPeriod.Name(p)))
                .ToList();

            _selectedSubject = Subjects[0];
            _selectedPeriod = Periods.First(p => p.Kind == ReportPeriodKind.ThisWeek);

            RefreshGroupings();
            ReloadTemplates();
        }

        // ======================= الاختيارات =======================

        public IReadOnlyList<SubjectOption> Subjects { get; }
        public IReadOnlyList<PeriodOption> Periods { get; }

        /// <summary>التفصيلات المتاحة للموضوع المختار — بتتغيّر معاه</summary>
        public ObservableCollection<GroupingOption> Groupings { get; } = new();

        [ObservableProperty]
        private SubjectOption _selectedSubject;

        partial void OnSelectedSubjectChanged(SubjectOption value)
        {
            RefreshGroupings();
            OnPropertyChanged(nameof(UsesPeriod));
            RequestPreview();
        }

        [ObservableProperty]
        private GroupingOption? _selectedGrouping;

        partial void OnSelectedGroupingChanged(GroupingOption? value) => RequestPreview();

        [ObservableProperty]
        private PeriodOption _selectedPeriod;

        partial void OnSelectedPeriodChanged(PeriodOption value)
        {
            // المدة الجاهزة بتملّي الداتتين عشان لو المستخدم قلب على
            // "مدة مخصوصة" يلاقيها مظبوطة بدل ما يبدأ من الصفر.
            // العلم بيمنع كل تغيير منهم إنه يطلب معاينة لوحده — معاينة
            // واحدة في الآخر بدل تلاتة
            if (value.Kind != ReportPeriodKind.Custom)
            {
                _suppressPreview = true;
                try
                {
                    var (from, to) = ReportPeriod.Resolve(value.Kind);
                    CustomFrom = from;
                    CustomTo = to;
                }
                finally
                {
                    _suppressPreview = false;
                }
            }

            OnPropertyChanged(nameof(IsCustomPeriod));
            RequestPreview();
        }

        /// <summary>بيمنع المعاينة وإحنا بنعدّل أكتر من اختيار مع بعض</summary>
        private bool _suppressPreview;

        /// <summary>
        /// الشاشة لسه بتتجهّز — مفيش أي معاينة قبل ما InitializeAsync تخلص.
        ///
        /// من غير البوابة دي كان بيحصل الآتي أول ما المستخدم يدخل الشاشة:
        /// الـ Constructor بينادي RefreshGroupings، ودي بتحطّ
        /// SelectedGrouping، والـ Setter بيطلب معاينة **قبل ما الفلاتر
        /// تتحمّل أصلاً**. وبعدها InitializeAsync بتطلب معاينة تانية.
        /// الاتنين fire-and-forget على نفس PreviewRows/PreviewHeaders، وكل
        /// واحدة بتعمل Clear وبعدين تملّي — فالجدول كان بيفضى ويتملى أكتر
        /// من مرة متراكبة، وده اللي المستخدم شافه "بيظهر ويختفي".
        /// </summary>
        private bool _ready;

        private void RequestPreview()
        {
            if (_suppressPreview || !_ready) return;
            SafeAsync.Run(PreviewAsync);
        }

        public bool IsCustomPeriod => SelectedPeriod.Kind == ReportPeriodKind.Custom;

        /// <summary>المهارات حالة مش حركة — اختيار المدة بيختفي معاها</summary>
        public bool UsesPeriod => ReportSpec.UsesPeriod(SelectedSubject.Subject);

        [ObservableProperty]
        private DateTime _customFrom = DateTime.Today.AddDays(-6);

        partial void OnCustomFromChanged(DateTime value) => RequestPreview();

        [ObservableProperty]
        private DateTime _customTo = DateTime.Today;

        partial void OnCustomToChanged(DateTime value) => RequestPreview();

        // ------- الفلاتر -------

        public ObservableCollection<WorkerFilterItem> Workers { get; } = new();
        public ObservableCollection<ProductFilterItem> Products { get; } = new();

        [ObservableProperty]
        private WorkerFilterItem? _selectedWorker;

        partial void OnSelectedWorkerChanged(WorkerFilterItem? value)
        {
            OnPropertyChanged(nameof(CanPrintPayslip));
            RequestPreview();
        }

        [ObservableProperty]
        private ProductFilterItem? _selectedProduct;

        partial void OnSelectedProductChanged(ProductFilterItem? value) => RequestPreview();

        [ObservableProperty]
        private bool _isFilterMenuOpen;

        public bool HasFilters => SelectedWorker?.Id is not null || SelectedProduct?.Id is not null;

        public int ActiveFilterCount =>
            (SelectedWorker?.Id is not null ? 1 : 0) + (SelectedProduct?.Id is not null ? 1 : 0);

        [RelayCommand]
        private void ClearFilters()
        {
            // الاتنين بيتغيّروا مع بعض، فمعاينة واحدة في الآخر بدل
            // اتنين ورا بعض على نفس الجدول
            _suppressPreview = true;
            try
            {
                SelectedWorker = Workers.FirstOrDefault();
                SelectedProduct = Products.FirstOrDefault();
            }
            finally
            {
                _suppressPreview = false;
            }

            RequestPreview();
        }

        // ======================= المعاينة =======================

        /// <summary>أعمدة الجدول المعروض — بتتبني من ReportTable مش ثابتة</summary>
        public ObservableCollection<string> PreviewHeaders { get; } = new();
        public ObservableCollection<PreviewRow> PreviewRows { get; } = new();

        [ObservableProperty]
        private string _reportTitle = "";

        [ObservableProperty]
        private string _reportPeriodText = "";

        [ObservableProperty]
        private string _resultsText = "";

        [ObservableProperty]
        private bool _isEmpty = true;

        partial void OnIsEmptyChanged(bool value) => OnPropertyChanged(nameof(HasReport));

        /// <summary>فيه تقرير فيه بيانات — بيتحكم في زرار التصدير</summary>
        public bool HasReport => !IsEmpty;

        [ObservableProperty]
        private bool _isBusy;

        private ReportTable? _current;

        /// <summary>بيبني الوصف من اختيارات الشاشة</summary>
        private ReportSpec BuildSpec()
        {
            var (from, to) = SelectedPeriod.Kind == ReportPeriodKind.Custom
                ? (CustomFrom, CustomTo)
                : ReportPeriod.Resolve(SelectedPeriod.Kind);

            return new ReportSpec
            {
                Subject = SelectedSubject.Subject,
                GroupBy = SelectedGrouping?.Grouping ?? ReportGrouping.Worker,
                From = from,
                To = to,
                WorkerIds = SelectedWorker?.Id is { } w ? new[] { w } : null,
                ProductIds = SelectedProduct?.Id is { } p ? new[] { p } : null
            };
        }

        /// <summary>
        /// رقم آخر معاينة اتطلبت. المستخدم بيقلب في الفلاتر أسرع من
        /// الاستعلام، والاستعلامات مش بتخلص بنفس ترتيب طلبها — تقرير
        /// مدة طويلة ممكن يخلص بعد تقرير مدة قصيرة اتطلب بعده. من غير
        /// الرقم ده النتيجة القديمة بتتكتب فوق الجديدة والمستخدم يشوف
        /// أرقام مش بتاعة اللي هو مختاره.
        /// </summary>
        private int _previewGeneration;

        [RelayCommand]
        public async Task PreviewAsync()
        {
            if (SelectedGrouping is null) return;

            var generation = ++_previewGeneration;

            IsBusy = true;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var table = await scope.ServiceProvider
                    .GetRequiredService<ReportBuilderService>()
                    .BuildAsync(BuildSpec());

                // اتطلبت معاينة أحدث وإحنا بنستنى — النتيجة دي بقت قديمة
                if (generation != _previewGeneration) return;

                _current = table;
                ShowTable(table);
            }
            finally
            {
                // آخر معاينة بس هي اللي بتطفي المؤشر، وإلا واحدة قديمة
                // بتخلص بدري وتقول "خلصنا" والجديدة لسه شغالة
                if (generation == _previewGeneration) IsBusy = false;
            }

            OnPropertyChanged(nameof(HasFilters));
            OnPropertyChanged(nameof(ActiveFilterCount));
        }

        /// <summary>
        /// بيحوّل الجدول العام لصفوف عرض. عمود واحد لكل عمود في
        /// الجدول — الشبكة نفسها مبتعرفش أي حاجة عن الموضوع، فأي
        /// موضوع جديد بيتعرض من غير ما الشاشة تتغيّر.
        /// </summary>
        private void ShowTable(ReportTable table)
        {
            ReportTitle = table.Title;
            ReportPeriodText = table.PeriodText;

            PreviewHeaders.Clear();
            PreviewHeaders.Add(table.LabelHeader);
            foreach (var column in table.Columns) PreviewHeaders.Add(column.Header);

            PreviewRows.Clear();
            foreach (var row in table.Rows)
                PreviewRows.Add(PreviewRow.From(row, table.Columns, isTotals: false));

            if (table.Totals is { } totals)
                PreviewRows.Add(PreviewRow.From(totals, table.Columns, isTotals: true));

            IsEmpty = table.IsEmpty;
            ResultsText = table.IsEmpty
                ? "مفيش بيانات في المدة دي"
                : $"{table.Rows.Count} سطر";
        }

        // ======================= التصدير =======================

        [RelayCommand]
        private async Task ExportAsync()
        {
            if (_current is null || _current.IsEmpty)
            {
                Notify.Info("مفيش بيانات في التقرير ده للتصدير");
                return;
            }

            var table = _current;

            await ExcelExport.RunAsync(
                "حفظ التقرير",
                $"{table.Title} {DateTime.Today:yyyy-MM-dd}",
                path =>
                {
                    using var scope = _scopeFactory.CreateScope();
                    scope.ServiceProvider.GetRequiredService<ReportTableExcelService>()
                        .Export(table, path);
                    return Task.CompletedTask;
                });
        }

        // ======================= قسيمة الأجر =======================

        /// <summary>
        /// القسيمة ورقة العامل نفسه — مش جدول، فمالهاش مكان في المُنشئ
        /// العام. بتظهر لما المستخدم يختار عامل واحد في الفلاتر: ساعتها
        /// بس السؤال "قسيمة مين؟" يبقى ليه إجابة.
        /// </summary>
        public bool CanPrintPayslip => SelectedWorker?.Id is not null;

        [RelayCommand]
        private async Task PrintPayslipAsync()
        {
            if (SelectedWorker?.Id is not { } workerId)
            {
                Notify.Info("اختار عامل من الفلاتر الأول عشان تطبع قسيمته");
                return;
            }

            var (from, to) = SelectedPeriod.Kind == ReportPeriodKind.Custom
                ? (CustomFrom, CustomTo)
                : ReportPeriod.Resolve(SelectedPeriod.Kind);

            WorkerProductionReportDto report;
            using (var scope = _scopeFactory.CreateScope())
                report = await scope.ServiceProvider
                    .GetRequiredService<ProductionReportService>()
                    .GetWorkerReportAsync(workerId, from, to);

            // معاينة في نافذة، والطباعة من جواها لأي طابعة أو PDF
            new Views.PayslipWindow(PayslipData.From(report))
            {
                Owner = System.Windows.Application.Current.MainWindow
            }.ShowDialog();
        }

        // ======================= القوالب =======================

        public ObservableCollection<ReportTemplate> Templates { get; } = new();

        [ObservableProperty]
        private ReportTemplate? _selectedTemplate;

        partial void OnSelectedTemplateChanged(ReportTemplate? value)
        {
            if (value is null) return;

            _suppressPreview = true;
            try
            {
                SelectedSubject = Subjects.First(s => s.Subject == value.Subject);
                SelectedGrouping = Groupings.FirstOrDefault(g => g.Grouping == value.GroupBy)
                                   ?? Groupings.FirstOrDefault();
                SelectedPeriod = Periods.First(p => p.Kind == value.Period);
            }
            finally
            {
                _suppressPreview = false;
            }

            RequestPreview();
        }

        [ObservableProperty]
        private string _newTemplateName = "";

        public bool CanSaveTemplate => NewTemplateName.Trim().Length > 0;

        partial void OnNewTemplateNameChanged(string value) =>
            OnPropertyChanged(nameof(CanSaveTemplate));

        [RelayCommand]
        private void SaveTemplate()
        {
            var name = NewTemplateName.Trim();
            if (name.Length == 0) return;

            ReportTemplateStore.Save(new ReportTemplate
            {
                Name = name,
                Subject = SelectedSubject.Subject,
                GroupBy = SelectedGrouping?.Grouping ?? ReportGrouping.Worker,
                Period = SelectedPeriod.Kind
            });

            NewTemplateName = "";
            ReloadTemplates();
            Notify.Info($"القالب \"{name}\" اتحفظ. هتلاقيه في قايمة القوالب.", "تم الحفظ");
        }

        [RelayCommand]
        private void DeleteTemplate(ReportTemplate? template)
        {
            if (template is null || template.IsBuiltIn) return;

            if (!Notify.Ask($"حذف قالب \"{template.Name}\"؟", "تأكيد")) return;

            ReportTemplateStore.Delete(template.Name);
            ReloadTemplates();
        }

        private void ReloadTemplates()
        {
            Templates.Clear();
            foreach (var t in ReportTemplateStore.Load()) Templates.Add(t);
        }

        // ======================= التحميل الأول =======================

        public async Task InitializeAsync()
        {
            // الـ finally مش زيادة: _ready بيقفل المعاينة لحد ما التجهيز
            // يخلص، فلو تحميل الفلاتر وقع من غيره الشاشة تفضل مقفولة
            // للأبد — المستخدم يقلب في الاختيارات ومفيش حاجة بتحصل.
            // كده أسوأ حالة إن الفلاتر تبقى ناقصة والتقرير يشتغل.
            try
            {
                using var scope = _scopeFactory.CreateScope();

                var workerRepo = scope.ServiceProvider.GetRequiredService<IWorkerRepository>();
                var productRepo = scope.ServiceProvider.GetRequiredService<IProductRepository>();

                Workers.Clear();
                Workers.Add(new WorkerFilterItem(null, "كل العمال"));
                foreach (var w in (await workerRepo.GetAllWithSkillsAsync()).OrderBy(w => w.FullName))
                    Workers.Add(new WorkerFilterItem(w.Id, w.FullName));

                Products.Clear();
                Products.Add(new ProductFilterItem(null, "كل المنتجات"));
                foreach (var p in (await productRepo.GetAllWithStagesAsync()).OrderBy(p => p.Name))
                    Products.Add(new ProductFilterItem(p.Id, p.Name));

                // جوّه الـ try عن قصد: لسه _ready = false هنا، فالسطرين
                // دول مش هيطلبوا معاينة لوحدهم
                SelectedWorker = Workers.FirstOrDefault();
                SelectedProduct = Products.FirstOrDefault();
            }
            finally
            {
                // من هنا وطالع أي تغيير في الاختيارات يطلب معاينة
                _ready = true;
            }

            await PreviewAsync();
        }

        /// <summary>
        /// التفصيلات بتتغيّر مع الموضوع: "الحضور بالمنتج" سؤال مالوش
        /// إجابة، فمبيتعرضش أصلاً بدل ما المستخدم يوصل لتقرير فاضي.
        /// </summary>
        private void RefreshGroupings()
        {
            var current = SelectedGrouping?.Grouping;

            Groupings.Clear();
            foreach (var g in ReportSpec.AllowedGroupings(SelectedSubject.Subject))
                Groupings.Add(new GroupingOption(g, ReportSpec.GroupingName(g)));

            SelectedGrouping = Groupings.FirstOrDefault(g => g.Grouping == current)
                               ?? Groupings.FirstOrDefault();
        }
    }
}
