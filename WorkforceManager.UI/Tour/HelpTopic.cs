namespace WorkforceManager.UI.Tour
{
    /// <summary>
    /// موضوع واحد في شاشة "الدليل" — شرح كامل لشاشة في البرنامج، بلغة
    /// المستخدم العادية، مع جولة سبوت لايت قصيرة (تعيد استخدام
    /// <see cref="AppTourStep"/> نفسها اللي بتتشغّل من جولة "إيه الجديد")
    /// لو حابب يشوف الميزة في مكانها بدل ما يقرا بس.
    /// </summary>
    public class HelpTopic
    {
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required IReadOnlyList<AppTourStep> TourSteps { get; init; }
    }
}
