using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Helpers;
using WorkforceManager.Core.Interfaces;
using System.Linq;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// رحلة إنتاج لمنتج واحد داخل شاشة التسجيل اليومي. الشاشة بتعرض
    /// رحلة أو أكتر في نفس اليوم (منتج أو أكتر شغالين مع بعض)، وكل
    /// رحلة مستقلة بمنتجها ومراحلها وتوزيع عمالها ونطاقاتها وحفظها.
    ///
    /// كل المنطق التفاعلي هنا: اختيار المنتج بيبني بطاقات مراحله،
    /// النطاقات بتحسب إنتاج كل مرحلة لحظيًا، القطع بتتوزع بالتساوي على
    /// عمال المرحلة (مع تعديل يدوي)، والمعاينة والتحذيرات بتتحدث فورًا.
    /// الحفظ بيمر على ProductionFlowService (مصدر الحقيقة للقواعد).
    /// </summary>
    public partial class FlowSessionViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;

        /// <summary>اليوم بييجي من الشاشة الأم (مشترك بين كل الرحلات والتبويبات)</summary>
        private readonly Func<DateTime> _getEntryDate;

        /// <summary>بيتنادى بعد حفظ ناجح — الشاشة الأم بتحدّث الحضور (الحضور التلقائي بيظهر فورًا)</summary>
        private readonly Func<Task> _onSavedAsync;

        /// <summary>
        /// كل الرحلات المفتوحة على الشاشة (بما فيها دي). قاعدة "العامل
        /// ميشتغلش على أكتر من مرحلة" لازم تشوف اللي لسه متحفظش كمان —
        /// المستخدم ممكن يفتح منتجين ويحط نفس العامل في الاتنين قبل ما
        /// يحفظ أي واحدة، والتعارض ده حقيقي بنفس القدر.
        /// </summary>
        private readonly Func<IEnumerable<FlowSessionViewModel>> _getOpenSessions;

        /// <summary>
        /// التكليفات اللي المستخدم أكّد عليها صراحة في الرحلة دي (لليوم
        /// الحالي). الغرض منها **مش** حفظ اختياره لعمليات جاية — ده بس
        /// عشان ميتسألش تاني عند الحفظ على نفس التكليف اللي أكّده وهو
        /// بيضيفه. بتتفضى مع إعادة تحميل الرحلة أو تغيير اليوم.
        /// </summary>
        private readonly HashSet<(int StageId, int WorkerId)> _confirmedAssignments = new();

        /// <summary>
        /// بيمنع إعادة الحساب أثناء ما الكود نفسه بيعدّل القيم (بناء الصفوف
        /// أو التوزيع التلقائي) — من غيره كل تعديل برمجي كان هيشغّل
        /// سلسلة إعادة حساب لا نهائية.
        /// </summary>
        private bool _suppressCallbacks;

        public FlowSessionViewModel(
            IServiceScopeFactory scopeFactory,
            IReadOnlyList<ProductOption> products,
            Func<DateTime> getEntryDate,
            Func<Task> onSavedAsync,
            Func<IEnumerable<FlowSessionViewModel>> getOpenSessions)
        {
            _scopeFactory = scopeFactory;
            Products = products;
            _getEntryDate = getEntryDate;
            _onSavedAsync = onSavedAsync;
            _getOpenSessions = getOpenSessions;
        }

        /// <summary>كل المنتجات النشطة (قائمة مشتركة بين كل الرحلات — للقراءة بس)</summary>
        public IReadOnlyList<ProductOption> Products { get; }

        [ObservableProperty]
        private ProductOption? _selectedProduct;

        partial void OnSelectedProductChanged(ProductOption? value)
        {
            // تغيير المنتج بيعيد بناء بطاقات المراحل (وأي خطأ بيظهر مش بيضيع بصمت)
            SafeAsync.Run(ReloadAsync);
        }

        /// <summary>مراحل المنتج المختار — بطاقة لكل مرحلة بعمالها المؤهلين</summary>
        public ObservableCollection<FlowStageRow> FlowStages { get; } = new();

        /// <summary>نطاقات الإنتاج: "من مرحلة إلى مرحلة: عدد قطع"</summary>
        public ObservableCollection<FlowRangeRow> FlowRanges { get; } = new();

        /// <summary>
        /// القطع الواقفة في خط المنتج ده من أيام فاتت. النطاق اللي بيبدأ من
        /// نص الخط لازم يختار واحدة منها — ده اللي بيمنع إن قطع تظهر من
        /// العدم أو تتحسب مرتين.
        /// </summary>
        public IReadOnlyList<OpenBatchDto> OpenBatches { get; private set; } = Array.Empty<OpenBatchDto>();

        public bool HasOpenBatches => OpenBatches.Count > 0;

        public string OpenBatchesText => OpenBatches.Count == 0
            ? ""
            : $"{OpenBatches.Sum(b => b.Quantity)} قطعة واقفة في {OpenBatches.Count} دفعة من أيام فاتت";

        /// <summary>معاينة يوميات كل عامل قبل الحفظ (بتتحدث لحظيًا)</summary>
        public ObservableCollection<FlowWorkerTotalDto> FlowPreview { get; } = new();

        /// <summary>تحذيرات لحظية (نطاقات متداخلة، توزيع مش مظبوط...) قبل ما المستخدم يحفظ</summary>
        [ObservableProperty]
        private string _flowWarning = string.Empty;

        /// <summary>هل المستخدم كتب أي حاجة في الرحلة دي؟ (للتأكيد قبل إزالتها)</summary>
        public bool HasUserInput =>
            FlowStages.Any(s => s.AssignedWorkers.Count > 0) ||
            FlowRanges.Any(r => !string.IsNullOrWhiteSpace(r.PiecesText));

        /// <summary>يبني بطاقات المراحل وعمالها المؤهلين للمنتج المختار (وبيتنادى برضه عند تغيير اليوم)</summary>
        public async Task ReloadAsync()
        {
            _suppressCallbacks = true;
            try
            {
                FlowStages.Clear();
                FlowRanges.Clear();
                FlowPreview.Clear();
                FlowWarning = string.Empty;
                // الموافقات بتخص التكليفات اللي كانت على الشاشة — الرحلة بتبدأ نظيفة
                _confirmedAssignments.Clear();

                var product = SelectedProduct;
                if (product is null || product.Stages.Count == 0) return;

                using var scope = _scopeFactory.CreateScope();
                var workerRepo = scope.ServiceProvider.GetRequiredService<IWorkerRepository>();
                var productionRepo = scope.ServiceProvider.GetRequiredService<IDailyProductionRepository>();
                var batchService = scope.ServiceProvider.GetRequiredService<ProductionBatchService>();

                // القطع الواقفة من أيام فاتت — دي اللي المستخدم بيختار منها
                // لما يكون بيكمّل شغل مش بيبدأ من الأول
                OpenBatches = await batchService.GetOpenBatchesAsync(product.ProductId, _getEntryDate());
                OnPropertyChanged(nameof(OpenBatches));
                OnPropertyChanged(nameof(HasOpenBatches));
                OnPropertyChanged(nameof(OpenBatchesText));

                // المؤهلين لكل مراحل المنتج باستعلام واحد
                var skillsByStage = (await workerRepo.GetSkillsForProductAsync(product.ProductId))
                    .ToLookup(ws => ws.ProductionStageId);

                // الإنتاج المسجل بالفعل في اليوم ده على مراحل المنتج (تحذير من الإدخال المزدوج)
                var stageIds = product.Stages.Select(s => s.StageId).ToHashSet();
                var alreadyByStage = (await productionRepo.GetByDateAsync(_getEntryDate()))
                    .Where(r => stageIds.Contains(r.ProductionStageId))
                    .GroupBy(r => r.ProductionStageId)
                    .ToDictionary(g => g.Key, g => g.Sum(r => r.PieceCount));

                foreach (var stage in product.Stages)
                {
                    alreadyByStage.TryGetValue(stage.StageId, out var already);
                    var row = new FlowStageRow(AddWorkerToStageAsync)
                    {
                        StageId = stage.StageId,
                        DisplayOrder = stage.DisplayOrder,
                        StageName = stage.StageName,
                        Quota = stage.PiecesPerWorkday,
                        QualifiedWorkers = skillsByStage[stage.StageId]
                            .Select(ws => new WorkerPick(ws.WorkerId, ws.Worker.FullName))
                            .ToList(),
                        AlreadyText = already > 0 ? $"مسجل اليوم: {already}" : ""
                    };

                    row.ApplyWorkerFilter(); // القايمة تبدأ كاملة قبل أي بحث
                    FlowStages.Add(row);
                }

                // نطاق افتراضي جاهز: من أول مرحلة لآخر مرحلة — لو اليوم كله
                // بنفس العدد يبقى المستخدم يكتب رقم واحد بس ويحفظ
                FlowRanges.Add(new FlowRangeRow(product.Stages, OpenBatches, OnStructureEdited, RemoveRange)
                {
                    FromStage = product.Stages.First(),
                    ToStage = product.Stages.Last()
                });
            }
            finally
            {
                _suppressCallbacks = false;
            }
        }

        /// <summary>تغيير هيكلي (نطاق اتعدل/اتضاف/اتشال أو عامل اتضاف/اتشال) → إعادة حساب وتوزيع</summary>
        private void OnStructureEdited()
        {
            if (_suppressCallbacks) return;
            RecomputeFlow();
        }

        /// <summary>تعديل يدوي في نصيب عامل → تحديث المعاينة بس (من غير ما نداس على تعديله)</summary>
        private void OnSharesEdited()
        {
            if (_suppressCallbacks) return;
            RecomputeTotals(new List<string>());
        }

        /// <summary>
        /// إعادة الحساب الكاملة: بيحسب إنتاج كل مرحلة من النطاقات، وبيوزّع
        /// قطع كل مرحلة بالتساوي على عمالها (الباقي بيتوزع واحدة واحدة على
        /// الأوائل)، وبعدها بيحدّث المعاينة. أي مشكلة بتظهر كتحذير لحظي.
        /// </summary>
        private void RecomputeFlow()
        {
            var warnings = new List<string>();

            _suppressCallbacks = true;
            try
            {
                // 1) إنتاج كل مرحلة من النطاقات (بنفس قواعد الخدمة: بلا تداخل وبترتيب صحيح)
                foreach (var row in FlowStages) row.ComputedPieces = 0;

                var indexByStageId = FlowStages
                    .Select((row, index) => (row.StageId, index))
                    .ToDictionary(x => x.StageId, x => x.index);

                foreach (var range in FlowRanges)
                {
                    if (range.FromStage is null || range.ToStage is null) continue;
                    if (string.IsNullOrWhiteSpace(range.PiecesText)) continue;

                    if (!int.TryParse(range.PiecesText.Trim(), out var pieces) || pieces <= 0)
                    {
                        warnings.Add($"⚠ عدد القطع \"{range.PiecesText}\" مش رقم صحيح موجب");
                        continue;
                    }

                    var fromIndex = indexByStageId[range.FromStage.StageId];
                    var toIndex = indexByStageId[range.ToStage.StageId];
                    if (fromIndex > toIndex)
                    {
                        warnings.Add($"⚠ نطاق معكوس: \"{range.FromStage.StageName}\" بتيجي بعد \"{range.ToStage.StageName}\" في الترتيب");
                        continue;
                    }

                    for (var i = fromIndex; i <= toIndex; i++)
                    {
                        if (FlowStages[i].ComputedPieces != 0)
                        {
                            warnings.Add($"⚠ مرحلة \"{FlowStages[i].StageName}\" واقعة في أكتر من نطاق — النطاقات ميصحش تتداخل");
                            continue;
                        }
                        FlowStages[i].ComputedPieces = pieces;
                    }
                }

                // 2) توزيع متساوٍ تلقائي على عمال كل مرحلة (قابل للتعديل اليدوي بعدها)
                foreach (var row in FlowStages)
                {
                    var workers = row.AssignedWorkers;
                    if (workers.Count == 0) continue;

                    if (row.ComputedPieces == 0)
                    {
                        foreach (var share in workers) share.SharePieces = "";
                        continue;
                    }

                    var baseShare = row.ComputedPieces / workers.Count;
                    var remainder = row.ComputedPieces % workers.Count;
                    for (var i = 0; i < workers.Count; i++)
                        workers[i].SharePieces = (baseShare + (i < remainder ? 1 : 0)).ToString();
                }
            }
            finally
            {
                _suppressCallbacks = false;
            }

            RecomputeTotals(warnings);
        }

        /// <summary>يبني معاينة إجمالي كل عامل (قطع + يوميات) ويجمّع التحذيرات في سطر واحد</summary>
        private void RecomputeTotals(List<string> warnings)
        {
            var totals = new Dictionary<int, (string Name, int Pieces, decimal Workdays)>();

            foreach (var row in FlowStages)
            {
                if (row.ComputedPieces == 0 && row.AssignedWorkers.Count == 0) continue;

                var stageSum = 0;
                foreach (var share in row.AssignedWorkers)
                {
                    if (!int.TryParse(share.SharePieces?.Trim(), out var pieces) || pieces <= 0) continue;

                    stageSum += pieces;
                    var workdays = Math.Round((decimal)pieces / row.Quota, 2);
                    totals[share.WorkerId] = totals.TryGetValue(share.WorkerId, out var t)
                        ? (t.Name, t.Pieces + pieces, t.Workdays + workdays)
                        : (share.WorkerName, pieces, workdays);
                }

                // مرحلة عليها إنتاج لكن التوزيع مش مساويه — تحذير قبل ما الحفظ يرفضها
                if (row.ComputedPieces > 0 && stageSum != row.ComputedPieces)
                    warnings.Add($"⚠ مرحلة \"{row.StageName}\": مجموع التوزيع ({stageSum}) ≠ إنتاج المرحلة ({row.ComputedPieces})");
            }

            FlowPreview.Clear();
            foreach (var t in totals.Values.OrderByDescending(t => t.Workdays))
            {
                FlowPreview.Add(new FlowWorkerTotalDto
                {
                    WorkerName = t.Name,
                    TotalPieces = t.Pieces,
                    TotalWorkdays = t.Workdays
                });
            }

            FlowWarning = string.Join("\n", warnings.Distinct());

            // تلوين البطاقات وعدّاد الجاهزية بيتحدّثوا مع أي تغيير
            foreach (var row in FlowStages) row.RefreshState();
            RefreshReadiness();
        }

        // ------- عدّاد جاهزية الرحلة -------

        /// <summary>عدد المراحل الداخلة في الرحلة النهارده (عليها إنتاج)</summary>
        public int StagesInFlowCount => FlowStages.Count(s => s.ComputedPieces > 0);

        /// <summary>منها كام مرحلة جاهزة فعلاً للحفظ</summary>
        public int ReadyStagesCount => FlowStages.Count(s => s.IsReady);

        /// <summary>"7 من 11 مرحلة جاهزة" — بيظهر فوق البطاقات</summary>
        public string ReadinessText => StagesInFlowCount == 0
            ? "اكتب عدد القطع في النطاق عشان المراحل تشتغل"
            : $"{ReadyStagesCount} من {StagesInFlowCount} مرحلة جاهزة";

        /// <summary>كل المراحل الداخلة في الرحلة جاهزة؟ (بيلوّن العدّاد أخضر)</summary>
        public bool AllReady => StagesInFlowCount > 0 && ReadyStagesCount == StagesInFlowCount;

        private void RefreshReadiness()
        {
            OnPropertyChanged(nameof(StagesInFlowCount));
            OnPropertyChanged(nameof(ReadyStagesCount));
            OnPropertyChanged(nameof(ReadinessText));
            OnPropertyChanged(nameof(AllReady));
        }

        // ------- أوامر الرحلة -------

        /// <summary>
        /// بيجيب توزيع عمال آخر يوم اشتغل فيه المنتج ده وبيحطه زي ما هو.
        ///
        /// **الأعداد مش بتتنسخ** عن قصد (قرار متفق عليه): العمال بيتحطوا
        /// على مراحلهم، والمستخدم بيكتب قطع النهارده. نسخ أرقام إمبارح
        /// كان بيخاطر إن رقم قديم يتحفظ من غير ما حد ياخد باله.
        /// </summary>
        [RelayCommand]
        private async Task RepeatLastDayAsync()
        {
            if (SelectedProduct is not { } product)
            {
                MessageBox.Show("اختار المنتج الأول", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            LastFlowDto? last;
            using (var scope = _scopeFactory.CreateScope())
            {
                var flowService = scope.ServiceProvider.GetRequiredService<ProductionFlowService>();
                last = await flowService.GetLastFlowAsync(product.ProductId, _getEntryDate());
            }

            if (last is null || last.Assignments.Count == 0)
            {
                MessageBox.Show(
                    $"مفيش إنتاج متسجل على \"{product.Name}\" في آخر شهرين — مفيش حاجة تتكرر.",
                    "مفيش يوم سابق", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // العمال اللي لسه مؤهلين فعلاً (ممكن مهارة اتشالت أو عامل اتوقف)
            var stageById = FlowStages.ToDictionary(s => s.StageId);
            var applicable = last.Assignments
                .Where(a => stageById.ContainsKey(a.ProductionStageId))
                .Where(a => stageById[a.ProductionStageId].QualifiedWorkers.Any(w => w.WorkerId == a.WorkerId))
                .ToList();

            var skipped = last.Assignments.Count - applicable.Count;

            var confirmMessage =
                $"هيتحط توزيع يوم {last.Date:yyyy/MM/dd} على \"{product.Name}\":\n" +
                $"  • {applicable.Count} عامل على مراحلهم\n" +
                (skipped > 0 ? $"  • {skipped} اتخطوا (مبقوش مؤهلين أو اتوقفوا)\n" : "") +
                "\nالأعداد مش هتتنسخ — هتكتبها انت.\n" +
                "التوزيع الحالي على الشاشة هيتمسح.";

            if (MessageBox.Show(confirmMessage, "تكرار يوم سابق",
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            _suppressCallbacks = true;
            try
            {
                // البداية من صفحة نظيفة عشان مايبقاش فيه خلط بين القديم والجديد
                foreach (var row in FlowStages) row.AssignedWorkers.Clear();
                _confirmedAssignments.Clear();

                foreach (var assignment in applicable)
                {
                    var stage = stageById[assignment.ProductionStageId];
                    if (stage.AssignedWorkers.Any(s => s.WorkerId == assignment.WorkerId)) continue;

                    stage.AssignedWorkers.Add(new FlowShareEntry(
                        stage, assignment.WorkerId, assignment.WorkerName, OnSharesEdited, RemoveWorkerShare));
                    stage.ApplyWorkerFilter(); // اللي اتحمّل من المحفوظ يختفي من الاقتراحات
                }
            }
            finally
            {
                _suppressCallbacks = false;
            }

            RecomputeFlow();
        }

        [RelayCommand]
        private void AddRange()
        {
            if (SelectedProduct is null) return;
            FlowRanges.Add(new FlowRangeRow(SelectedProduct.Stages, OpenBatches, OnStructureEdited, RemoveRange));
        }

        /// <summary>بيتنادى من زرار الحذف اللي على سطر النطاق نفسه</summary>
        private void RemoveRange(FlowRangeRow range)
        {
            FlowRanges.Remove(range);
            RecomputeFlow();
        }

        /// <summary>
        /// بيتنادى من زرار "＋ عامل" اللي على بطاقة المرحلة نفسها.
        ///
        /// هنا بيتطبّق تحذير "العامل مكلّف بحاجة تانية النهارده": بنتحقق
        /// **قبل** ما الشريحة تتضاف على الشاشة أصلاً — يعني لو المستخدم
        /// لغى، مفيش حاجة تترجّع لأن مفيش حاجة اتعملت من الأساس (ولا في
        /// الشاشة ولا في قاعدة البيانات).
        ///
        /// ده تحذير سريع للراحة بس — الخدمة بتعيد نفس التحقق وقت الحفظ
        /// وهي مصدر الحقيقة الوحيد.
        /// </summary>
        private async Task AddWorkerToStageAsync(FlowStageRow stage)
        {
            if (stage.SelectedWorkerToAdd is not { } pick) return;
            if (SelectedProduct is not { } product) return;

            // منع إضافة نفس العامل مرتين لنفس المرحلة في نفس الرحلة
            if (stage.AssignedWorkers.Any(s => s.WorkerId == pick.WorkerId)) return;

            var attempted = new WorkerAssignmentDto
            {
                WorkerId = pick.WorkerId,
                WorkerName = pick.Name,
                ProductId = product.ProductId,
                ProductName = product.Name,
                ProductionStageId = stage.StageId,
                StageName = stage.StageName
            };

            // نفس قاعدة الخدمة بالحرف (WorkerAssignmentGuard.Evaluate) — مش نسخة تانية منها
            var known = await LoadKnownAssignmentsAsync();
            var check = WorkerAssignmentGuard.Evaluate(known, new[] { attempted });

            // تكرار حرفي: مسجل بالفعل على نفس المرحلة النهارده — مش حالة تأكيد
            if (check.HasDuplicates)
            {
                MessageBox.Show(
                    $"العامل \"{pick.Name}\" مسجل بالفعل على مرحلة \"{stage.StageName}\" النهارده.\n" +
                    "لو عايز تعدّل عدد قطعه، استخدم تبويب \"سجلات اليوم\".",
                    "مسجل بالفعل", MessageBoxButton.OK, MessageBoxImage.Information);
                stage.ResetWorkerPicker();
                return;
            }

            // تعارض: مكلّف بمرحلة/منتج تاني — تأكيد صريح، والافتراضي "لأ"
            if (check.RequiresConfirmation && !ConfirmOverride(check.Conflicts))
            {
                stage.ResetWorkerPicker();
                return; // إلغاء: مفيش أي تغيير لا على الشاشة ولا في البيانات
            }

            if (check.RequiresConfirmation)
                _confirmedAssignments.Add((stage.StageId, pick.WorkerId));

            stage.AssignedWorkers.Add(
                new FlowShareEntry(stage, pick.WorkerId, pick.Name, OnSharesEdited, RemoveWorkerShare));
            stage.ResetWorkerPicker();
            RecomputeFlow(); // إعادة التوزيع المتساوي بعد إضافة عامل
        }

        /// <summary>بيتنادى من زرار ✕ اللي على شريحة العامل نفسها</summary>
        private void RemoveWorkerShare(FlowShareEntry share)
        {
            share.Parent.AssignedWorkers.Remove(share);
            share.Parent.ApplyWorkerFilter(); // رجّعه لقايمة الاقتراحات تاني
            // الموافقة كانت على التكليف ده بالذات — بيشيلها معاه عشان لو
            // اتضاف تاني يتسأل من جديد (مفيش "افتكر اختياري")
            _confirmedAssignments.Remove((share.Parent.StageId, share.WorkerId));
            RecomputeFlow(); // إعادة التوزيع المتساوي بعد إزالة عامل
        }

        /// <summary>
        /// تكليفات اليوم اللي القاعدة بتتقاس عليها: المحفوظ في قاعدة
        /// البيانات + اللي لسه على الشاشة في أي رحلة مفتوحة.
        /// </summary>
        private async Task<List<WorkerAssignmentDto>> LoadKnownAssignmentsAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var guard = scope.ServiceProvider.GetRequiredService<WorkerAssignmentGuard>();

            var saved = await guard.GetDayAssignmentsAsync(_getEntryDate());
            var onScreen = _getOpenSessions().SelectMany(s => s.CurrentAssignments());

            return saved.Concat(onScreen).ToList();
        }

        /// <summary>تكليفات الرحلة دي زي ما هي على الشاشة دلوقتي (لسه متحفظتش)</summary>
        internal IEnumerable<WorkerAssignmentDto> CurrentAssignments()
        {
            if (SelectedProduct is not { } product) yield break;

            foreach (var stage in FlowStages)
                foreach (var share in stage.AssignedWorkers)
                    yield return new WorkerAssignmentDto
                    {
                        WorkerId = share.WorkerId,
                        WorkerName = share.WorkerName,
                        ProductId = product.ProductId,
                        ProductName = product.Name,
                        ProductionStageId = stage.StageId,
                        StageName = stage.StageName
                    };
        }

        /// <summary>
        /// هل التعارض ده اتأكد عليه بالفعل وإحنا بنضيف العامل؟
        ///
        /// بنقارن بطرفي التعارض مش بالطرف "المطلوب" بس: الخدمة بترتب
        /// التكليفات بترتيب المراحل في خط الإنتاج، واللي المستخدم أكّد
        /// عليه ممكن يطلع هو "المكلّف به بالفعل" لو كان أضاف المرحلة
        /// المتأخرة الأول — وساعتها كان هيتسأل تاني على نفس الحاجة.
        /// </summary>
        private bool AlreadyConfirmed(AssignmentConflictDto conflict) =>
            _confirmedAssignments.Contains(
                (conflict.Attempted.ProductionStageId, conflict.Attempted.WorkerId)) ||
            _confirmedAssignments.Contains(
                (conflict.Existing.ProductionStageId, conflict.Existing.WorkerId));

        /// <summary>
        /// مربع التأكيد الموحّد لكل تعارضات التكليف — مكان واحد عشان
        /// الرسالة والزراير تفضل واحدة سواء ظهرت عند الإضافة أو عند الحفظ.
        /// الافتراضي "لأ" (الاختيار الآمن): Enter/Esc = إلغاء.
        /// </summary>
        private static bool ConfirmOverride(IReadOnlyList<AssignmentConflictDto> conflicts)
        {
            var question = string.Join("\n\n", conflicts.Select(c => c.ConfirmationQuestion));

            return MessageBox.Show(
                question + "\n\n(عامل واحد المفروض ميشتغلش على أكتر من مرحلة في نفس الوقت)",
                "تأكيد تكليف إضافي",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        [RelayCommand]
        private async Task SaveFlowAsync()
        {
            if (SelectedProduct is null)
            {
                MessageBox.Show("اختار المنتج الأول", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var entryDate = _getEntryDate();

            // النطاقات المكتملة بس (مرحلة بداية ونهاية وعدد صحيح موجب)
            var ranges = FlowRanges
                .Where(r => r.FromStage is not null && r.ToStage is not null &&
                            int.TryParse(r.PiecesText?.Trim(), out var p) && p > 0)
                .Select(r => new BatchRangeDto
                {
                    // مصدر القطع: دفعة واقفة، رصيد افتتاحي، ولا إنتاج جديد
                    // (الاتنين فاضيين = بيبدأ من أول الخط)
                    BatchId = r.SelectedBatch?.BatchId,
                    IsOpeningBalance = r.IsOpeningBalance,
                    FromStageId = r.FromStage!.StageId,
                    ToStageId = r.ToStage!.StageId,
                    PieceCount = int.Parse(r.PiecesText!.Trim())
                })
                .ToList();

            // أنصبة العمال من كل بطاقات المراحل
            var shares = FlowStages
                .SelectMany(row => row.AssignedWorkers
                    .Where(s => int.TryParse(s.SharePieces?.Trim(), out var p) && p > 0)
                    .Select(s => new FlowShareDto
                    {
                        ProductionStageId = row.StageId,
                        WorkerId = s.WorkerId,
                        PieceCount = int.Parse(s.SharePieces!.Trim())
                    }))
                .ToList();

            try
            {
                FlowSaveResultDto result;

                // مرحلة 1: محاولة الحفظ من غير تخطي. لو فيه تعارض تكليف
                // الخدمة بترفض **قبل أي كتابة** وبتبعت تفاصيله.
                try
                {
                    result = await RecordFlowAsync(confirmOverride: false);
                }
                catch (AssignmentConfirmationRequiredException ex)
                {
                    // التعارضات اللي المستخدم أكّدها وهو بيضيف العامل مش
                    // بيتسأل عنها تاني — اللي جديد بس (مثلاً حد تاني سجّل
                    // نفس العامل في نفس اللحظة) هو اللي بيتعرض
                    var unconfirmed = ex.Conflicts.Where(c => !AlreadyConfirmed(c)).ToList();

                    if (unconfirmed.Count > 0 && !ConfirmOverride(unconfirmed))
                        return; // إلغاء: مفيش أي سجل اتكتب ومفيش بيانات اتغيّرت

                    // مرحلة 2: نفس الطلب بموافقة صريحة — الخدمة بتعيد التحقق وتحفظ
                    result = await RecordFlowAsync(confirmOverride: true);
                }

                // ملخص واضح لكل اللي حصل: سجلات + يوميات كل عامل + الحضور التلقائي
                var totalsLines = string.Join("\n", result.WorkerTotals.Select(t =>
                    $"  • {t.WorkerName}: {t.TotalPieces} قطعة ≈ {t.TotalWorkdays} يومية"));
                var attendanceLine = result.AttendanceMarkedCount > 0
                    ? $"\n\n✔ اتسجل حضور تلقائي لـ {result.AttendanceMarkedCount} عامل"
                    : "";

                // إيه اللي خلص الخط وإيه اللي هيترحّل — أهم سطر للمستخدم
                // قبل ما يقفل يومه
                var batchLines = string.Join("\n", result.BatchMovements.Select(m => m.IsCompleted
                    ? $"  ✔ {m.Pieces} قطعة خلصت الخط كامل"
                    : $"  ⏳ {m.Pieces} قطعة واقفة عند \"{m.StoppedAtStageName}\""));

                var leftBehind = result.BatchMovements.Where(m => m.WasSplit).ToList();
                var splitLines = leftBehind.Count == 0 ? "" : "\n" + string.Join("\n", leftBehind.Select(m =>
                    $"  ⏳ {m.LeftBehindPieces} قطعة فضلت واقفة مكانها"));

                MessageBox.Show(
                    $"تم حفظ رحلة إنتاج \"{SelectedProduct.Name}\" بتاريخ {entryDate:yyyy/MM/dd}\n" +
                    $"({result.RecordsCount} سجل على {result.StagesCovered} مراحل)\n\n" +
                    $"حالة الإنتاج:\n{batchLines}{splitLines}\n\n" +
                    $"يوميات العمال:\n{totalsLines}{attendanceLine}",
                    "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information);

                // إعادة تحميل الرحلة ("مسجل اليوم" بيتحدث وبتبدأ نظيفة) + إبلاغ الشاشة الأم (تحديث الحضور)
                await ReloadAsync();
                await _onSavedAsync();
            }
            catch (InvalidOperationException ex)
            {
                // رسائل التحقق العربية الواضحة من الخدمة بتوصل للمستخدم زي ما هي
                // (AssignmentConfirmationRequiredException اتمسك فوق، فمبيوصلش هنا)
                MessageBox.Show(ex.Message, "راجع بيانات الرحلة", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // نداء واحد للخدمة بنفس المدخلات — الفرق بين المحاولة والتأكيد
            // هو المعامل ده بس، فمفيش أي احتمال إن الطلبين يختلفوا
            async Task<FlowSaveResultDto> RecordFlowAsync(bool confirmOverride)
            {
                using var scope = _scopeFactory.CreateScope();
                var flowService = scope.ServiceProvider.GetRequiredService<ProductionFlowService>();
                // الخدمة بتتحقق من كل حاجة تاني (مصدر الحقيقة الوحيد للقواعد) — يا كله يا مفيش
                return await flowService.RecordFlowAsync(
                    SelectedProduct!.ProductId, entryDate, ranges, shares, confirmOverride);
            }
        }
    }

    // ======================= نماذج عرض الرحلة =======================

    /// <summary>عامل مؤهل في قائمة اختيار عمال المرحلة</summary>
    public record WorkerPick(int WorkerId, string Name);

    /// <summary>
    /// اختيار في قايمة "القطع دي جاية منين؟". القايمة بتفضل ظاهرة دايمًا
    /// بالتلات اختيارات — من غير كده المستخدم اللي بيسجل من نص الخط بيتقاله
    /// "اختار دفعة" وهو مش شايف أي قايمة يختار منها.
    /// </summary>
    public class BatchSourceChoice
    {
        /// <summary>الدفعة الواقفة (null للإنتاج الجديد والرصيد الافتتاحي)</summary>
        public OpenBatchDto? Batch { get; init; }

        public bool IsNewProduction { get; init; }
        public bool IsOpeningBalance { get; init; }

        public string Text { get; init; } = "";

        public static BatchSourceChoice NewProduction() => new()
        {
            IsNewProduction = true,
            Text = "🆕 إنتاج جديد — من أول الخط"
        };

        public static BatchSourceChoice ForBatch(OpenBatchDto batch) => new()
        {
            Batch = batch,
            Text = "⏳ " + batch.PickerText
        };

        public static BatchSourceChoice OpeningBalance() => new()
        {
            IsOpeningBalance = true,
            Text = "📦 رصيد افتتاحي — القطع عدّت المراحل السابقة قبل كده"
        };
    }

    /// <summary>حالة مرحلة في رحلة الإنتاج — بتحدد لون البطاقة ورسالتها</summary>
    public enum FlowStageState
    {
        /// <summary>مش داخلة في أي نطاق النهارده (مفيش عليها إنتاج)</summary>
        NotToday,

        /// <summary>عليها إنتاج وعمالها مظبوطين — جاهزة للحفظ</summary>
        Ready,

        /// <summary>عليها إنتاج بس مفيش عمال متوزعين</summary>
        NeedsWorkers,

        /// <summary>مجموع توزيع العمال مش مساوي إنتاج المرحلة</summary>
        Mismatch,

        /// <summary>عليها عمال بس مش داخلة في أي نطاق (الحفظ هيرفضها)</summary>
        WorkersWithoutPieces
    }

    /// <summary>
    /// بطاقة مرحلة واحدة في رحلة الإنتاج: بياناتها + عمالها المؤهلين +
    /// العمال المتوزعين عليها بأنصبتهم + إنتاجها المحسوب من النطاقات.
    /// زرار "＋ عامل" أمره على البطاقة نفسها (بيوصّل للرحلة عبر callback).
    /// </summary>
    public partial class FlowStageRow : ObservableObject
    {
        // غير متزامن لأن إضافة عامل بقت بتتحقق من تكليفاته المحفوظة في اليوم
        private readonly Func<FlowStageRow, Task> _onAddWorker;

        public FlowStageRow(Func<FlowStageRow, Task> onAddWorker) => _onAddWorker = onAddWorker;

        public int StageId { get; init; }
        public int DisplayOrder { get; init; }
        public string StageName { get; init; } = "";
        public int Quota { get; init; }
        public List<WorkerPick> QualifiedWorkers { get; init; } = new();

        /// <summary>مفيش عمال مؤهلين للمرحلة دي — لازم تتربط المهارات الأول (قرار: المؤهلين بس)</summary>
        public bool HasNoQualified => QualifiedWorkers.Count == 0;

        public string QuotaText => $"اليومية: {Quota}";

        // ------- بحث في قايمة العمال المؤهلين -------

        /// <summary>
        /// نص البحث في قايمة عمال المرحلة. المرحلة ممكن يكون ليها ٢٠ عامل
        /// مؤهل، والنزول فيهم بالماوس كل مرة بطيء.
        /// </summary>
        [ObservableProperty]
        private string _workerSearch = string.Empty;

        partial void OnWorkerSearchChanged(string value) => ApplyWorkerFilter();

        /// <summary>العمال المعروضين في القايمة دلوقتي (بعد البحث)</summary>
        public ObservableCollection<WorkerPick> VisibleWorkers { get; } = new();

        /// <summary>فيه اقتراحات تتعرض تحت خانة البحث؟</summary>
        public bool HasSuggestions => VisibleWorkers.Count > 0;

        /// <summary>المستخدم كتب حاجة ومفيش ولا عامل مطابق — لازم يعرف بدل ما يستنى</summary>
        public bool HasNoMatch => WorkerSearch.Trim().Length > 0 && VisibleWorkers.Count == 0;

        /// <summary>بيتنادى بعد بناء الصف عشان القايمة تبدأ كاملة</summary>
        public void ApplyWorkerFilter()
        {
            var query = WorkerSearch?.Trim() ?? "";

            var matches = query.Length == 0
                ? QualifiedWorkers
                : QualifiedWorkers.Where(w => ArabicSearch.Contains(w.Name, query));

            // اللي اتضاف للمرحلة خلاص مبيظهرش تاني — إضافته مرة تانية بترجع
            // من غير ما يحصل حاجة، فوجوده في القايمة كان بيوهم إن فيه مشكلة
            VisibleWorkers.Clear();
            foreach (var w in matches.Where(w => AssignedWorkers.All(a => a.WorkerId != w.WorkerId)))
                VisibleWorkers.Add(w);

            OnPropertyChanged(nameof(HasSuggestions));
            OnPropertyChanged(nameof(HasNoMatch));
        }

        /// <summary>
        /// تحريك الاختيار في قايمة الاقتراحات بالسهمين من غير ما المستخدم
        /// يسيب خانة البحث. أول ضغطة (ومفيش اختيار) بتاخد أول/آخر عنصر.
        /// </summary>
        public void MoveSuggestion(int delta)
        {
            if (VisibleWorkers.Count == 0) return;

            var current = SelectedWorkerToAdd is null ? -1 : VisibleWorkers.IndexOf(SelectedWorkerToAdd);

            SelectedWorkerToAdd = current < 0
                ? VisibleWorkers[delta > 0 ? 0 : VisibleWorkers.Count - 1]
                : VisibleWorkers[Math.Clamp(current + delta, 0, VisibleWorkers.Count - 1)];
        }

        /// <summary>
        /// Enter من غير ما المستخدم ينزل بالسهم: لو النتيجة واحدة بس خدها
        /// علطول. أكتر من واحدة من غير اختيار = مفيش إضافة (نتفادى إضافة الغلط).
        /// </summary>
        public bool TryPickSuggestion()
        {
            if (SelectedWorkerToAdd is not null) return true;
            if (VisibleWorkers.Count != 1) return false;

            SelectedWorkerToAdd = VisibleWorkers[0];
            return true;
        }

        /// <summary>تفضية خانة البحث بعد إضافة عامل — الخانة تبقى جاهزة للي بعده</summary>
        public void ResetWorkerPicker()
        {
            SelectedWorkerToAdd = null;
            WorkerSearch = string.Empty;
            // لو النص كان فاضي أصلاً الـ setter مش بيشغّل الفلترة، والقايمة
            // محتاجة تتحدّث برضه عشان اللي اتضاف يختفي منها
            ApplyWorkerFilter();
        }

        // ------- حالة المرحلة (لون وعلامة على البطاقة) -------

        /// <summary>مجموع اللي اتوزع على عمال المرحلة دلوقتي</summary>
        public int AssignedSum =>
            AssignedWorkers.Sum(s => int.TryParse(s.SharePieces?.Trim(), out var p) && p > 0 ? p : 0);

        /// <summary>
        /// حالة المرحلة دلوقتي — دي اللي بتلوّن البطاقة وبتخلي المستخدم
        /// يعرف بنظرة واحدة مين ناقص من غير ما يقرا كل بطاقة.
        /// </summary>
        public FlowStageState State
        {
            get
            {
                if (ComputedPieces == 0)
                    return AssignedWorkers.Count > 0 ? FlowStageState.WorkersWithoutPieces : FlowStageState.NotToday;

                if (AssignedWorkers.Count == 0) return FlowStageState.NeedsWorkers;

                return AssignedSum == ComputedPieces ? FlowStageState.Ready : FlowStageState.Mismatch;
            }
        }

        public bool IsReady => State == FlowStageState.Ready;

        /// <summary>لون الشريط الجانبي للبطاقة حسب الحالة</summary>
        public string StateColor => State switch
        {
            FlowStageState.Ready => "#0B6E4F",              // أخضر: تمام
            FlowStageState.NeedsWorkers => "#B7791F",       // أصفر: عليها إنتاج ومحتاجة عمال
            FlowStageState.Mismatch => "#B00020",           // أحمر: التوزيع مش مساوي الإنتاج
            FlowStageState.WorkersWithoutPieces => "#B7791F",
            _ => "#DDE3ED"                                   // رمادي باهت: مش داخلة النهارده
        };

        /// <summary>الرسالة القصيرة اللي بتظهر على البطاقة</summary>
        public string StateText => State switch
        {
            FlowStageState.Ready => "جاهزة",
            FlowStageState.NeedsWorkers => "محتاجة عمال",
            FlowStageState.Mismatch => $"التوزيع {AssignedSum} ≠ {ComputedPieces}",
            FlowStageState.WorkersWithoutPieces => "عليها عمال من غير إنتاج",
            _ => ""
        };

        public bool HasState => State != FlowStageState.NotToday;

        /// <summary>بيتنادى من الرحلة بعد أي إعادة حساب عشان البطاقة تتلوّن من جديد</summary>
        public void RefreshState()
        {
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(IsReady));
            OnPropertyChanged(nameof(StateColor));
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(HasState));
            OnPropertyChanged(nameof(AssignedSum));
        }

        /// <summary>تنبيه لو فيه إنتاج متسجل بالفعل على المرحلة في نفس اليوم</summary>
        [ObservableProperty]
        private string _alreadyText = "";

        [ObservableProperty]
        private WorkerPick? _selectedWorkerToAdd;

        /// <summary>إنتاج المرحلة المحسوب من النطاقات (صفر = مش داخلة الرحلة النهارده)</summary>
        [ObservableProperty]
        private int _computedPieces;

        public ObservableCollection<FlowShareEntry> AssignedWorkers { get; } = new();

        // الاسم المولّد للأمر بيفضل AddWorkerCommand (الـ Toolkit بيشيل لاحقة Async) — الـ XAML ما اتغيرش
        [RelayCommand]
        private Task AddWorkerAsync() => _onAddWorker(this);
    }

    /// <summary>
    /// نصيب عامل واحد من قطع مرحلة (الخانة بتتملى تلقائي بالتساوي وتتعدل
    /// يدوي). زرار ✕ أمره على الشريحة نفسها (بيوصّل للرحلة عبر callback).
    /// </summary>
    public partial class FlowShareEntry : ObservableObject
    {
        private readonly Action _onEdited;
        private readonly Action<FlowShareEntry> _onRemove;

        public FlowShareEntry(FlowStageRow parent, int workerId, string workerName,
            Action onEdited, Action<FlowShareEntry> onRemove)
        {
            Parent = parent;
            WorkerId = workerId;
            WorkerName = workerName;
            _onEdited = onEdited;
            _onRemove = onRemove;
        }

        /// <summary>البطاقة الأم — عشان أمر الإزالة يعرف يشيل النصيب من مرحلته</summary>
        public FlowStageRow Parent { get; }
        public int WorkerId { get; }
        public string WorkerName { get; }

        [ObservableProperty]
        private string _sharePieces = "";

        partial void OnSharePiecesChanged(string value) => _onEdited();

        [RelayCommand]
        private void Remove() => _onRemove(this);
    }

    /// <summary>
    /// نطاق إنتاج واحد: من مرحلة إلى مرحلة بعدد قطع.
    /// زرار الحذف أمره على السطر نفسه (بيوصّل للرحلة عبر callback).
    /// </summary>
    public partial class FlowRangeRow : ObservableObject
    {
        private readonly Action _onEdited;
        private readonly Action<FlowRangeRow> _onRemove;

        public FlowRangeRow(
            List<StageEntryOption> stageOptions,
            IReadOnlyList<OpenBatchDto> openBatches,
            Action onEdited,
            Action<FlowRangeRow> onRemove)
        {
            StageOptions = stageOptions;
            OpenBatches = openBatches;
            _onEdited = onEdited;
            _onRemove = onRemove;

            RebuildSourceChoices();
        }

        /// <summary>مراحل المنتج بالترتيب — نفس القائمة لقايمتي "من" و"إلى"</summary>
        public List<StageEntryOption> StageOptions { get; }

        /// <summary>القطع الواقفة من أيام فاتت على المنتج ده</summary>
        public IReadOnlyList<OpenBatchDto> OpenBatches { get; }

        [ObservableProperty]
        private StageEntryOption? _fromStage;

        [ObservableProperty]
        private StageEntryOption? _toStage;

        [ObservableProperty]
        private string _piecesText = "";

        /// <summary>
        /// مصدر القطع: إنتاج جديد، دفعة واقفة، ولا رصيد افتتاحي.
        /// القايمة دي بتظهر **دايمًا** — من غيرها المستخدم اللي بيسجل نطاق
        /// من نص الخط بيتقاله "اختار دفعة" وهو مش شايف ولا قايمة.
        /// </summary>
        public ObservableCollection<BatchSourceChoice> SourceChoices { get; } = new();

        [ObservableProperty]
        private BatchSourceChoice? _selectedSource;

        /// <summary>الدفعة الواقفة المختارة (null = إنتاج جديد أو رصيد افتتاحي)</summary>
        public OpenBatchDto? SelectedBatch => SelectedSource?.Batch;

        /// <summary>المستخدم قال إن القطع دي عدّت المراحل السابقة برّه النظام</summary>
        public bool IsOpeningBalance => SelectedSource?.IsOpeningBalance ?? false;

        partial void OnFromStageChanged(StageEntryOption? value) => _onEdited();
        partial void OnToStageChanged(StageEntryOption? value) => _onEdited();
        partial void OnPiecesTextChanged(string value) => _onEdited();

        partial void OnSelectedSourceChanged(BatchSourceChoice? value)
        {
            if (value?.Batch is { } batch)
            {
                // البداية مفروضة: الدفعة واقفة عند مرحلة واحدة بالظبط
                FromStage = StageOptions.FirstOrDefault(s => s.StageId == batch.NextStageId);
                if (PiecesText.Trim().Length == 0) PiecesText = batch.Quantity.ToString();
            }
            else if (value is { IsNewProduction: true })
            {
                // إنتاج جديد بيبدأ من أول الخط دايمًا
                FromStage = StageOptions.FirstOrDefault();
            }

            OnPropertyChanged(nameof(SelectedBatch));
            OnPropertyChanged(nameof(IsOpeningBalance));
            OnPropertyChanged(nameof(IsContinuingBatch));
            OnPropertyChanged(nameof(IsStartStageEditable));
            OnPropertyChanged(nameof(BatchHintText));
            OnPropertyChanged(nameof(IsOpeningBalanceWarningVisible));
            _onEdited();
        }

        /// <summary>بيكمّل دفعة واقفة (مش إنتاج جديد) — بيقفل قايمة "من"</summary>
        public bool IsContinuingBatch => SelectedBatch is not null;

        /// <summary>
        /// مرحلة البداية بتتقفل وقت تكميل دفعة: الدفعة واقفة عند مرحلة واحدة
        /// بالظبط، وأي بداية تانية الخدمة هترفضها. الرصيد الافتتاحي بالعكس —
        /// المستخدم هو اللي بيحدد دخلت الخط عند فين.
        /// </summary>
        public bool IsStartStageEditable => SelectedBatch is null;

        /// <summary>الرصيد الافتتاحي بيفتح ثغرة مقصودة — لازم يبان إنه اختيار مش الوضع الطبيعي</summary>
        public bool IsOpeningBalanceWarningVisible => IsOpeningBalance;

        public string BatchHintText => SelectedSource switch
        {
            { Batch: { } b } =>
                $"بتكمّل {b.Quantity} قطعة واقفة من {b.DaysWaiting} يوم عند \"{b.NextStageName}\" — " +
                "تقدر تكتب عدد أقل وتسيب الباقي واقف",
            { IsOpeningBalance: true } =>
                "هتتفتح دفعة جديدة عند المرحلة اللي هتختارها في \"من\" — " +
                "استخدمها للشغل اللي كان واقف في الخط قبل ما النظام يمسك الدفعات",
            _ => "إنتاج جديد — بيبدأ من أول مرحلة في الخط"
        };

        /// <summary>بيبني اختيارات المصدر: إنتاج جديد + كل دفعة واقفة + رصيد افتتاحي</summary>
        public void RebuildSourceChoices()
        {
            var previous = SelectedSource;

            SourceChoices.Clear();
            SourceChoices.Add(BatchSourceChoice.NewProduction());
            foreach (var batch in OpenBatches) SourceChoices.Add(BatchSourceChoice.ForBatch(batch));
            SourceChoices.Add(BatchSourceChoice.OpeningBalance());

            SelectedSource = previous?.Batch is { } old
                ? SourceChoices.FirstOrDefault(c => c.Batch?.BatchId == old.BatchId) ?? SourceChoices[0]
                : SourceChoices[0];
        }

        [RelayCommand]
        private void Remove() => _onRemove(this);
    }
}
