namespace WorkforceManager.UI.Tour
{
    /// <summary>
    /// نص أيقونة "؟" لكل خانة مالهاش شرح تاني في البرنامج — مكان واحد،
    /// نفس فكرة KeyboardShortcutsContent. كل نص جملة واحدة بس؛ الشرح
    /// الكامل (لو موجود) في الدليل، مش هنا.
    /// </summary>
    public static class FieldHelpText
    {
        public const string PieceWeight =
            "بيتحسب بيه بس وزن الخطة الشهرية بالكيلو — مالوش أي تأثير على حاجة تانية في البرنامج.";

        public const string Material =
            "بتتقسم بيها الخطة الشهرية لكل مادة لوحدها (نحاس/زاما) — مالهاش أي تأثير على حاجة تانية.";

        public const string AchievedPercent =
            "مقارنة بنصيب النهارده من الخطة (مش الخطة الشهرية كلها) — يوم 10 من 30 نصيبه تقريبًا تلت الخطة.";

        public const string RequiredDailyOutput =
            "عدد القطع المطلوب إنتاجه يوميًا في الأيام الباقية عشان الخطة تتحقق بالكامل آخر الشهر.";

        public const string PaceStatus =
            "متأخر لو المحقق أقل من 90% من نصيبه لحد النهارده، سابق لو أكتر من 110%، وماشي صح لو بينهم.";

        public const string InitialBalance =
            "شغل دخل الخط ومكملش لآخره لسه — تقدر تسحب منه لما يكمل، أو تحوّله لهالك لو اتعطّب.";
    }
}
