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

        [ObservableProperty]
        private bool _isExpanded;
    }
}
