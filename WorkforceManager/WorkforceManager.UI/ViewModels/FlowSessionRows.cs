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
using WorkforceManager.UI.Views;
using System.Linq;

namespace WorkforceManager.UI.ViewModels
{
    // صفوف رحلة الإنتاج داخل شاشة التسجيل اليومي:
    // WorkerPick = عامل مؤهل في قايمة الاختيار،
    // FlowStageRow = بطاقة مرحلة بعمالها، FlowShareEntry = نصيب عامل،
    // FlowRangeRow = نطاق "من مرحلة لمرحلة: كام قطعة".

    public record WorkerPick(
        int WorkerId,
        string Name,
        int Stars = SkillRatingService.DefaultStars,
        decimal MeasuredRatio = 1.0m,
        int MeasuredDays = 0,
        bool IsTagOnly = false,
        string TagLabel = "")
    {
        /// <summary>النجوم كنص ("★★★★☆")</summary>
        public string StarsText => RtlSafeText.Stars(Stars);

        public bool HasTagLabel => TagLabel.Length > 0;

        /// <summary>فيه قياس أداء فعلي ولا لسه؟</summary>
        public bool HasMeasurement => MeasuredDays > 0;

        /// <summary>الأداء المقاس كنسبة ("115%")</summary>
        public string MeasuredText => HasMeasurement ? $"{MeasuredRatio * 100:0}%" : "";

        /// <summary>
        /// شرح التقييم: تقييم المدير + الأداء المقاس لو موجود.
        /// المستخدم لازم يعرف الرقم ده رأي مين قبل ما يبني عليه قرار.
        /// </summary>
        public string RatingTooltip => MeasuredDays > 0
            ? $"تقييمك: {SkillRatingService.StarsLabel(Stars)} ({Stars}/5) — " +
              $"إنتاجه الفعلي {MeasuredRatio * 100:0}% من الكوتة على مدار {MeasuredDays} يوم"
            : $"تقييمك: {SkillRatingService.StarsLabel(Stars)} ({Stars}/5) — لسه مافيش إنتاج كفاية للقياس";

        /// <summary>4 نجوم أو أكتر (بيتلوّن أخضر في القايمة)</summary>
        public bool IsTopRated => Stars >= 4;

        /// <summary>نجمتين أو أقل (بيتلوّن أصفر)</summary>
        public bool IsLowRated => Stars <= 2;

        /// <summary>
        /// مكلّف النهارده على مرحلة تانية — "المنتج / المرحلة"، أو null.
        ///
        /// بيتحدّد قبل ما العنصر يتحط في القايمة (اللي بتتفضى وتتملي من
        /// الأول في كل فلترة)، فالعناصر بتتبني من جديد ومفيش لزوم لإشعار
        /// تغيير هنا.
        /// </summary>
        public string? AssignedElsewhere { get; set; }

        public bool IsAssignedElsewhere => AssignedElsewhere is not null;

        public string AssignedElsewhereText =>
            AssignedElsewhere is null ? "" : $"مكلّف على {AssignedElsewhere}";
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

        /// <summary>(workerId, exceptStageId) → "المنتج / المرحلة" لو مكلّف في مكان تاني النهارده</summary>
        private readonly Func<int, int, string?> _describeAssignedElsewhere;

        public FlowStageRow(
            Func<FlowStageRow, Task> onAddWorker,
            Func<int, int, string?> describeAssignedElsewhere)
        {
            _onAddWorker = onAddWorker;
            _describeAssignedElsewhere = describeAssignedElsewhere;
        }

        public int StageId { get; init; }
        public int DisplayOrder { get; init; }
        public string StageName { get; init; } = "";
        public int Quota { get; init; }

        /// <summary>
        /// مرحلة رص إدارية — بلا نطاق/قطع خالص، وعاملها بيتحط تاج بيومية
        /// مباشرة (شوف <see cref="WorkforceManager.Core.Models.ProductionStage.IsRackingStage"/>).
        /// </summary>
        public bool IsRackingStage { get; init; }

        /// <summary>
        /// عمال المرحلة في قايمة "+ عامل" الموحّدة: المؤهلين
        /// (WorkerSkill) للمراحل العادية، أو عمال الرص (HourlyRole.Racking)
        /// لمرحلة الرص نفسها — بالإضافة للمتدرّبين (HourlyRole.Training)
        /// المتاحين على أي مرحلة عادية، مع بادج "تحت التدريب"
        /// (<see cref="WorkerPick.IsTagOnly"/>/<see cref="WorkerPick.TagLabel"/>).
        /// قايمة واحدة موحّدة عن قصد — المتدرّب بيتحط بنفس زرار "+ عامل"
        /// اللي بيتحط بيه أي عامل تاني، مش بعنصر واجهة منفصل.
        /// </summary>
        public List<WorkerPick> QualifiedWorkers { get; init; } = new();

        /// <summary>مفيش عمال مؤهلين للمرحلة دي — لازم تتربط المهارات الأول (قرار: المؤهلين بس)</summary>
        public bool HasNoQualified => QualifiedWorkers.Count == 0;

        /// <summary>مرحلة الرص مالهاش يومية حقيقية (القيمة صورية لقيد قاعدة البيانات بس)</summary>
        public bool HasQuota => !IsRackingStage;

        public string QuotaText => $"اليومية: {Quota}";

        // ------- بحث في قايمة العمال المؤهلين -------

        /// <summary>
        /// نص البحث في قايمة عمال المرحلة. المرحلة ممكن يكون ليها ٢٠ عامل
        /// مؤهل، والنزول فيهم بالماوس كل مرة بطيء.
        /// </summary>
        [ObservableProperty]
        private string _workerSearch = string.Empty;

        partial void OnWorkerSearchChanged(string value)
        {
            // الكتابة معناها إنه بيدوّر — القايمة تفتح
            IsPickerOpen = true;
            ApplyWorkerFilter();
        }

        /// <summary>
        /// قايمة الاقتراحات مفتوحة؟
        ///
        /// كانت بتتحكم بالفوكس لوحده، فبعد إضافة عامل كانت بتفضل مفتوحة
        /// (الفوكس مكانه والاقتراحات رجعت كاملة) وبتغطي المرحلة اللي
        /// بعدها. بقت بتتقفل مع كل إضافة، والمستخدم بيفتحها لما يدوس
        /// على خانة البحث أو يكتب فيها.
        /// </summary>
        [ObservableProperty]
        private bool _isPickerOpen;

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
            var pool = matches
                .Where(w => AssignedWorkers.All(a => a.WorkerId != w.WorkerId))
                .ToList();

            // اللي مكلّف على مرحلة تانية النهارده ينزل آخر القايمة، ومعاه
            // علامة بتقول هو فين.
            //
            // مش ترتيب شكلي: العامل الواحد له تكليف واحد في اليوم
            // (WorkerAssignmentGuard)، فاختياره بيطلّع رسالة تأكيد.
            // تنزيله بيدّي مساحة للناس الفاضية الأول، والعلامة بتفسّر
            // الرسالة قبل ما تحصل بدل ما تيجي مفاجأة.
            //
            // OrderBy في LINQ ثابت (stable)، فترتيب التقييم جوّه كل
            // مجموعة بيفضل زي ما هو.
            foreach (var w in pool)
                w.AssignedElsewhere = _describeAssignedElsewhere(w.WorkerId, StageId);

            VisibleWorkers.Clear();
            foreach (var w in pool.OrderBy(w => w.IsAssignedElsewhere ? 1 : 0))
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

            // آخر سطر عن قصد: تفضية البحث فوق بتفتح القايمة، والقفل هنا
            // هو الكلمة الأخيرة
            IsPickerOpen = false;
        }

