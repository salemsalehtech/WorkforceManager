using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Interfaces;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// المصدر الوحيد لقاعدة "العامل ميشتغلش على أكتر من (منتج + مرحلة)
    /// في نفس يوم الإنتاج". أي مكان بيضيف تكليف — رحلة الإنتاج، التسجيل
    /// المفرد، التسجيل بالجملة، أو أي مدخل يتضاف بعدين — لازم يعدّي من
    /// هنا؛ ممنوع نسخ الشرط في أي كلاس تاني.
    ///
    /// القواعد بالترتيب اللي بتتفحص بيه (الترتيب مقصود):
    /// • تكرار حرفي (نفس العامل + نفس المرحلة + نفس اليوم) → ممنوع دايمًا،
    ///   حتى لو المستخدم أكّد. ده مش تخطي قاعدة، ده تسجيل مكرر بالغلط.
    ///   الاستثناء الوحيد: التكليف متعلّم <see cref="WorkerAssignmentDto.IsRework"/>
    ///   — يعني المستخدم قال بنفسه إن العامل رجع صلّح شغل خلص، مش نسي
    ///   إنه سجّله. ده علم مستقل بيتحط لتكليف واحد بعينه، مش
    ///   <c>confirmOverride</c> العام (اللي بيفضل عاجز عن تعدية التكرار).
    /// • العامل مكلّف بمرحلة تانية (نفس المنتج) أو بمنتج تاني في نفس اليوم
    ///   → محتاج تأكيد صريح من المستخدم.
    /// • غير كده → تكليف عادي.
    ///
    /// <see cref="Evaluate"/> دالة خالصة (بدون قاعدة بيانات) عشان نفس
    /// القاعدة بالظبط تشتغل في الواجهة للتحذير الفوري وهي بتقارن بصفوف
    /// لسه في الذاكرة — والخدمة هي اللي بتفصل وقت الحفظ.
    /// </summary>
    public class WorkerAssignmentGuard
    {
        private readonly IDailyProductionRepository _productionRepo;

        public WorkerAssignmentGuard(IDailyProductionRepository productionRepo) =>
            _productionRepo = productionRepo;

        /// <summary>
        /// بيقرا تكليفات اليوم المحفوظة فعليًا وبيفحص عليها التكليفات المطلوبة.
        /// لازم يتنادى **جوه** معاملة الكتابة عشان القرار يتاخد على بيانات
        /// محميّة بالقفل (راجع <see cref="IUnitOfWork"/>).
        /// </summary>
        public async Task<AssignmentCheckResultDto> CheckAsync(
            DateTime date, IReadOnlyList<WorkerAssignmentDto> requested)
        {
            var existing = await GetDayAssignmentsAsync(date);
            return Evaluate(existing, requested);
        }

        /// <summary>تكليفات يوم كامل زي ما هي محفوظة — الواجهة بتستخدمها للتحذير الفوري كمان</summary>
        public async Task<IReadOnlyList<WorkerAssignmentDto>> GetDayAssignmentsAsync(DateTime date)
        {
            // GetByDateAsync بتحمّل العامل والمرحلة ومنتجها معاها، فمفيش استعلامات إضافية
            var records = await _productionRepo.GetByDateAsync(date);

            return records
                .Select(r => new WorkerAssignmentDto
                {
                    WorkerId = r.WorkerId,
                    WorkerName = r.Worker?.FullName ?? string.Empty,
                    ProductId = r.ProductionStage?.ProductId ?? 0,
                    ProductName = r.ProductionStage?.Product?.Name ?? string.Empty,
                    ProductionStageId = r.ProductionStageId,
                    StageName = r.ProductionStage?.StageName ?? string.Empty,
                    IsRework = r.IsRework
                })
                .ToList();
        }

        /// <summary>
        /// القاعدة نفسها — خالصة وقابلة للاختبار من غير قاعدة بيانات.
        ///
        /// <paramref name="requested"/> بتتفحص بالترتيب وكل واحدة بتتقارن
        /// بالمحفوظ **زائد** اللي قبلها في نفس الطلب. السبب: لو طلب واحد
        /// فيه نفس العامل على مرحلتين، ده تعارض حقيقي بنفس المنطق — ولو
        /// قارنّا بالمحفوظ بس كان هيعدّي من غير ما حد يشوفه.
        /// </summary>
        public static AssignmentCheckResultDto Evaluate(
            IReadOnlyList<WorkerAssignmentDto> existing,
            IReadOnlyList<WorkerAssignmentDto> requested)
        {
            var duplicates = new List<WorkerAssignmentDto>();
            var conflicts = new List<AssignmentConflictDto>();

            // نسخة بتكبر مع كل تكليف مقبول، عشان "المكلّف به بالفعل" يشمل
            // اللي اتضاف في نفس الطلب (مش المحفوظ بس)
            var known = existing.ToList();

            foreach (var attempted in requested)
            {
                var sameWorker = known.Where(k => k.WorkerId == attempted.WorkerId).ToList();

                // R5: نفس العامل على نفس المرحلة في نفس اليوم = تسجيل مكرر، ممنوع
                if (sameWorker.Any(k => k.ProductionStageId == attempted.ProductionStageId))
                {
                    // ...إلا لو ده إعادة عمل مقصودة: العامل رجع صلّح نفس
                    // المرحلة. مفيش تعارض R2 كمان — المرحلة هي هي أصلاً
                    if (attempted.IsRework)
                    {
                        known.Add(attempted);
                        continue;
                    }

                    duplicates.Add(attempted);
                    continue; // متتحسبش تعارض كمان — التكرار حالة مستقلة
                }

                // R2: مكلّف بمرحلة تانية أو منتج تاني في نفس اليوم = محتاج تأكيد
                var blocking = sameWorker.FirstOrDefault(k =>
                    k.ProductId != attempted.ProductId ||
                    k.ProductionStageId != attempted.ProductionStageId);

                if (blocking is not null)
                {
                    conflicts.Add(new AssignmentConflictDto
                    {
                        Existing = blocking,
                        Attempted = attempted
                    });
                }

                // R1/R3: التكليف مقبول (يا إما من غير تعارض، يا إما بعد التأكيد)
                // وبيتحسب "معروف" للي بعده في نفس الطلب
                known.Add(attempted);
            }

            return new AssignmentCheckResultDto
            {
                Duplicates = duplicates,
                Conflicts = conflicts
            };
        }

        /// <summary>
        /// بيطبّق نتيجة الفحص على مسار كتابة: بيرمي الاستثناء المناسب أو
        /// بيسيب المتصل يكمّل. مكان واحد لقرار "نكمّل ولا نقف" عشان كل
        /// مسارات الكتابة تتصرف بنفس الشكل وبنفس الرسائل.
        /// </summary>
        /// <param name="confirmOverride">
        /// المستخدم أكّد صراحة على التعارضات. بيخص الطلب ده بس — مالوش أي
        /// أثر على أي عملية تانية، ومبيلغيش منع التكرار الحرفي.
        /// </param>
        public static void EnsureAllowed(AssignmentCheckResultDto result, bool confirmOverride)
        {
            // التكرار الحرفي ممنوع دايمًا — التأكيد مبيعديهوش (R5).
            // اللي بيعدّي هو علم IsRework على التكليف نفسه، اللي Evaluate
            // بيفلتره قبل ما يوصل هنا أصلاً — مش الموافقة العامة دي.
            if (result.HasDuplicates)
            {
                var names = string.Join("\n", result.Duplicates
                    .Select(d => $"  • {d.WorkerName}: {d.Display}"));

                throw new InvalidOperationException(
                    "التكليفات دي مسجلة بالفعل في نفس اليوم:\n" + names +
                    "\n\nلو عايز تعدّل عددها، استخدم تبويب \"سجلات اليوم\".");
            }

            // تعارض من غير تأكيد → وقف قبل أي كتابة، والمتصل بيسأل المستخدم (R2/R4)
            if (result.RequiresConfirmation && !confirmOverride)
                throw new AssignmentConfirmationRequiredException(result.Conflicts);

            // R1 (مفيش تعارض) أو R3 (اتأكد) → كمّل عادي
        }
    }
}
