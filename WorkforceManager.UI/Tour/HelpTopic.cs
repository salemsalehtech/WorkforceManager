using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WorkforceManager.UI.Tour
{
    /// <summary>
    /// موضوع واحد في شاشة "الدليل" — كارت شاشة بيتمدد (أكورديون) لقايمة
    /// ميزاتها الفردية (<see cref="TourSteps"/>)، كل ميزة بزرار "جرّبها"
    /// بيشغّل سبوت لايت لخطوة واحدة بس (تعيد استخدام <see cref="AppTourStep"/>
    /// نفسها اللي بتتشغّل من جولة "إيه الجديد") — المستخدم يختار هو عايز
    /// يتعلم إيه، مش جولة طويلة مفروضة عليه بالترتيب.
    ///
    /// <see cref="ObservableObject"/> بس عشان <see cref="IsExpanded"/> — حالة
    /// UI بسيطة بتعيش طول عمر الجلسة، مفيش داعي تتخزّن.
    /// </summary>
    public partial class HelpTopic : ObservableObject
    {
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required IReadOnlyList<AppTourStep> TourSteps { get; init; }

        /// <summary>اسم أيقونة MaterialDesign — نفس أيقونة الشاشة في القايمة الجانبية.</summary>
        public string Icon { get; init; } = "HelpCircleOutline";

        /// <summary>
        /// عدد الميزات الإجمالي — بيشمل ميزات المواضيع الفرعية (SubTopics)
        /// عشان تايل زي "تسجيل الإنتاج اليومي" (TourSteps بتاعته فاضية،
        /// الميزات كلها جوّه SubTopics) يوري رقم حقيقي على الشبكة بدل صفر.
        /// </summary>
        public int FeatureCount =>
            TourSteps.Count + SubTopics.Sum(s => s.TourSteps.Count);

        /// <summary>
        /// فلوات تدريب تفاعلي حقيقي (وضع تجربة، بيانات وهمية) — فاضية
        /// افتراضيًا، فباقي المواضيع مايتأثروش لحد ما يتضاف لهم فلو بنفس
        /// الطريقة اللي اتعملت بيها "إضافة مهارة" في موضوع العمال.
        /// </summary>
        public IReadOnlyList<GuidedPracticeFlow> GuidedFlows { get; init; } = Array.Empty<GuidedPracticeFlow>();

        public bool HasGuidedFlows => GuidedFlows.Count > 0;

        /// <summary>
        /// مواضيع فرعية متداخلة — مستخدمة لـ"تسجيل الإنتاج اليومي" بس دلوقتي
        /// (7 تبويبات داخلية، كل واحد HelpTopic عادي بميزاته الخاصة). فاضية
        /// افتراضيًا، فباقي المواضيع بتعرض TourSteps بتاعتها مباشرة زي ما هي.
        /// نفس النوع HelpTopic بالظبط بدل نوع جديد موازي — أي شاشة تانية
        /// تحتاج نفس الفكرة تقدر تستخدمها من غير أي تعديل هنا.
        /// </summary>
        public IReadOnlyList<HelpTopic> SubTopics { get; init; } = Array.Empty<HelpTopic>();

        public bool HasSubTopics => SubTopics.Count > 0;

        [ObservableProperty]
        private bool _isExpanded;
    }
}