        // ------- حالة المرحلة (لون وعلامة على البطاقة) -------

        /// <summary>
        /// حالة المرحلة دلوقتي — دي اللي بتلوّن البطاقة وبتخلي المستخدم
        /// يعرف بنظرة واحدة مين ناقص من غير ما يقرا كل بطاقة.
        ///
        /// **مفيش مقارنة بين توزيع العمال والإنتاج المحسوب هنا عن قصد**:
        /// قطعة العامل عدد ضرباته على المكنة (أساس يوميته)، والإنتاج
        /// الفعلي رقم منفصل تمامًا — اختلافهم طبيعي مش عطل، شوف
        /// ProductionStageOutputService. مرحلة عليها إنتاج وعامل واحد
        /// على الأقل جاهزة، بصرف النظر عن مجموع أنصبتهم.
        /// </summary>
        public FlowStageState State
        {
            get
            {
                // مرحلة الرص مالهاش نطاق قطع خالص — عامل واحد بس متحط
                // تاجه يكفي عشان تبقى "جاهزة"، وده مش "عليها عمال من غير
                // إنتاج" زي المراحل العادية (ده متوقع منها بالتصميم)
                if (IsRackingStage)
                    return AssignedWorkers.Count > 0 ? FlowStageState.Ready : FlowStageState.NotToday;

                if (ComputedPieces == 0)
                    return AssignedWorkers.Count > 0 ? FlowStageState.WorkersWithoutPieces : FlowStageState.NotToday;

                return AssignedWorkers.Count == 0 ? FlowStageState.NeedsWorkers : FlowStageState.Ready;
            }
        }

