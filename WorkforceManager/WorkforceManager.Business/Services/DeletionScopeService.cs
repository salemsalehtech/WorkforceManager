using WorkforceManager.Core.Interfaces;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// بيجاوب على سؤال واحد: الحاجة دي ينفع تتمسح نهائي ولا لازم تتعلّم
    /// محذوفة بس؟
    ///
    /// القاعدة: **ينفع تتمسح نهائي طول ما مفيش تاريخ أجور بيشاور عليها.**
    ///
    /// ليه التاريخ بالذات: كشوف الأجور والتقارير القديمة بتقرا اسم العامل
    /// والمنتج والمرحلة من الصفوف دي. لو الصف اتمسح، كشف الشهر اللي فات
    /// بيتحوّل لأرقام من غير أصحاب — ولا حتى ده، لأن قيد المفتاح الأجنبي
    /// على سجلات الإنتاج بيرفض المسح من الأساس فالعملية بتفشل برسالة
    /// قاعدة بيانات المستخدم مش هيفهمها.
    ///
    /// وفي كل الحالات التانية — منتج اتضاف بالغلط، عامل اتسجّل مرتين،
    /// مرحلة اتكتبت غلط — الصف بيتمسح ومبيسبش وراه حاجة. دي الحالة
    /// الشائعة، وهي اللي كانت بتترّاكم صفوف ميتة في الجداول للأبد.
    ///
    /// الحضور والمهارات والجزاءات والسلف بتتشال مع العامل (Cascade)
    /// لأنها بتخصّه هو مالهاش معنى من غيره. الجزاءات والسلف بتمنع المسح
    /// برضه لأنها حركة فلوس.
    /// </summary>
    public class DeletionScopeService
    {
        private readonly IDailyProductionRepository _production;
        private readonly IHourlyWorkLogRepository _hourly;
        private readonly IPenaltyRepository _penalties;
        private readonly IWageAdjustmentRepository _adjustments;
        private readonly IProductRepository _products;

        public DeletionScopeService(
            IDailyProductionRepository production,
            IHourlyWorkLogRepository hourly,
            IPenaltyRepository penalties,
            IWageAdjustmentRepository adjustments,
            IProductRepository products)
        {
            _production = production;
            _hourly = hourly;
            _penalties = penalties;
            _adjustments = adjustments;
            _products = products;
        }

        /// <summary>
        /// العامل ينفع يتمسح نهائي؟ لأ لو ليه إنتاج، أو شغل بالساعة، أو
        /// جزاء، أو سلفة/حافز — الأربعة دول بيدخلوا في أجره.
        /// </summary>
        public async Task<bool> CanRemoveWorkerAsync(int workerId) =>
            !await _production.HasAnyForWorkerAsync(workerId) &&
            !await _hourly.HasAnyForWorkerAsync(workerId) &&
            !await _penalties.HasAnyForWorkerAsync(workerId) &&
            !await _adjustments.HasAnyForWorkerAsync(workerId);

        /// <summary>المرحلة تتمسح نهائي طول ما مفيش إنتاج اتسجّل عليها</summary>
        public async Task<bool> CanRemoveStageAsync(int stageId) =>
            !await _production.HasAnyForStageAsync(stageId);

        /// <summary>
        /// المنتج يتمسح نهائي طول ما مفيش إنتاج على أي مرحلة من مراحله.
        /// مراحله بتتشال معاه (Cascade) — مرحلة من غير منتج مالهاش معنى.
        /// </summary>
        public async Task<bool> CanRemoveProductAsync(int productId)
        {
            var product = await _products.GetWithStagesAsync(productId);
            if (product is null) return false;

            foreach (var stage in product.Stages)
                if (await _production.HasAnyForStageAsync(stage.Id))
                    return false;

            return true;
        }
    }
}
