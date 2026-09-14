namespace WorkforceManager.UI.Tour
{
    /// <summary>
    /// خطوة فرعية واحدة حقيقية جوّه فلو تفاعلي: بتلوّن سبوت لايت على عنصر
    /// حقيقي (نفس آلية <see cref="AppTourStep.TargetElementName"/> —
    /// MainWindow.FindTourTarget)، بس بتستنى إن الـViewModel الحقيقي المربوط
    /// بالشاشة يوصل لحالة معيّنة، مش دوسة "التالي". بتشتغل جوّه وضع التجربة
    /// بس (Sandbox.SandboxSession) — <see cref="AppTourStep"/> وكل الجولة
    /// العادية (سبوت لايت-شرح) مايتأثروش خالص، ده نوع شقيق جديد بس.
    /// </summary>
    public sealed class GuidedPracticeStep
    {
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required string TargetElementName { get; init; }

        /// <summary>
        /// بتتنادى على الـViewModel المربوط فعليًا بـMainContent.Content
        /// دلوقتي، أول ما الخطوة تتعرض وبعد كل تغيير جوّه القايمة تحت.
        /// </summary>
        public required Func<object, bool> IsComplete { get; init; }

        /// <summary>
        /// كائنات إضافية (PropertyChanged/CollectionChanged) لازم نراقبها
        /// كمان — كارت منتج معيّن، أو قايمة المراحل جوّاه — لأن التغيير اللي
        /// IsComplete بيدوّر عليه مش دايمًا على الـViewModel الجذر نفسه.
        /// </summary>
        public Func<object, IEnumerable<object>>? WatchSelectors { get; init; }
    }

    /// <summary>فلو كامل من خطوات حقيقية ورا بعض — إضافة مهارة، مثلًا.</summary>
    public sealed class GuidedPracticeFlow
    {
        public required string Title { get; init; }
        public required string Description { get; init; }
        public TourScreen Screen { get; init; } = TourScreen.None;
        public bool SelectFirstWorker { get; init; }
        public required IReadOnlyList<GuidedPracticeStep> Steps { get; init; }
    }
}
