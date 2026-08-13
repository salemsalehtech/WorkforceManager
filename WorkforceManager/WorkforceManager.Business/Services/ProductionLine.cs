using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// ترتيب مراحل المنتج — المكان الوحيد اللي بيجاوب على "المراحل ماشية
    /// بأي ترتيب؟" و"أنهي واحدة هي الأخيرة؟".
    ///
    /// السؤال التاني ده أهم مما شكله: **إنتاج آخر مرحلة = القطع اللي خرجت
    /// من الخط تامة**، وهو الرقم اللي كل التقارير والرسوم البيانية
    /// بتعرضه كإنتاج المصنع. لو اتحسب غلط، الرقم اللي المدير بيبني عليه
    /// قراراته غلط.
    ///
    /// كان مكتوب في تلات أماكن **بقاعدتين مختلفتين**: تقرير اليوم كان
    /// بياخد المراحل النشطة بس، والتقرير العام والرسم البياني كان عندهم
    /// نسخة زيادة بترجع لكل المراحل لو مفيش ولا واحدة نشطة. يعني منتج
    /// اتوقفت كل مراحله كان بيتحسب في شاشتين ويختفي من التالتة، والتوثيق
    /// بيقول إنهم بنفس القاعدة. بقوا كلهم بينادوا من هنا.
    /// </summary>
    public static class ProductionLine
    {
        /// <summary>
        /// مراحل المنتج النشطة بترتيب الخط.
        ///
        /// النشطة بس عن قصد: ده اللي بيتعرض للمستخدم وهو بيسجّل إنتاج
        /// النهارده، والمرحلة الموقوفة مش جزء من الخط الشغّال.
        /// بترجع فاضية لو كل المراحل موقوفة.
        ///
        /// **مرحلة الرص (IsRackingStage) مستبعدة هنا دايمًا.** موجودة
        /// كصف حقيقي في Product.Stages (تظهر في شاشة المنتج)، بس مش جزء
        /// من الخط المحسوب — مالها وجود في شاشة الإنتاج اليومي، وعامل
        /// الرص بتاعها بيتسجّل حاضر بشكل منفصل تمامًا (شوف
        /// ProductionFlowService)، مش عن طريق قطع على مرحلة في الخط.
        /// </summary>
        public static List<ProductionStage> Active(Product product) =>
            product.Stages
                .Where(s => s.IsActive && !s.IsRackingStage)
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
                .ToList();

        /// <summary>
        /// آخر مرحلة في الخط لأغراض **التقارير التاريخية** — أو null لو
        /// المنتج مالوش مراحل خالص.
        ///
        /// الفرق عن <see cref="Active"/>: لو كل المراحل موقوفة بتاخد أعلى
        /// ترتيب من كلها بدل ما ترجع لا حاجة. ده مقصود هنا وغلط هناك:
        /// منتج اتوقف الشهر ده لسه ليه إنتاج الشهر اللي فات، وتقرير
        /// الشهر اللي فات لازم يفضل يعرف أنهي مرحلة كانت الأخيرة عشان
        /// يعدّ التام صح. أما شاشة التسجيل فمش بتسجّل على منتج موقوف
        /// أصلاً.
        /// </summary>
        public static ProductionStage? LastForHistory(Product product)
        {
            // مرحلة الرص مستبعدة هنا كمان — شوف تعليق Active
            var stages = product.Stages.Where(s => !s.IsRackingStage).ToList();
            if (stages.Count == 0) return null;

            var pool = stages.Any(s => s.IsActive)
                ? stages.Where(s => s.IsActive)
                : stages;

            return pool
                .OrderByDescending(s => s.SortOrder).ThenByDescending(s => s.Id)
                .First();
        }

        /// <summary>
        /// معرّفات آخر مرحلة لكل منتج — الشكل اللي التقارير بتحتاجه عشان
        /// تسأل "السجل ده على آخر مرحلة؟" لكل سجل من غير ما تدوّر.
        /// </summary>
        public static Dictionary<int, int> LastStageIdByProduct(IEnumerable<Product> products)
        {
            var map = new Dictionary<int, int>();

            foreach (var product in products)
                if (LastForHistory(product) is { } last)
                    map[product.Id] = last.Id;

            return map;
        }
    }
}
