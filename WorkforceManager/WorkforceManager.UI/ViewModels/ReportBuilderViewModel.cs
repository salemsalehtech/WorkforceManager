using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Data;

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
            LoadPayslipFieldChoices();
            ReloadPayslipFormats();
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
            RefreshThenByOptions();
            RefreshColumnChoices(); // كل موضوع وأعمدته
            OnPropertyChanged(nameof(UsesPeriod));
            OnPropertyChanged(nameof(IsWagesReport));
            OnPropertyChanged(nameof(CanPrintPayslip));
            RequestPreview();
        }

        /// <summary>
        /// القسايم بتاعت الأجور — مش بتاعت أي تقرير.
        ///
        /// زرار القسايم كان ظاهر على كل تقرير: تقرير حضور أو هالك أو
        /// مهارات، وجنبه زرار بيطبع أجور. الزرار كان **شغال** برضه لأنه
        /// بيسأل PayrollService مباشرة، فالمستخدم بيطلع قسايم أجر وهو
        /// شايف قدامه جدول حضور — نفس المدة، بس مش نفس التقرير. ده مش
        /// مجرد زحمة، ده اقتراح غلط للي بيبصّ على الشاشة.
        /// </summary>
        public bool IsWagesReport => SelectedSubject.Subject == ReportSubject.Wages;

        [ObservableProperty]
        private GroupingOption? _selectedGrouping;

        partial void OnSelectedGroupingChanged(GroupingOption? value)
        {
            // المهارات بتغيّر عمودين مع التفصيل، فالقايمة لازم تتحدّث
            RefreshColumnChoices();
            RequestPreview();
        }

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

        /// <summary>
        /// العمال والمنتجات باختيار **متعدد**. المحرك بيقبل مجموعة من
        /// زمان (<see cref="ReportSpec.WorkerIds"/>) بس الشاشة كانت
        /// بتبعت واحد بس — يعني "تقرير عن الخمس عمال دول" مكانش ينفع
        /// أصلاً مع إن المحرك عامله.
        /// </summary>
        public ObservableCollection<CheckableItem> Workers { get; } = new();
        public ObservableCollection<CheckableItem> Products { get; } = new();

        /// <summary>فلتر المرحلة — كان في المحرك ومش ظاهر في الشاشة</summary>
        public ObservableCollection<StageFilterItem> Stages { get; } = new();

        [ObservableProperty]
        private StageFilterItem? _selectedStage;

        partial void OnSelectedStageChanged(StageFilterItem? value)
        {
            OnPropertyChanged(nameof(ActiveFilterCount));
            OnPropertyChanged(nameof(HasFilters));
            RequestPreview();
        }

        /// <summary>بالقطعة / بالساعة — كان في المحرك ومش ظاهر كمان</summary>
        public IReadOnlyList<WorkerKindOption> WorkerKinds { get; } = new[]
        {
            new WorkerKindOption(WorkerKindFilter.All, "الكل"),
            new WorkerKindOption(WorkerKindFilter.ByProduction, "بالقطعة"),
            new WorkerKindOption(WorkerKindFilter.Hourly, "بالساعة")
        };

        [ObservableProperty]
        private WorkerKindOption? _selectedWorkerKind;

        partial void OnSelectedWorkerKindChanged(WorkerKindOption? value)
        {
            OnPropertyChanged(nameof(ActiveFilterCount));
            OnPropertyChanged(nameof(HasFilters));
            RequestPreview();
        }

        [ObservableProperty]
        private bool _isFilterMenuOpen;

        /// <summary>مفيش علامات = الفلتر مقفول (مش "طابق ولا حاجة")</summary>
        private static IReadOnlyCollection<int>? CheckedIds(IEnumerable<CheckableItem> items)
        {
            var ids = items.Where(i => i.IsChecked).Select(i => i.Id).ToList();
            return ids.Count == 0 ? null : ids;
        }

        public bool HasFilters => ActiveFilterCount > 0;

        public int ActiveFilterCount =>
            (Workers.Any(w => w.IsChecked) ? 1 : 0) +
            (Products.Any(p => p.IsChecked) ? 1 : 0) +
            (SelectedStage?.Id is not null ? 1 : 0) +
            (SelectedWorkerKind is { Kind: not WorkerKindFilter.All } ? 1 : 0);

        /// <summary>بيتنادى من أي عنصر اتعلّم أو اتشال منه العلامة</summary>
        private void OnFilterToggled()
        {
            OnPropertyChanged(nameof(HasFilters));
            OnPropertyChanged(nameof(ActiveFilterCount));
            OnPropertyChanged(nameof(CanPrintPayslip));
            RequestPreview();
        }

        private void OnCheckableChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CheckableItem.IsChecked)) OnFilterToggled();
        }

        [RelayCommand]
        private void ClearFilters()
        {
            // كل الفلاتر بتتغيّر مع بعض، فمعاينة واحدة في الآخر بدل
            // معاينة لكل واحد فيهم
            _suppressPreview = true;
            try
            {
                foreach (var w in Workers) w.IsChecked = false;
                foreach (var p in Products) p.IsChecked = false;
                SelectedStage = Stages.FirstOrDefault();
                SelectedWorkerKind = WorkerKinds[0];
            }
            finally
            {
                _suppressPreview = false;
            }

            OnFilterToggled();
        }

        // ------- مستويات التجميع الإضافية -------

        /// <summary>
        /// "ثم" و"ثم" — مستويين إضافيين بعد التفصيل الأساسي.
        ///
        /// اتنين كفاية عمليًا: المنتج ← العامل ← المرحلة هو أعمق سؤال
        /// بيتسأل، وأي مستوى زيادة بيخلي كل سطر مجموعة من سجل واحد —
        /// يعني بقى شيت التفاصيل الخام مش تقرير.
        /// </summary>
        public ObservableCollection<GroupingOption> ThenByOptions { get; } = new();

        [ObservableProperty]
        private GroupingOption? _thenBy1;

        partial void OnThenBy1Changed(GroupingOption? value)
        {
            RefreshColumnChoices();
            RequestPreview();
        }

        [ObservableProperty]
        private GroupingOption? _thenBy2;

        partial void OnThenBy2Changed(GroupingOption? value)
        {
            RefreshColumnChoices();
            RequestPreview();
        }

        /// <summary>المستويات الإضافية المختارة، من غير الفاضي ولا المكرر</summary>
        private List<ReportGrouping> SelectedThenBy()
        {
            var levels = new List<ReportGrouping>();

            foreach (var option in new[] { ThenBy1, ThenBy2 })
                if (option is { Grouping: var g } && g != (SelectedGrouping?.Grouping ?? ReportGrouping.Worker)
                    && !levels.Contains(g))
                    levels.Add(g);

            return levels;
        }

        /// <summary>
        /// التجميع المركّب للإنتاج بس دلوقتي: هو الموضوع الوحيد اللي
        /// سجلاته بتحمل التلات أبعاد مع بعض (عامل ومنتج ومرحلة في نفس
        /// السجل). الحضور مثلاً مالوش منتج، فـ"الحضور بالمنتج" سؤال
        /// مالوش إجابة زي ما هو موضّح في AllowedGroupings.
        /// </summary>
        public bool SupportsLevels => SelectedSubject.Subject == ReportSubject.Production;

        private void RefreshThenByOptions()
        {
            var previous1 = ThenBy1?.Grouping;
            var previous2 = ThenBy2?.Grouping;

            ThenByOptions.Clear();
            ThenByOptions.Add(new GroupingOption((ReportGrouping)0, "— مفيش —"));

            if (SupportsLevels)
                foreach (var g in new[]
                         {
                             ReportGrouping.Worker, ReportGrouping.Product,
                             ReportGrouping.Stage, ReportGrouping.Day
                         })
                    ThenByOptions.Add(new GroupingOption(g, ReportSpec.GroupingLabel(g)));

            _suppressPreview = true;
            try
            {
                ThenBy1 = ThenByOptions.FirstOrDefault(o => o.Grouping == previous1) ?? ThenByOptions[0];
                ThenBy2 = ThenByOptions.FirstOrDefault(o => o.Grouping == previous2) ?? ThenByOptions[0];
            }
            finally
            {
                _suppressPreview = false;
            }

            OnPropertyChanged(nameof(SupportsLevels));
        }

        // ------- شكل الجدول: الأعمدة -------

        /// <summary>
        /// أعمدة الموضوع المختار: المستخدم بيعلّم اللي عايزه، بيرتّبهم،
        /// وبيغيّر أسماءهم.
        /// </summary>
        public ObservableCollection<ColumnChoiceItem> ColumnChoices { get; } = new();

        [ObservableProperty]
        private bool _isColumnMenuOpen;

        public int HiddenColumnCount => ColumnChoices.Count(c => !c.IsVisible);
        public bool HasColumnChanges => ColumnChoices.Any(c => !c.IsVisible || c.IsRenamed);

        /// <summary>
        /// بيعيد بناء قايمة الأعمدة لما الموضوع أو التفصيل يتغيّر،
        /// **ومحافظ على اختيار المستخدم** للأعمدة اللي لسه موجودة
        /// بمفتاحها — فتغيير التفصيل مش بيضيّع شغله.
        /// </summary>
        private void RefreshColumnChoices()
        {
            var previous = ColumnChoices.ToDictionary(c => c.Key);

            foreach (var choice in ColumnChoices) choice.PropertyChanged -= OnColumnChoiceChanged;
            ColumnChoices.Clear();

            // أعمدة المستويات الإضافية بتيجي الأول زي ما المحرك
            // بيبنيها، عشان المحرّر يعرض نفس ترتيب الجدول
            var levelColumns = SelectedThenBy()
                .Select(level => new ReportColumn
                {
                    Key = "dim_" + level.ToString().ToLowerInvariant(),
                    Header = ReportSpec.GroupingLabel(level),
                    Kind = ReportValueKind.Text
                });

            var available = levelColumns.Concat(ReportBuilderService.ColumnsFor(
                SelectedSubject.Subject,
                SelectedGrouping?.Grouping ?? ReportGrouping.Worker)).ToList();

            foreach (var column in available)
            {
                var item = previous.TryGetValue(column.Key, out var old)
                    ? new ColumnChoiceItem(column.Key, column.Header, old.IsVisible,
                        old.IsRenamed ? old.Header : null, column.Sums, old.Sums)
                    : new ColumnChoiceItem(column.Key, column.Header, canSum: column.Sums);

                item.PropertyChanged += OnColumnChoiceChanged;
                ColumnChoices.Add(item);
            }

            RefreshSortOptions();

            OnPropertyChanged(nameof(HiddenColumnCount));
            OnPropertyChanged(nameof(HasColumnChanges));
        }

        private void OnColumnChoiceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(ColumnChoiceItem.IsVisible)
                or nameof(ColumnChoiceItem.Header) or nameof(ColumnChoiceItem.Sums)))
                return;

            OnPropertyChanged(nameof(HiddenColumnCount));
            OnPropertyChanged(nameof(HasColumnChanges));
            RefreshSortOptions();
            RequestPreview();
        }

        [RelayCommand]
        private void MoveColumnUp(ColumnChoiceItem? item) => MoveColumn(item, -1);

        [RelayCommand]
        private void MoveColumnDown(ColumnChoiceItem? item) => MoveColumn(item, +1);

        private void MoveColumn(ColumnChoiceItem? item, int direction)
        {
            if (item is null) return;

            var index = ColumnChoices.IndexOf(item);
            var target = index + direction;

            if (index < 0 || target < 0 || target >= ColumnChoices.Count) return;

            ColumnChoices.Move(index, target);
            RequestPreview();
        }

        [RelayCommand]
        private void ResetColumns()
        {
            _suppressPreview = true;
            try
            {
                foreach (var choice in ColumnChoices)
                {
                    choice.IsVisible = true;
                    choice.Header = choice.DefaultHeader;
                }

                RefreshColumnChoices();
            }
            finally
            {
                _suppressPreview = false;
            }

            OnPropertyChanged(nameof(HiddenColumnCount));
            OnPropertyChanged(nameof(HasColumnChanges));
            RequestPreview();
        }

        // ------- الترتيب وأعلى N -------

        public ObservableCollection<SortOption> SortOptions { get; } = new();

        [ObservableProperty]
        private SortOption? _selectedSort;

        partial void OnSelectedSortChanged(SortOption? value)
        {
            OnPropertyChanged(nameof(HasDisplayChanges));
            RequestPreview();
        }

        [ObservableProperty]
        private bool _sortDescending = true;

        partial void OnSortDescendingChanged(bool value) => RequestPreview();

        public IReadOnlyList<TopNOption> TopNOptions { get; } = new[]
        {
            new TopNOption(null, "كل الصفوف"),
            new TopNOption(5, "أعلى 5"),
            new TopNOption(10, "أعلى 10"),
            new TopNOption(20, "أعلى 20"),
            new TopNOption(50, "أعلى 50")
        };

        [ObservableProperty]
        private TopNOption? _selectedTopN;

        partial void OnSelectedTopNChanged(TopNOption? value)
        {
            OnPropertyChanged(nameof(HasDisplayChanges));
            RequestPreview();
        }

        /// <summary>قايمة "الترتيب والعرض" — الترتيب وأعلى N والإجمالي والمقارنة</summary>
        [ObservableProperty]
        private bool _isDisplayMenuOpen;

        /// <summary>
        /// فيه إعداد عرض مش على وضعه الافتراضي. الشارة على الزرار
        /// بتقول كده، لأن الإعداد ده جوّه قايمة مقفولة: من غير شارة
        /// المستخدم بيشوف 10 صفوف بس وميعرفش إن هو نفسه اللي طلب كده.
        /// </summary>
        public bool HasDisplayChanges =>
            SelectedSort?.Key is not null
            || SelectedTopN?.Count is not null
            || !ShowTotals
            || CompareWithPrevious;

        private void RefreshSortOptions()
        {
            var current = SelectedSort?.Key;

            SortOptions.Clear();
            SortOptions.Add(new SortOption(null, "الترتيب الطبيعي"));
            SortOptions.Add(new SortOption(ReportTable.LabelColumnKey, "بالاسم"));

            // بالأعمدة كلها حتى المخفية: "رتّب بالأجر بس متعرضهوش"
            // طلب معقول، والمحرك بيدعمه
            foreach (var column in ColumnChoices)
                SortOptions.Add(new SortOption(column.Key, column.Header));

            _suppressPreview = true;
            try
            {
                SelectedSort = SortOptions.FirstOrDefault(o => o.Key == current) ?? SortOptions[0];
            }
            finally
            {
                _suppressPreview = false;
            }
        }

        // ------- المقارنة وخيارات التصدير -------

        /// <summary>
        /// سطر الإجمالي تحت الجدول. مش كل تقرير ليه معنى: "إجمالي" تحت
        /// تقرير حضور بيجمع أيام حضور كل العمال في رقم واحد محدش بيسأل
        /// عنه.
        /// </summary>
        [ObservableProperty]
        private bool _showTotals = true;

        partial void OnShowTotalsChanged(bool value)
        {
            OnPropertyChanged(nameof(HasDisplayChanges));
            RequestPreview();
        }

        [ObservableProperty]
        private bool _compareWithPrevious;

        partial void OnCompareWithPreviousChanged(bool value)
        {
            OnPropertyChanged(nameof(HasDisplayChanges));
            RequestPreview();
        }

        [ObservableProperty]
        private bool _exportDetailSheet = true;

        [ObservableProperty]
        private bool _exportSheetPerGroup;

        [ObservableProperty]
        private bool _isExportMenuOpen;

        // ======================= المعاينة =======================

        /// <summary>أعمدة الجدول المعروض — بتتبني من ReportTable مش ثابتة</summary>
        public ObservableCollection<string> PreviewHeaders { get; } = new();

        private List<ReportValueKind> _columnKinds = new();

        /// <summary>
        /// العمود ده نصي؟ الشاشة بتسأل عشان تحاذيه من بداية السطر بدل
        /// النص، ورأسه معاه. الفهرس هنا بيعدّ الأعمدة بعد عمود الاسم.
        /// </summary>
        public bool IsTextColumn(int index) =>
            index >= 0 && index < _columnKinds.Count && _columnKinds[index] == ReportValueKind.Text;
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
                ThenBy = SelectedThenBy(),
                From = from,
                To = to,

                WorkerIds = CheckedIds(Workers),
                ProductIds = CheckedIds(Products),
                StageIds = SelectedStage?.Id is { } s ? new[] { s } : null,
                WorkerKind = SelectedWorkerKind?.Kind ?? WorkerKindFilter.All,

                // ترتيب القايمة **هو** ترتيب الأعمدة في التقرير
                ColumnLayout = ColumnChoices.Select(c => c.ToChoice()).ToList(),
                SortKey = SelectedSort?.Key,
                SortDescending = SortDescending,
                TopN = SelectedTopN?.Count,
                ShowTotals = ShowTotals,

                CompareWithPrevious = CompareWithPrevious
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

            // الشاشة محتاجة تعرف العمود نصي ولا رقمي عشان تحاذيه صح —
            // بتاخدها من الجدول مش بتخمّنها من المحتوى
            _columnKinds = table.Columns.Select(c => c.Kind).ToList();

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
            var spec = BuildSpec();

            var settings = AppSettingsStore.Load();
            var options = new ReportExportOptions
            {
                IncludeDetailSheet = ExportDetailSheet,
                SheetPerGroup = ExportSheetPerGroup,
                FactoryName = settings.FactoryName,
                LogoPath = settings.LogoPath
            };

            // التفاصيل بتتبني بس لما تكون مطلوبة — دي أتقل استعلام في
            // الشاشة (سجل لكل صف)، ومفيش داعي نحمّله على كل تصدير
            ReportDetail? detail = null;

            if (options.IncludeDetailSheet || options.SheetPerGroup)
            {
                using var scope = _scopeFactory.CreateScope();
                detail = await scope.ServiceProvider
                    .GetRequiredService<ReportBuilderService>()
                    .BuildDetailAsync(spec);
            }

            await ExcelExport.RunAsync(
                "حفظ التقرير",
                $"{table.Title} {DateTime.Today:yyyy-MM-dd}",
                path =>
                {
                    using var scope = _scopeFactory.CreateScope();
                    scope.ServiceProvider.GetRequiredService<ReportTableExcelService>()
                        .Export(table, path, detail, options);
                    return Task.CompletedTask;
                });
        }

        // ======================= بنود قسايم الأجر المطبوعة =======================

        /// <summary>
        /// البنود اللي المستخدم يقدر يظهرها/يخفيها في "قسايم الأجر"
        /// (شوف ExportPayslipStripsAsync). كل تعليم/إلغاء بيتحفظ فورًا
        /// في الإعدادات — نفس اختيار المستخدم بيفضل في المرة الجاية،
        /// زي قوالب التقارير بالظبط. اسم المصنع والعامل والفترة
        /// و"الصافي المستحق" ثابتين دايمًا في القسيمة نفسها، فمش هنا.
        /// </summary>
        public ObservableCollection<PayslipFieldChoice> PayslipFieldChoices { get; } = new();

        [ObservableProperty]
        private bool _isPayslipFieldMenuOpen;

        private void LoadPayslipFieldChoices()
        {
            var saved = AppSettingsStore.Load().PayslipStripFields;

            // null = كل البنود — نفس القسيمة الأصلية قبل ما البنود تبقى اختيارية
            var selected = saved is null
                ? PayslipStripExcelService.AllFields
                : saved
                    .Select(name => Enum.TryParse<PayslipStripField>(name, out var f) ? f : (PayslipStripField?)null)
                    .Where(f => f is not null)
                    .Select(f => f!.Value)
                    .ToHashSet();

            foreach (var choice in PayslipFieldChoices) choice.Changed -= SavePayslipFieldChoices;
            PayslipFieldChoices.Clear();

            void Add(PayslipStripField field, string label)
            {
                var item = new PayslipFieldChoice(field, label) { IsChecked = selected.Contains(field) };
                item.Changed += SavePayslipFieldChoices;
                PayslipFieldChoices.Add(item);
            }

            Add(PayslipStripField.DailyWageRate, "سعر اليومية");
            Add(PayslipStripField.ProducedWorkdays, "يوميات منتجة");
            Add(PayslipStripField.DaysWorked, "أيام فيها شغل");
            Add(PayslipStripField.StageBreakdown, "اشتغل على (تفاصيل المراحل والقطع)");
            Add(PayslipStripField.TotalPieces, "إجمالي القطع");
            Add(PayslipStripField.AbsenceDeduction, "خصم غياب");
            Add(PayslipStripField.PenaltyDeduction, "خصم جزاءات");
            Add(PayslipStripField.NetWorkdays, "صافي اليوميات");
            Add(PayslipStripField.WorkdaysWageEgp, "أجر اليوميات");
            Add(PayslipStripField.Bonus, "حوافز");
            Add(PayslipStripField.Advance, "سلف");
        }

        private void SavePayslipFieldChoices()
        {
            var settings = AppSettingsStore.Load();
            settings.PayslipStripFields = PayslipFieldChoices
                .Where(c => c.IsChecked)
                .Select(c => c.Field.ToString())
                .ToList();
            AppSettingsStore.Save(settings);
        }

        private HashSet<PayslipStripField> SelectedPayslipFields() =>
            PayslipFieldChoices.Where(c => c.IsChecked).Select(c => c.Field).ToHashSet();

        // ------- فورمات محفوظة (بنود مسمّاة، زي قوالب التقارير) -------

        /// <summary>
        /// فورمات جاهزة أو محفوظة بالاسم — اختياره بيملّي <see cref="PayslipFieldChoices"/>
        /// دفعة واحدة بدل ما المستخدم يعلّم/يشيل يدويًا كل مرة. الاختيار
        /// اليدوي بعد كده لسه شغال عادي (مش قفل على الفورمات المختار).
        /// </summary>
        public ObservableCollection<PayslipFormat> PayslipFormats { get; } = new();

        [ObservableProperty]
        private PayslipFormat? _selectedPayslipFormat;

        partial void OnSelectedPayslipFormatChanged(PayslipFormat? value)
        {
            IsRenamingPayslipFormat = false;
            OnPropertyChanged(nameof(CanRenameSelectedPayslipFormat));

            if (value is null) return;

            var fields = value.ToFieldSet();

            foreach (var choice in PayslipFieldChoices)
            {
                choice.Changed -= SavePayslipFieldChoices;
                choice.IsChecked = fields.Contains(choice.Field);
                choice.Changed += SavePayslipFieldChoices;
            }

            SavePayslipFieldChoices();
        }

        [ObservableProperty]
        private string _newPayslipFormatName = "";

        public bool CanSavePayslipFormat => NewPayslipFormatName.Trim().Length > 0;

        partial void OnNewPayslipFormatNameChanged(string value) =>
            OnPropertyChanged(nameof(CanSavePayslipFormat));

        /// <summary>فورم "احفظ باسم" مطويّة افتراضيًا — نفس سبب طيّها في القوالب</summary>
        [ObservableProperty]
        private bool _isAddingNewPayslipFormat;

        [RelayCommand]
        private void ToggleAddNewPayslipFormat()
        {
            IsAddingNewPayslipFormat = !IsAddingNewPayslipFormat;
            if (!IsAddingNewPayslipFormat) NewPayslipFormatName = "";
        }

        [RelayCommand]
        private void SavePayslipFormat()
        {
            var name = NewPayslipFormatName.Trim();
            if (name.Length == 0) return;

            // لو الاسم زي فورمات جاهز بالظبط، الحفظ كان بيعمل نسخة
            // تانية بنفس الاسم من غير ما يستبدلها — الكومبو كان بيبقى
            // فيه سطرين بنفس الاسم، والاختيار بعد الحفظ كان بيوقع على
            // الجاهز (الأول في الترتيب) بدل نسخة المستخدم، فتعديله كان
            // بيختفي فورًا من الشاشة وهو ساعتها متأكد إنه ضغط حفظ
            if (PayslipFormats.Any(f => f.IsBuiltIn && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                Notify.Warn($"\"{name}\" اسم فورمات جاهز مع البرنامج — اختار اسم تاني", "مش هينفع");
                return;
            }

            PayslipFormatStore.Save(PayslipFormat.FromFields(name, SelectedPayslipFields()));

            NewPayslipFormatName = "";
            IsAddingNewPayslipFormat = false;
            ReloadPayslipFormats();
            SelectedPayslipFormat = PayslipFormats.FirstOrDefault(f =>
                string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

            Notify.Info($"الفورمات \"{name}\" اتحفظ. هتلاقيه في القايمة.", "تم الحفظ");
        }

        [RelayCommand]
        private void DeletePayslipFormat(PayslipFormat? format)
        {
            if (format is null || format.IsBuiltIn) return;

            if (!Notify.Ask($"حذف فورمات \"{format.Name}\"؟", "تأكيد")) return;

            // بعد الحذف الكولكشن بتتبني من جديد بعناصر جديدة، فالمقارنة
            // بعد الحذف لازم تبقى بالاسم مش بمرجع الكائن القديم — وإلا
            // الكومبو بتاع الفورمات بيفضل شايل الاختيار القديم اللي
            // بقى مش موجود في القايمة، ويبان فاضي من غير سبب واضح
            var currentName = SelectedPayslipFormat?.Name;
            PayslipFormatStore.Delete(format.Name);
            ReloadPayslipFormats();

            SelectedPayslipFormat = string.Equals(currentName, format.Name, StringComparison.OrdinalIgnoreCase)
                ? null
                : PayslipFormats.FirstOrDefault(f => string.Equals(f.Name, currentName, StringComparison.OrdinalIgnoreCase));
        }

        [ObservableProperty]
        private bool _isRenamingPayslipFormat;

        [ObservableProperty]
        private string _renamePayslipFormatText = "";

        public bool CanRenameSelectedPayslipFormat =>
            SelectedPayslipFormat is not null && !SelectedPayslipFormat.IsBuiltIn;

        [RelayCommand]
        private void StartRenamePayslipFormat()
        {
            if (!CanRenameSelectedPayslipFormat) return;

            RenamePayslipFormatText = SelectedPayslipFormat!.Name;
            IsRenamingPayslipFormat = true;
        }

        /// <summary>
        /// بيحفظ الاسم **والبنود المعلّمة دلوقتي** مع بعض — مش الاسم بس.
        ///
        /// لو المستخدم اختار فورمات، عدّل البنود، وبعدين فتح مربع
        /// "إعادة التسمية" وضغط حفظ (زي ما بيحصل غالبًا لو مفيش زرار
        /// تاني ظاهر قدامه)، كان بيتوقّع إن تعديله يتحفظ — لكن
        /// PayslipFormatStore.Rename بتغيّر الاسم بس ومبتلمسش البنود،
        /// فتعديله كان بيختفي بصمت والفورمات القديم يفضل زي ما هو.
        /// عشان كده هنا بنحفظ فورمات كامل بالاسم الجديد (بالبنود
        /// الحالية) بدل ما ننادي Rename، وبعدين نشيل النسخة القديمة لو
        /// الاسم اتغيّر فعلاً.
        /// </summary>
        [RelayCommand]
        private void ConfirmRenamePayslipFormat()
        {
            if (SelectedPayslipFormat is null) return;

            var oldName = SelectedPayslipFormat.Name;
            var newName = RenamePayslipFormatText.Trim();

            if (newName.Length == 0)
            {
                Notify.Warn("اسم الفورمات مينفعش يبقى فاضي", "مش هينفع");
                return;
            }

            if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase)
                && PayslipFormats.Any(f => !f.IsBuiltIn && string.Equals(f.Name, newName, StringComparison.OrdinalIgnoreCase)))
            {
                Notify.Warn($"في فورمات اسمه \"{newName}\" بالفعل", "مش هينفع");
                return;
            }

            PayslipFormatStore.Save(PayslipFormat.FromFields(newName, SelectedPayslipFields()));
            if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
                PayslipFormatStore.Delete(oldName);

            IsRenamingPayslipFormat = false;
            ReloadPayslipFormats();
            SelectedPayslipFormat = PayslipFormats.FirstOrDefault(f =>
                string.Equals(f.Name, newName, StringComparison.OrdinalIgnoreCase));

            Notify.Info($"الفورمات \"{newName}\" اتحفظ بالبنود الحالية.", "تم");
        }

        [RelayCommand]
        private void CancelRenamePayslipFormat() => IsRenamingPayslipFormat = false;

        private void ReloadPayslipFormats()
        {
            PayslipFormats.Clear();
            foreach (var f in PayslipFormatStore.Load()) PayslipFormats.Add(f);
        }

        // ======================= قسيمة الأجر =======================

        /// <summary>
        /// القسيمة ورقة العامل نفسه — مش جدول، فمالهاش مكان في المُنشئ
        /// العام. بتظهر لما المستخدم يختار عامل واحد في الفلاتر: ساعتها
        /// بس السؤال "قسيمة مين؟" يبقى ليه إجابة.
        /// </summary>
        /// <summary>
        /// القسيمة ورقة عامل **واحد** في تقرير **أجور**، فبتظهر لما
        /// يبقى فيه واحد بالظبط متعلّم في الفلاتر — لا صفر (مفيش إجابة
        /// لسؤال "قسيمة مين؟") ولا اتنين (مش هنطبع قسيمة الأول ونسيب
        /// التاني).
        /// </summary>
        public bool CanPrintPayslip => IsWagesReport && SingleCheckedWorkerId is not null;

        private int? SingleCheckedWorkerId
        {
            get
            {
                var checkedWorkers = Workers.Where(w => w.IsChecked).Take(2).ToList();
                return checkedWorkers.Count == 1 ? checkedWorkers[0].Id : null;
            }
        }

        /// <summary>
        /// قسايم الأسبوع: ورقة تتطبع وتتقص بالطول، شريط لكل عامل.
        ///
        /// منفصلة عن "قسيمة أجر" (اللي بتطبع عامل واحد على نافذة): دي
        /// للتوزيع الأسبوعي على القسم كله دفعة واحدة، وبتخرج Excel عشان
        /// تتطبع من أي مكان من غير ما البرنامج يبقى مفتوح.
        ///
        /// الأرقام بتيجي من PayrollService — نفس اللي كشف الأجور بيقوله
        /// بالحرف، فالورقة اللي في إيد العامل متطابقة مع الكشف.
        /// </summary>
        [RelayCommand]
        private async Task ExportPayslipStripsAsync()
        {
            var (from, to) = SelectedPeriod.Kind == ReportPeriodKind.Custom
                ? (CustomFrom, CustomTo)
                : ReportPeriod.Resolve(SelectedPeriod.Kind);

            PeriodPayrollDto payroll;

            using (var scope = _scopeFactory.CreateScope())
                payroll = await scope.ServiceProvider
                    .GetRequiredService<PayrollService>()
                    .GetPeriodPayrollAsync(from, to);

            // فلتر العمال المختارين بينطبق هنا كمان: اللي عامل تقرير
            // لعمال معيّنين عايز قسايمهم هما بس
            var selected = CheckedIds(Workers);
            if (selected is { Count: > 0 })
                payroll = new PeriodPayrollDto
                {
                    From = payroll.From,
                    To = payroll.To,
                    Workers = payroll.Workers.Where(w => selected.Contains(w.WorkerId)).ToList()
                };

            if (payroll.Workers.Count == 0)
            {
                Notify.Info("مفيش عمال ليهم أجر في المدة دي", "مفيش قسايم");
                return;
            }

            var settings = AppSettingsStore.Load();
            var options = new ReportExportOptions
            {
                FactoryName = settings.FactoryName,
                LogoPath = settings.LogoPath
            };

            var pages = (int)Math.Ceiling(
                payroll.Workers.Count / (double)PayslipStripExcelService.SlipsPerPage);

            var fields = SelectedPayslipFields();

            await ExcelExport.RunAsync(
                "حفظ قسايم الأجر",
                $"قسايم الأجر {from:yyyy-MM-dd}",
                path =>
                {
                    using var scope = _scopeFactory.CreateScope();
                    scope.ServiceProvider.GetRequiredService<PayslipStripExcelService>()
                        .Export(payroll, path, options, fields);
                    return Task.CompletedTask;
                });

            Notify.Info(
                $"{payroll.Workers.Count} قسيمة على {pages} ورقة " +
                $"({PayslipStripExcelService.SlipsPerPage} في الورقة).\n" +
                "اطبع بالعرض (Landscape) وقص على الخطوط الرأسية.",
                "القسايم جاهزة");
        }

        [RelayCommand]
        private async Task PrintPayslipAsync()
        {
            if (SingleCheckedWorkerId is not { } workerId)
            {
                Notify.Info("علّم على عامل واحد بس في الفلاتر عشان تطبع قسيمته");
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
            IsRenamingTemplate = false;
            OnPropertyChanged(nameof(CanRenameSelectedTemplate));

            if (value is null) return;

            _suppressPreview = true;
            try
            {
                SelectedSubject = Subjects.First(s => s.Subject == value.Subject);
                SelectedGrouping = Groupings.FirstOrDefault(g => g.Grouping == value.GroupBy)
                                   ?? Groupings.FirstOrDefault();
                SelectedPeriod = Periods.First(p => p.Kind == value.Period);

                RefreshThenByOptions();
                var levels = value.ThenBy ?? new List<ReportGrouping>();
                ThenBy1 = ThenByOptions.FirstOrDefault(o => levels.Count > 0 && o.Grouping == levels[0])
                          ?? ThenByOptions[0];
                ThenBy2 = ThenByOptions.FirstOrDefault(o => levels.Count > 1 && o.Grouping == levels[1])
                          ?? ThenByOptions[0];
                ShowThenBy = levels.Count > 0; // القالب لو محدد مستويات إضافية، تفضل ظاهرة له

                ApplyFilters(value);
                ApplyColumns(value);

                SelectedSort = SortOptions.FirstOrDefault(o => o.Key == value.SortKey) ?? SortOptions[0];
                SortDescending = value.SortDescending;
                SelectedTopN = TopNOptions.FirstOrDefault(o => o.Count == value.TopN) ?? TopNOptions[0];
                ShowTotals = value.ShowTotals;
                CompareWithPrevious = value.CompareWithPrevious;

                ExportDetailSheet = value.ExportDetailSheet;
                ExportSheetPerGroup = value.ExportSheetPerGroup;
            }
            finally
            {
                _suppressPreview = false;
            }

            OnFilterToggled();
        }

        private void ApplyFilters(ReportTemplate template)
        {
            var workerIds = template.WorkerIds?.ToHashSet() ?? new HashSet<int>();
            foreach (var w in Workers) w.IsChecked = workerIds.Contains(w.Id);

            var productIds = template.ProductIds?.ToHashSet() ?? new HashSet<int>();
            foreach (var p in Products) p.IsChecked = productIds.Contains(p.Id);

            var stageId = template.StageIds?.FirstOrDefault();
            SelectedStage = Stages.FirstOrDefault(s => s.Id == stageId) ?? Stages.FirstOrDefault();

            SelectedWorkerKind = WorkerKinds.FirstOrDefault(k => k.Kind == template.WorkerKind)
                                 ?? WorkerKinds[0];
        }

        /// <summary>
        /// بيطبّق أعمدة القالب: الترتيب من القالب، والأعمدة اللي القالب
        /// ميعرفهاش بتفضل في الآخر ظاهرة.
        ///
        /// قالب اتحفظ قبل ما عمود جديد يتزوّد لازم يفضل يفتح — والعمود
        /// الجديد يبان، مش يختفي من غير ما حد ياخد باله.
        /// </summary>
        private void ApplyColumns(ReportTemplate template)
        {
            RefreshColumnChoices(); // أعمدة الموضوع الجديد بحالتها الأصلية

            if (template.ColumnLayout is not { Count: > 0 }) return;

            var byKey = ColumnChoices.ToDictionary(c => c.Key);
            var ordered = new List<ColumnChoiceItem>();

            foreach (var saved in template.ColumnLayout)
            {
                if (!byKey.TryGetValue(saved.Key, out var item)) continue; // عمود اتشال من البرنامج

                item.IsVisible = saved.Visible;
                if (!string.IsNullOrWhiteSpace(saved.Header)) item.Header = saved.Header!;
                if (saved.Sums is { } sums) item.Sums = sums;

                ordered.Add(item);
            }

            foreach (var item in ColumnChoices)
                if (!ordered.Contains(item))
                    ordered.Add(item);

            ColumnChoices.Clear();
            foreach (var item in ordered) ColumnChoices.Add(item);

            RefreshSortOptions();
            OnPropertyChanged(nameof(HiddenColumnCount));
            OnPropertyChanged(nameof(HasColumnChanges));
        }

        [ObservableProperty]
        private string _newTemplateName = "";

        public bool CanSaveTemplate => NewTemplateName.Trim().Length > 0;

        partial void OnNewTemplateNameChanged(string value) =>
            OnPropertyChanged(nameof(CanSaveTemplate));

        /// <summary>
        /// فورم "احفظ باسم" مطويّة افتراضيًا — اختيار قالب جاهز هو
        /// المسار الأكتر استخدامًا، وحفظ قالب جديد أندر بكتير. ظهورها
        /// على طول كان زوّد عدد الخيارات الظاهرة من غير داعي.
        /// </summary>
        [ObservableProperty]
        private bool _isAddingNewTemplate;

        [RelayCommand]
        private void ToggleAddNewTemplate()
        {
            IsAddingNewTemplate = !IsAddingNewTemplate;
            if (!IsAddingNewTemplate) NewTemplateName = "";
        }

        /// <summary>
        /// مستويات التجميع الإضافية ("ثم... ثم...") مطويّة افتراضيًا —
        /// معظم التقارير تجميع بمستوى واحد، والاتنين الإضافيين كانوا
        /// ظاهرين على طول لأي تقرير إنتاج حتى لو محدش محتاجهم.
        /// </summary>
        [ObservableProperty]
        private bool _showThenBy;

        [RelayCommand]
        private void SaveTemplate()
        {
            var name = NewTemplateName.Trim();
            if (name.Length == 0) return;

            // القالب بيحفظ **كل** اللي المستخدم ظبّطه، مش الموضوع
            // والمدة بس — وإلا هيعيد نفس الشغل كل مرة يفتحه
            ReportTemplateStore.Save(new ReportTemplate
            {
                Name = name,
                Subject = SelectedSubject.Subject,
                GroupBy = SelectedGrouping?.Grouping ?? ReportGrouping.Worker,
                ThenBy = SelectedThenBy(),
                Period = SelectedPeriod.Kind,

                WorkerIds = CheckedIds(Workers)?.ToList(),
                ProductIds = CheckedIds(Products)?.ToList(),
                StageIds = SelectedStage?.Id is { } s ? new List<int> { s } : null,
                WorkerKind = SelectedWorkerKind?.Kind ?? WorkerKindFilter.All,

                ColumnLayout = ColumnChoices.Select(c => c.ToChoice()).ToList(),
                SortKey = SelectedSort?.Key,
                SortDescending = SortDescending,
                TopN = SelectedTopN?.Count,
                ShowTotals = ShowTotals,
                CompareWithPrevious = CompareWithPrevious,

                ExportDetailSheet = ExportDetailSheet,
                ExportSheetPerGroup = ExportSheetPerGroup
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

        // ------- إعادة تسمية قالب -------

        [ObservableProperty]
        private bool _isRenamingTemplate;

        [ObservableProperty]
        private string _renameTemplateText = "";

        public bool CanRenameSelectedTemplate =>
            SelectedTemplate is not null && !SelectedTemplate.IsBuiltIn;

        [RelayCommand]
        private void StartRenameTemplate()
        {
            if (!CanRenameSelectedTemplate) return;

            RenameTemplateText = SelectedTemplate!.Name;
            IsRenamingTemplate = true;
        }

        [RelayCommand]
        private void ConfirmRenameTemplate()
        {
            if (SelectedTemplate is null) return;

            try
            {
                ReportTemplateStore.Rename(SelectedTemplate.Name, RenameTemplateText);
                var newName = RenameTemplateText.Trim();

                IsRenamingTemplate = false;
                ReloadTemplates();
                SelectedTemplate = Templates.FirstOrDefault(t =>
                    string.Equals(t.Name, newName, StringComparison.OrdinalIgnoreCase));

                Notify.Info($"القالب بقى اسمه \"{newName}\".", "تم");
            }
            catch (InvalidOperationException ex)
            {
                Notify.Warn(ex.Message, "مش هينفع");
            }
        }

        [RelayCommand]
        private void CancelRenameTemplate() => IsRenamingTemplate = false;

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

                foreach (var w in Workers) w.PropertyChanged -= OnCheckableChanged;
                Workers.Clear();

                foreach (var w in await workerRepo.GetAllWithSkillsAsync())
                {
                    var item = new CheckableItem(w.Id, w.FullName);
                    item.PropertyChanged += OnCheckableChanged;
                    Workers.Add(item);
                }

                foreach (var p in Products) p.PropertyChanged -= OnCheckableChanged;
                Products.Clear();

                Stages.Clear();
                Stages.Add(new StageFilterItem(null, "كل المراحل"));

                foreach (var p in (await productRepo.GetAllWithStagesAsync()).OrderBy(p => p.Name))
                {
                    var item = new CheckableItem(p.Id, p.Name);
                    item.PropertyChanged += OnCheckableChanged;
                    Products.Add(item);

                    // اسم المرحلة لوحده مش كافي: "قص" موجودة في أكتر من
                    // منتج، فاسم المنتج لازم يبقى معاها
                    foreach (var stage in ProductionLine.Active(p))
                        Stages.Add(new StageFilterItem(stage.Id, $"{p.Name} — {stage.StageName}"));
                }

                // جوّه الـ try عن قصد: لسه _ready = false هنا، فالسطور
                // دي مش هتطلب معاينة لوحدها
                SelectedStage = Stages[0];
                SelectedWorkerKind = WorkerKinds[0];
                SelectedTopN = TopNOptions[0];

                RefreshThenByOptions();
                RefreshColumnChoices();
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
