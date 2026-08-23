using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Helpers;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Models;
using WorkforceManager.UI.Views;
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

        /// <summary>
        /// تكليفات اليوم المحفوظة في الداتابيز، متخزّنة في الذاكرة.
        ///
        /// ترتيب قايمة الاقتراحات بيتنادى مع كل حرف المستخدم بيكتبه،
        /// فمينفعش يستنى استعلام. بتتحدّث مع إعادة تحميل الرحلة وبعد
        /// الحفظ — واللي لسه على الشاشة بيتقرا لحظيًا من الجلسات المفتوحة.
        /// </summary>
        private List<WorkerAssignmentDto> _savedDayAssignments = new();

        /// <summary>
        /// اتضاف عامل على المرحلة دي. الشاشة بتزحلق لها عشان اللي بعدها
        /// يبقى باين — المستخدم كان بينزل بالماوس بعد كل عامل.
        /// </summary>
        public event Action<FlowStageRow>? WorkerAdded;

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

        /// <summary>معاينة يوميات كل عامل قبل الحفظ (بتتحدث لحظيًا)</summary>
        public ObservableCollection<FlowWorkerTotalDto> FlowPreview { get; } = new();

        // بلوك التحذيرات الأصفر اللي كان هنا اتشال: كان بيجمّع أخطاء كل
        // النطاقات في نص واحد فوق الشاشة، فالمستخدم بيقرا "مرحلة كذا
        // واقعة في أكتر من نطاق" ويفضل يدوّر هي في أنهي نطاق. دلوقتي كل
        // نطاق بيعرض خطأه تحته (FlowRangeRow.Error)، وتحذير "التوزيع ≠
        // الإنتاج" اتشال خالص لأن كارت المرحلة بيتلوّن أحمر في نفس الحالة.

        /// <summary>هل المستخدم كتب أي حاجة في الرحلة دي؟ (للتأكيد قبل إزالتها)</summary>
        public bool HasUserInput =>
            FlowStages.Any(s => s.AssignedWorkers.Count > 0) ||
            FlowRanges.Any(r => !string.IsNullOrWhiteSpace(r.PiecesText));

        /// <summary>مراحل النطاقات: كل مراحل المنتج ما عدا مرحلة الرص (مالهاش قطع خالص)</summary>
        private static List<StageEntryOption> RangeableStages(ProductOption product) =>
            product.Stages.Where(s => !s.IsRackingStage).ToList();

        /// <summary>يبني بطاقات المراحل وعمالها المؤهلين للمنتج المختار (وبيتنادى برضه عند تغيير اليوم)</summary>
        public async Task ReloadAsync()
        {
            _suppressCallbacks = true;
            try
            {
                FlowStages.Clear();
                FlowRanges.Clear();
                FlowPreview.Clear();
                // الموافقات بتخص التكليفات اللي كانت على الشاشة — الرحلة بتبدأ نظيفة
                _confirmedAssignments.Clear();

                var product = SelectedProduct;
                if (product is null || product.Stages.Count == 0) return;

                using var scope = _scopeFactory.CreateScope();
                var workerRepo = scope.ServiceProvider.GetRequiredService<IWorkerRepository>();
                var productionRepo = scope.ServiceProvider.GetRequiredService<IDailyProductionRepository>();

                // المؤهلين لكل مراحل المنتج باستعلام واحد
                var skillsByStage = (await workerRepo.GetSkillsForProductAsync(product.ProductId))
                    .ToLookup(ws => ws.ProductionStageId);

                // متدرّبين (HourlyRole.Training): بيتحطوا تاج على أي مرحلة
                // عادية زي أي عامل عادي — بادج "تحت التدريب" جنب اسمهم في
                // نفس قايمة "+ عامل" الموحّدة، مش عنصر واجهة منفصل.
                var allHourly = await workerRepo.GetActiveWithSkillsAsync();
                var trainees = allHourly
                    .Where(w => w.HourlyRole == HourlyRole.Training)
                    .OrderBy(w => w.SortOrder)
                    .Select(w => new WorkerPick(w.Id, w.FullName, IsTagOnly: true, TagLabel: "تحت التدريب"))
                    .ToList();

                // عمال الرص (HourlyRole.Racking): قايمة "+ عامل" بتاعة
                // مرحلة الرص نفسها بس — مرحلة زي أي مرحلة عادي، بس عمالها
                // من الدور ده مش من WorkerSkill
                var rackingWorkers = allHourly
                    .Where(w => w.HourlyRole == HourlyRole.Racking)
                    .OrderBy(w => w.SortOrder)
                    .Select(w => new WorkerPick(w.Id, w.FullName, IsTagOnly: true))
                    .ToList();

                // الإنتاج المسجل بالفعل في اليوم ده على مراحل المنتج (تحذير من الإدخال المزدوج)
                // تكليفات اليوم المحفوظة — قايمة الاقتراحات بترتّب بيها،
                // فلازم تكون جاهزة قبل أول فلترة
                _savedDayAssignments = (await scope.ServiceProvider
                    .GetRequiredService<WorkerAssignmentGuard>()
                    .GetDayAssignmentsAsync(_getEntryDate())).ToList();

                var stageIds = product.Stages.Select(s => s.StageId).ToHashSet();
                var alreadyByStage = (await productionRepo.GetByDateAsync(_getEntryDate()))
                    .Where(r => stageIds.Contains(r.ProductionStageId))
                    .GroupBy(r => r.ProductionStageId)
                    .ToDictionary(g => g.Key, g => g.Sum(r => r.PieceCount));

                foreach (var stage in product.Stages)
                {
                    alreadyByStage.TryGetValue(stage.StageId, out var already);

                    var row = new FlowStageRow(AddWorkerToStageAsync, DescribeAssignedElsewhere)
                    {
                        StageId = stage.StageId,
                        DisplayOrder = stage.DisplayOrder,
                        StageName = stage.StageName,
                        Quota = stage.PiecesPerWorkday,
                        IsRackingStage = stage.IsRackingStage,
                        // مرحلة الرص: عمالها من HourlyRole.Racking بس، مش
                        // من WorkerSkill (مالهاش أصلًا). أي مرحلة عادية:
                        // المؤهلين + المتدرّبين (مرتبين بالتقييم للمؤهلين
                        // أولًا — نفس قاعدة SkillRatingService.Rank اللي
                        // شاشة المنتجات شغالة بيها).
                        QualifiedWorkers = stage.IsRackingStage
                            ? rackingWorkers
                            : SkillRatingService.Rank(skillsByStage[stage.StageId])
                                .Select(ws => new WorkerPick(ws.WorkerId, ws.Worker.FullName, ws.Stars))
                                .Concat(trainees)
                                .ToList(),
                        AlreadyText = already > 0 ? $"مسجل اليوم: {already}" : ""
                    };

                    FlowStages.Add(row);
                }

                // عامل الرص الثابت بتاع المنتج بيتحط تاجه تلقائيًا كل يوم
                // على مرحلة الرص — تصحيح بديل (سهر أو غياب) بيتم بمسح
                // التاج ده أو تغييره بإيد المستخدم لليوم ده بس.
                PrefillRackingDefault(product);

                foreach (var row in FlowStages)
                    row.ApplyWorkerFilter(); // القايمة تبدأ كاملة قبل أي بحث (وبعد تعبية تاج الرص)

                // نطاق افتراضي جاهز: من أول مرحلة لآخر مرحلة — لو اليوم كله
                // بنفس العدد يبقى المستخدم يكتب رقم واحد بس ويحفظ.
                // مرحلة الرص مستبعدة: مالهاش نطاق قطع خالص.
                var rangeableStages = RangeableStages(product);
                if (rangeableStages.Count > 0)
                    FlowRanges.Add(new FlowRangeRow(rangeableStages, OnStructureEdited, RemoveRange)
                    {
                        FromStage = rangeableStages.First(),
                        ToStage = rangeableStages.Last()
                    });
            }
            finally
            {
                _suppressCallbacks = false;
            }

            await LoadPendingWorkAsync();
        }

        /// <summary>
        /// عامل الرص الثابت بتاع المنتج (Product.RackingWorkerId) بيتحط
        /// تاجه تلقائيًا على مرحلة الرص كل مرة الرحلة بتفتح — كأنه
        /// الافتراضي اليومي. المستخدم يقدر يشيله أو يستبدله بعامل رص
        /// تاني لليوم ده بس (زي أي تاج تاني)، من غير ما ده يغيّر
        /// الافتراضي الثابت بتاع المنتج نفسه.
        /// </summary>
        private void PrefillRackingDefault(ProductOption product)
        {
            if (product.RackingWorkerId is not { } rackingWorkerId) return;

            var rackingRow = FlowStages.FirstOrDefault(s => s.IsRackingStage);
            if (rackingRow is null) return;
            if (rackingRow.AssignedWorkers.Any(s => s.WorkerId == rackingWorkerId)) return;

            var pick = rackingRow.QualifiedWorkers.FirstOrDefault(w => w.WorkerId == rackingWorkerId);
            if (pick is null) return; // العامل مبقاش عامل رص نشط (اتوقف أو اتغيّر دوره)

            rackingRow.AssignedWorkers.Add(new FlowShareEntry(
                rackingRow, pick.WorkerId, pick.Name, OnSharesEdited, RemoveWorkerShare,
                isTagOnly: true, tagLabel: pick.TagLabel));
        }

        // ======================= الشغل الواقف =======================

        /// <summary>
        /// المراحل اللي قدامها شغل واقف من أيام فاتت — كل واحدة معاها
        /// زرار "كمّل من هنا".
        /// </summary>
        public ObservableCollection<PendingStageDto> PendingStages { get; } = new();

        /// <summary>غلط إدخال: مرحلة اتسجل عليها أكتر من اللي قبلها</summary>
        public ObservableCollection<PendingStageDto> PendingErrors { get; } = new();

        [ObservableProperty]
        private bool _hasPendingWork;

        [ObservableProperty]
        private bool _hasPendingErrors;

        [ObservableProperty]
        private string _pendingSummaryText = "";

        /// <summary>آخر مرحلة في الخط — التكميل بيوصل لعندها</summary>
        private int _lastStageId;

        /// <summary>
        /// بيقرا الشغل الواقف على المنتج المختار لحد اليوم المعروض.
        ///
        /// الحساب كله في PendingWorkService — الشاشة بتعرض بس.
        /// </summary>
        private async Task LoadPendingWorkAsync()
        {
            PendingStages.Clear();
            PendingErrors.Clear();
            HasPendingWork = false;
            HasPendingErrors = false;
            PendingSummaryText = "";

            if (SelectedProduct is not { } product) return;

            using var scope = _scopeFactory.CreateScope();
            var pending = await scope.ServiceProvider.GetRequiredService<PendingWorkService>()
                .GetForProductAsync(product.ProductId, _getEntryDate());

            if (pending is null) return;

            _lastStageId = pending.LastStageId;

            foreach (var stage in pending.Resumable) PendingStages.Add(stage);
            foreach (var error in pending.Stages.Where(s => s.IsDataError)) PendingErrors.Add(error);

            HasPendingWork = PendingStages.Count > 0;
            HasPendingErrors = PendingErrors.Count > 0;

            PendingSummaryText = HasPendingWork
                ? $"{pending.TotalPending:N0} قطعة دخلت الخط ولسه ماخرجتش منه"
                : "";
        }

        /// <summary>
        /// بيجهّز نطاق يكمّل الشغل الواقف قدام المرحلة دي لآخر الخط.
        ///
        /// **بيملا النطاق بس** — العمال بيتحطوا بإيد المستخدم. الأعداد
        /// جاهزة لأنها معروفة (الواقف بالظبط)، لكن مين هيشتغل عليها
        /// قرار بتاعه هو.
        /// </summary>
        [RelayCommand]
        private void ResumePending(PendingStageDto? stage)
        {
            if (stage is null || SelectedProduct is not { } product) return;

            var from = product.Stages.FirstOrDefault(s => s.StageId == stage.StageId);
            var to = product.Stages.FirstOrDefault(s => s.StageId == _lastStageId);
            if (from is null || to is null) return;

            _suppressCallbacks = true;
            try
            {
                // النطاق الافتراضي الفاضي بيتشال: سيبه معناه نطاقين
                // متداخلين والحفظ هيترفض
                foreach (var empty in FlowRanges.Where(r => r.PiecesText.Trim().Length == 0).ToList())
                    FlowRanges.Remove(empty);

                FlowRanges.Add(new FlowRangeRow(RangeableStages(product), OnStructureEdited, RemoveRange)
                {
                    FromStage = from,
                    ToStage = to,
                    PiecesText = stage.PendingPieces.ToString()
                });
            }
            finally
            {
                _suppressCallbacks = false;
            }

            RecomputeFlow();
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
            RecomputeTotals();
        }

        /// <summary>
        /// إعادة الحساب الكاملة: بيحسب إنتاج كل مرحلة من النطاقات، وبيوزّع
        /// قطع كل مرحلة بالتساوي على عمالها (الباقي بيتوزع واحدة واحدة على
        /// الأوائل)، وبعدها بيحدّث المعاينة. أي مشكلة بتظهر كتحذير لحظي.
        /// </summary>
        private void RecomputeFlow()
        {
            _suppressCallbacks = true;
            try
            {
                // 1) إنتاج كل مرحلة من النطاقات (بنفس قواعد الخدمة: بلا تداخل وبترتيب صحيح)
                foreach (var row in FlowStages) row.ComputedPieces = 0;
                foreach (var range in FlowRanges) range.Error = "";

                var indexByStageId = FlowStages
                    .Select((row, index) => (row.StageId, index))
                    .ToDictionary(x => x.StageId, x => x.index);

                // أنهي نطاق حجز أنهي مرحلة — عشان رسالة التعارض تقول
                // "بيتعارض مع النطاق رقم كذا" مش "فيه تداخل" وبس
                var claimedBy = new Dictionary<int, int>();

                for (var rangeNumber = 0; rangeNumber < FlowRanges.Count; rangeNumber++)
                {
                    var range = FlowRanges[rangeNumber];

                    if (range.FromStage is null || range.ToStage is null) continue;
                    if (string.IsNullOrWhiteSpace(range.PiecesText)) continue;

                    if (!int.TryParse(range.PiecesText.Trim(), out var pieces) || pieces <= 0)
                    {
                        range.Error = $"\"{range.PiecesText}\" مش رقم قطع صحيح — لازم رقم موجب";
                        continue;
                    }

                    var fromIndex = indexByStageId[range.FromStage.StageId];
                    var toIndex = indexByStageId[range.ToStage.StageId];
                    if (fromIndex > toIndex)
                    {
                        range.Error =
                            $"النطاق معكوس: \"{range.FromStage.StageName}\" بتيجي بعد " +
                            $"\"{range.ToStage.StageName}\" في خط الإنتاج";
                        continue;
                    }

                    // منع تكرار المرحلة بين النطاقات: مرحلة اتسجلت في نطاق
                    // مش هينفع تتسجل تاني في نطاق بعده — ده تسجيل مزدوج
                    // بيضاعف يوميات العمال. الرسالة بتسمّي النطاقين والمرحلة.
                    var conflict = FirstConflictingStage(fromIndex, toIndex, claimedBy);
                    if (conflict is { } clash)
                    {
                        range.Error =
                            $"مرحلة \"{FlowStages[clash.StageIndex].StageName}\" متسجلة خلاص في " +
                            $"النطاق رقم {clash.OwnerRangeNumber + 1} — المرحلة ميصحش تتحسب مرتين";
                        continue;
                    }

                    for (var i = fromIndex; i <= toIndex; i++)
                    {
                        FlowStages[i].ComputedPieces = pieces;
                        claimedBy[i] = rangeNumber;
                    }
                }

                // 2) توزيع متساوٍ تلقائي على عمال كل مرحلة (قابل للتعديل اليدوي بعدها)
                foreach (var row in FlowStages)
                {
                    // التاجات (متدرّب/عامل رص) مالهاش نصيب قطع خالص —
                    // مستبعدة من التوزيع المتساوي
                    var workers = row.AssignedWorkers.Where(w => !w.IsTagOnly).ToList();
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

            RecomputeTotals();
        }

        /// <summary>
        /// أول مرحلة في النطاق ده محجوزة لنطاق قبله — مع رقم النطاق اللي
        /// حاجزها. بترجّع null لو النطاق سليم.
        ///
        /// دالة منفصلة عن قصد: الشرط ده هو قاعدة "المرحلة ميصحش تتحسب
        /// مرتين"، وخلطه جوّه حلقة الحساب كان بيخليه صعب يتقري ويتغيّر.
        /// </summary>
        private static (int StageIndex, int OwnerRangeNumber)? FirstConflictingStage(
            int fromIndex, int toIndex, IReadOnlyDictionary<int, int> claimedBy)
        {
            for (var i = fromIndex; i <= toIndex; i++)
                if (claimedBy.TryGetValue(i, out var owner))
                    return (i, owner);

            return null;
        }

        /// <summary>
        /// يبني معاينة إجمالي كل عامل (قطع + يوميات).
        ///
        /// مبقاش بيجمّع تحذيرات: تحذير "التوزيع ≠ إنتاج المرحلة" اتشال
        /// خالص — قطعة العامل (عدد ضرباته) والإنتاج الفعلي رقمين منفصلين
        /// عن قصد، فاختلافهم طبيعي مش حاجة تستحق تحذير. أخطاء النطاقات
        /// بقت على كل نطاق في مكانه.
        /// </summary>
        private void RecomputeTotals()
        {
            var totals = new Dictionary<int, (string Name, int Pieces, decimal Workdays)>();

            foreach (var row in FlowStages)
            {
                if (row.ComputedPieces == 0 && row.AssignedWorkers.Count == 0) continue;

                foreach (var share in row.AssignedWorkers)
                {
                    if (share.IsTagOnly) continue; // مالوش نصيب قطع خالص
                    if (!int.TryParse(share.SharePieces?.Trim(), out var pieces) || pieces <= 0) continue;

                    var workdays = Math.Round((decimal)pieces / row.Quota, 2);
                    totals[share.WorkerId] = totals.TryGetValue(share.WorkerId, out var t)
                        ? (t.Name, t.Pieces + pieces, t.Workdays + workdays)
                        : (share.WorkerName, pieces, workdays);
                }
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
        /// بيجيب توزيع عمال يوم فات وبيحطه زي ما هو.
        ///
        /// المستخدم **بيختار اليوم** من قايمة الأيام اللي فيها شغل فعلي
        /// على المنتج ده — قبل كده كان بياخد آخر يوم تلقائيًا، وده مكانش
        /// بينفع لما يكون آخر يوم استثنائي (نصف عمالة، أو منتج تاني).
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
                Notify.Info("اختار المنتج الأول", "تنبيه");
                return;
            }

            IReadOnlyList<FlowDayOptionDto> days;
            using (var scope = _scopeFactory.CreateScope())
            {
                days = await scope.ServiceProvider.GetRequiredService<ProductionFlowService>()
                    .GetRepeatableDaysAsync(product.ProductId, _getEntryDate());
            }

            // القايمة الفاضية بتتعرض جوه النافذة نفسها — رسالة منفصلة
            // قبلها كانت هتبقى نافذتين ورا بعض على نفس المعلومة
            var pickedDate = RepeatDayDialog.Pick(
                Application.Current.MainWindow, product.Name, days);

            if (pickedDate is null) return;

            LastFlowDto? last;
            using (var scope = _scopeFactory.CreateScope())
            {
                last = await scope.ServiceProvider.GetRequiredService<ProductionFlowService>()
                    .GetFlowOnAsync(product.ProductId, pickedDate.Value);
            }

            if (last is null || last.Assignments.Count == 0)
            {
                Notify.Info($"يوم {pickedDate:yyyy/MM/dd} مبقاش فيه توزيع على \"{product.Name}\" — يمكن اتشال دلوقتي.", "مفيش حاجة تتكرر");
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

            if (!Notify.AskDangerous(confirmMessage, "تكرار يوم سابق"))
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
                }

                // مرحلة الرص مفيش لها DailyProduction خالص، فمكانتش داخلة
                // في last.Assignments — والمسح فوق شالها هي كمان. رجّع
                // تاج عامل الرص الافتراضي بتاع المنتج تاني.
                PrefillRackingDefault(product);
            }
            finally
            {
                _suppressCallbacks = false;
            }

            // مرة واحدة بعد ما التوزيع كله يتحط: اللي اتحمّل يختفي من
            // اقتراحات مرحلته وينزل تحت في المراحل التانية
            RefreshAllWorkerPickers();
            RecomputeFlow();
        }

        [RelayCommand]
        private void AddRange()
        {
            if (SelectedProduct is not { } product) return;
            FlowRanges.Add(new FlowRangeRow(RangeableStages(product), OnStructureEdited, RemoveRange));
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

            // تاج بسيط (متدرّب على مرحلة عادية، أو عامل رص على مرحلة
            // الرص): مفيش تحقق تأهيل ومفيش قطع ومفيش تعارض تكليف —
            // العامل مش بيشتغل بالقطعة أصلًا، فمفهوم "مكلّف على مرحلة
            // تانية" ملوش معنى هنا زي عمال الإنتاج بالقطعة.
            if (pick.IsTagOnly)
            {
                stage.AssignedWorkers.Add(new FlowShareEntry(
                    stage, pick.WorkerId, pick.Name, OnSharesEdited, RemoveWorkerShare,
                    isTagOnly: true, tagLabel: pick.TagLabel));
                stage.ResetWorkerPicker();
                RefreshAllWorkerPickers();
                RecomputeFlow();
                WorkerAdded?.Invoke(stage);
                return;
            }

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
                Notify.Info($"العامل \"{pick.Name}\" مسجل بالفعل على مرحلة \"{stage.StageName}\" النهارده.\n" +
                    "لو عايز تعدّل عدد قطعه، استخدم تبويب \"سجلات اليوم\".", "مسجل بالفعل");
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
            RefreshAllWorkerPickers();
            RecomputeFlow(); // إعادة التوزيع المتساوي بعد إضافة عامل

            // الشاشة تزحلق للمرحلة دي عشان اللي بعدها يبقى باين
            WorkerAdded?.Invoke(stage);
        }

        /// <summary>
        /// بعد أي إضافة أو إزالة: كل قوايم الاقتراحات في كل الجلسات
        /// بتتحدّث.
        ///
        /// من غير كده، العامل اللي اتكلّف دلوقتي كان هيفضل أول قايمة
        /// المرحلة اللي بعدها لحد ما المستخدم يكتب فيها — يعني بالظبط
        /// اللحظة اللي الترتيب المفروض يساعده فيها.
        /// كله في الذاكرة، مفيش أي استعلام.
        /// </summary>
        private void RefreshAllWorkerPickers()
        {
            foreach (var session in _getOpenSessions())
                foreach (var row in session.FlowStages)
                    row.ApplyWorkerFilter();
        }

        /// <summary>بيتنادى من زرار ✕ اللي على شريحة العامل نفسها</summary>
        private void RemoveWorkerShare(FlowShareEntry share)
        {
            share.Parent.AssignedWorkers.Remove(share);
            RefreshAllWorkerPickers(); // رجّعه لقايمة الاقتراحات تاني، وفي كل المراحل
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

            _savedDayAssignments = (await guard.GetDayAssignmentsAsync(_getEntryDate())).ToList();
            var onScreen = _getOpenSessions().SelectMany(s => s.CurrentAssignments());

            return _savedDayAssignments.Concat(onScreen).ToList();
        }

        /// <summary>
        /// العامل ده مكلّف النهارده فين غير المرحلة دي؟ بيرجّع
        /// "المنتج / المرحلة" أو null.
        ///
        /// متزامنة عن قصد: بتتنادى في فلترة القايمة مع كل حرف. المحفوظ
        /// بيتقرا من الكاش، واللي لسه على الشاشة من الجلسات المفتوحة —
        /// نفس المصدرين اللي القاعدة بتتقاس عليهم في
        /// <see cref="LoadKnownAssignmentsAsync"/>.
        /// </summary>
        private string? DescribeAssignedElsewhere(int workerId, int exceptStageId)
        {
            var match = _savedDayAssignments
                .Concat(_getOpenSessions().SelectMany(s => s.CurrentAssignments()))
                .FirstOrDefault(a => a.WorkerId == workerId && a.ProductionStageId != exceptStageId);

            return match is null ? null : $"{match.ProductName} / {match.StageName}";
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

            return Notify.AskDangerous(
                question + "\n\n(عامل واحد المفروض ميشتغلش على أكتر من مرحلة في نفس الوقت)",
                "تأكيد تكليف إضافي");
        }

        [RelayCommand]
        private async Task SaveFlowAsync()
        {
            if (SelectedProduct is null)
            {
                Notify.Info("اختار المنتج الأول", "تنبيه");
                return;
            }

            var entryDate = _getEntryDate();

            // النطاقات المكتملة بس (مرحلة بداية ونهاية وعدد صحيح موجب)
            var ranges = FlowRanges
                .Where(r => r.FromStage is not null && r.ToStage is not null &&
                            int.TryParse(r.PiecesText?.Trim(), out var p) && p > 0)
                .Select(r => new FlowRangeDto
                {
                    FromStageId = r.FromStage!.StageId,
                    ToStageId = r.ToStage!.StageId,
                    PieceCount = int.Parse(r.PiecesText!.Trim())
                })
                .ToList();

            // أنصبة العمال من كل بطاقات المراحل — التاجات (متدرّب/عامل رص) مستبعدة هنا عمدًا
            var shares = FlowStages
                .SelectMany(row => row.AssignedWorkers
                    .Where(s => !s.IsTagOnly && int.TryParse(s.SharePieces?.Trim(), out var p) && p > 0)
                    .Select(s => new FlowShareDto
                    {
                        ProductionStageId = row.StageId,
                        WorkerId = s.WorkerId,
                        PieceCount = int.Parse(s.SharePieces!.Trim())
                    }))
                .ToList();

            // متدرّبين على مراحل عادية + عامل الرص على مرحلة الرص — بلا
            // قطع وبلا شرط تأهيل، ومنفصلين عن shares تمامًا (شوف FlowTaggedWorkerDto)
            var taggedWorkers = FlowStages
                .SelectMany(row => row.AssignedWorkers
                    .Where(s => s.IsTagOnly)
                    .Select(t => new FlowTaggedWorkerDto
                    {
                        ProductionStageId = row.StageId,
                        WorkerId = t.WorkerId
                    }))
                .ToList();

            // الإنتاج هو اللي اليوميات بتتحسب منه، واليوميات هي الأجر —
            // فالتسجيل عملية بتلمس فلوس. كلمة سر واحدة للرحلة كلها مش
            // لكل عامل، زي حفظ الحضور بالظبط.
            var totalPieces = shares.Sum(s => s.PieceCount);
            var workerCount = shares.Select(s => s.WorkerId).Distinct().Count();

            var gate = SensitiveActionDialog.Ask(
                Application.Current.MainWindow,
                "حفظ رحلة الإنتاج",
                $"رحلة \"{SelectedProduct.Name}\" بتاريخ {entryDate:yyyy/MM/dd}: " +
                $"{totalPieces:N0} قطعة على {workerCount} عامل.",
                SensitiveActionKind.Save,
                passwordRequired: true,
                reasonRequired: false);

            if (gate is null) return;

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

                // إيه اللي دخل الخط وإيه اللي خلصه — أهم سطر للمستخدم
                var outputLines = string.Join("\n", new[]
                {
                    result.StartedPieces > 0 ? $"  ▶ {result.StartedPieces:N0} قطعة دخلت أول مرحلة" : "",
                    result.CompletedPieces > 0 ? $"  ✔ {result.CompletedPieces:N0} قطعة خلصت آخر مرحلة" : ""
                }.Where(line => line.Length > 0));

                Notify.Info($"تم حفظ رحلة إنتاج \"{SelectedProduct.Name}\" بتاريخ {entryDate:yyyy/MM/dd}\n" +
                    $"({result.RecordsCount} سجل على {result.StagesCovered} مراحل)\n\n" +
                    (outputLines.Length > 0 ? $"حالة الإنتاج:\n{outputLines}\n\n" : "") +
                    $"يوميات العمال:\n{totalsLines}{attendanceLine}", "تم الحفظ");

                // السؤال التلقائي عن الهالك بعد كل حفظ اتشال — كان بيظهر
                // كل يوم فيه شغل واقف حتى لو المستخدم مش قاصد يسجّل هالك
                // النهارده. تسجيل الهالك بقى يدوي بس من تبويب "الهالك"،
                // وقت ما المستخدم يحب (آخر الأسبوع مثلاً).

                // إعادة تحميل الرحلة ("مسجل اليوم" بيتحدث وبتبدأ نظيفة) + إبلاغ الشاشة الأم (تحديث الحضور)
                await ReloadAsync();
                await _onSavedAsync();
            }
            catch (InvalidOperationException ex)
            {
                // رسائل التحقق العربية الواضحة من الخدمة بتوصل للمستخدم زي ما هي
                // (AssignmentConfirmationRequiredException اتمسك فوق، فمبيوصلش هنا)
                Notify.Warn(ex.Message, "راجع بيانات الرحلة");
            }

            // نداء واحد للخدمة بنفس المدخلات — الفرق بين المحاولة والتأكيد
            // هو المعامل ده بس، فمفيش أي احتمال إن الطلبين يختلفوا
            async Task<FlowSaveResultDto> RecordFlowAsync(bool confirmOverride)
            {
                using var scope = _scopeFactory.CreateScope();
                var flowService = scope.ServiceProvider.GetRequiredService<ProductionFlowService>();
                // الخدمة بتتحقق من كل حاجة تاني (مصدر الحقيقة الوحيد للقواعد) — يا كله يا مفيش
                return await flowService.RecordFlowAsync(
                    SelectedProduct!.ProductId, entryDate, ranges, shares,
                    taggedWorkers: taggedWorkers,
                    confirmOverride: confirmOverride, operationsPassword: gate.Password);
            }
        }
    }
}