        public bool IsReady => State == FlowStageState.Ready;

        /// <summary>
        /// مفتاح فرشاة الشريط الجانبي للبطاقة حسب الحالة — مش كود لون
        /// (شوف <see cref="ThemeBrush"/>).
        /// </summary>
        public string StateColor => State switch
        {
            FlowStageState.Ready => "GoodBrush",              // تمام
            FlowStageState.NeedsWorkers => "WarnBrush",       // عليها إنتاج ومحتاجة عمال
            FlowStageState.WorkersWithoutPieces => "WarnBrush",
            _ => "LineBrush"                                  // باهت: مش داخلة النهارده
        };

        /// <summary>الرسالة القصيرة اللي بتظهر على البطاقة</summary>
        public string StateText => State switch
        {
            FlowStageState.Ready => "جاهزة",
            FlowStageState.NeedsWorkers => "محتاجة عمال",
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
            Action onEdited, Action<FlowShareEntry> onRemove,
            bool isTagOnly = false, string tagLabel = "")
        {
            Parent = parent;
            WorkerId = workerId;
            WorkerName = workerName;
            IsTagOnly = isTagOnly;
            TagLabel = tagLabel;
            _onEdited = onEdited;
            _onRemove = onRemove;
        }

        /// <summary>البطاقة الأم — عشان أمر الإزالة يعرف يشيل النصيب من مرحلته</summary>
        public FlowStageRow Parent { get; }
        public int WorkerId { get; }
        public string WorkerName { get; }

        /// <summary>
        /// تاج بس (متدرّب على مرحلة عادية، أو عامل الرص على مرحلة الرص) —
        /// مالوش نصيب قطع خالص، الشريحة بتعرض بادج <see cref="TagLabel"/>
        /// بدل خانة العدد. بيتحوّل لـ <see cref="WorkforceManager.Business.DTOs.FlowTaggedWorkerDto"/>
        /// وقت الحفظ، منفصل تمامًا عن shares.
        /// </summary>
        public bool IsTagOnly { get; }

        /// <summary>نص البادج على الشريحة ("تحت التدريب") — فاضي لمرحلة الرص نفسها (كل عمالها رص أصلًا)</summary>
        public string TagLabel { get; }

        public bool HasTagLabel => TagLabel.Length > 0;

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
            Action onEdited,
            Action<FlowRangeRow> onRemove)
        {
            StageOptions = stageOptions;
            _onEdited = onEdited;
            _onRemove = onRemove;
        }

        /// <summary>مراحل المنتج بالترتيب — نفس القائمة لقايمتي "من" و"إلى"</summary>
        public List<StageEntryOption> StageOptions { get; }

        [ObservableProperty]
        private StageEntryOption? _fromStage;

        [ObservableProperty]
        private StageEntryOption? _toStage;

        [ObservableProperty]
        private string _piecesText = "";

        partial void OnFromStageChanged(StageEntryOption? value) => _onEdited();
        partial void OnToStageChanged(StageEntryOption? value) => _onEdited();
        partial void OnPiecesTextChanged(string value) => _onEdited();

        /// <summary>
        /// خطأ النطاق ده بالذات (فاضي = سليم).
        ///
        /// الخطأ بيتعرض **تحت النطاق نفسه** مش في بلوك تحذيرات فوق:
        /// البلوك كان بيجمّع أخطاء كل النطاقات في نص واحد، فالمستخدم اللي
        /// عنده تلات نطاقات كان لازم يقرا الرسالة ويدوّر هي بتتكلم عن
        /// أنهي واحد فيهم.
        /// </summary>
        [ObservableProperty]
        private string _error = "";

        partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

        public bool HasError => Error.Length > 0;

        [RelayCommand]
        private void Remove() => _onRemove(this);
    }
}
