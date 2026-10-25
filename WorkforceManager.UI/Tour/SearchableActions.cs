using WorkforceManager.Core.Helpers;

namespace WorkforceManager.UI.Tour
{
    /// <summary>
    /// أمر مباشر من البحث السريع: المستخدم بيكتب اللي عايز يعمله ("سجّل غياب أحمد"، "أضف عامل"، "حفظ نهائي")
    /// والبحث بيعرض الأمر كنتيجة، والاختيار بينفّذه (MainWindow.RunSearchActionAsync). نفس فكرة
    /// <see cref="SearchableSettings"/>: محتوى واجهة ثابت مكتوب باليد، مش صف قاعدة بيانات.
    /// </summary>
    public sealed class SearchableAction
    {
        public required string Key { get; init; }
        public required string Title { get; init; }
        public required string Description { get; init; }

        /// <summary>
        /// العبارات اللي بتطلب الأمر (بتتطبّع بـ ArabicSearch.Normalize وقت المطابقة، فتتكتب طبيعي). الأمر بيتطابق لو
        /// كل كلمات عبارة منهم موجودة في البحث — والكلام الزيادة بيتحسب اسم عامل لو <see cref="TargetsWorker"/>.
        /// </summary>
        public required string[] Triggers { get; init; }

        /// <summary>الأمر بياخد عامل (الكلام الزيادة بعد العبارة = اسمه) — من غير اسم بيفتح الفورم فاضي</summary>
        public bool TargetsWorker { get; init; }
    }

    public static class SearchableActions
    {
        public const string RecordAttendance = "RecordAttendance";
        public const string RecordPenalty = "RecordPenalty";
        public const string RecordAdjustment = "RecordAdjustment";
        public const string AddWorker = "AddWorker";
        public const string AddProduct = "AddProduct";
        public const string RecordProduction = "RecordProduction";
        public const string FinalSave = "FinalSave";
        public const string Backup = "Backup";
        public const string ExportMonthlyPlan = "ExportMonthlyPlan";

        public static IReadOnlyList<SearchableAction> Entries { get; } = new List<SearchableAction>
        {
            new()
            {
                Key = RecordAttendance, Title = "سجّل حضور / غياب", TargetsWorker = true,
                Description = "يفتح الحضور والغياب في إدارة العمال، والعامل متحدد لو كتبت اسمه",
                Triggers = new[] { "سجل غياب", "سجل الغياب", "سجل حضور", "سجل الحضور", "غيب", "حضر" }
            },
            new()
            {
                Key = RecordPenalty, Title = "سجّل جزاء", TargetsWorker = true,
                Description = "يفتح الجزاءات والعامل متحدد في الفورم",
                Triggers = new[] { "سجل جزاء", "اعمل جزاء", "جزاء جديد", "جازي" }
            },
            new()
            {
                Key = RecordAdjustment, Title = "سجّل سلفة / حافز", TargetsWorker = true,
                Description = "يفتح السلف والحوافز والعامل متحدد في الفورم",
                Triggers = new[] { "سجل سلفه", "سلفه جديده", "اصرف سلفه", "سجل حافز", "حافز جديد", "اصرف حافز" }
            },
            new()
            {
                Key = AddWorker, Title = "أضف عامل جديد",
                Description = "يفتح فورم إضافة عامل",
                Triggers = new[] { "اضف عامل", "ضيف عامل", "اضيف عامل", "عامل جديد", "اضافه عامل" }
            },
            new()
            {
                Key = AddProduct, Title = "أضف منتج جديد",
                Description = "يفتح فورم إضافة منتج",
                Triggers = new[] { "اضف منتج", "ضيف منتج", "اضيف منتج", "منتج جديد", "اضافه منتج" }
            },
            new()
            {
                Key = RecordProduction, Title = "سجّل إنتاج العمال اليوم",
                Description = "يفتح صفحة إنتاج العمال اليوم في تسجيل يوميات العمال",
                Triggers = new[] { "سجل انتاج", "سجل الانتاج", "انتاج جديد", "سجل يوميه", "سجل اليوميات", "سجل يوميات" }
            },
            new()
            {
                Key = FinalSave, Title = "حفظ نهائي (توقيع اليوم)",
                Description = "يراجع كل اللي حصل النهارده ويوقّعه",
                Triggers = new[] { "حفظ نهائي", "توقيع اليوم", "وقع اليوم", "اقفل اليوم", "قفل اليوم" }
            },
            new()
            {
                Key = Backup, Title = "نسخة احتياطية",
                Description = "يفتح قسم النسخ الاحتياطي في الإعدادات",
                Triggers = new[] { "نسخه احتياطيه", "اعمل نسخه", "باك اب", "باكاب" }
            },
            new()
            {
                Key = ExportMonthlyPlan, Title = "صدّر الإنتاج الشهري لإكسل",
                Description = "يصدّر خطة الشهر والمحقق منها لملف Excel",
                Triggers = new[] { "صدر الخطه", "تصدير الخطه", "اكسل الخطه", "صدر الانتاج الشهري", "تصدير الانتاج الشهري" }
            },
        };

        /// <summary>
        /// الأوامر اللي البحث بيطلبها، مع الكلام الزيادة (اسم العامل المحتمل) لكل واحد. منطق نقي — مفيش قاعدة بيانات:
        /// عبارة كاملة موجودة في البحث = تطابق كامل (Exact)، وبحث لسه بيتكتب وهو بداية عبارة ("سجل غ") = تطابق جزئي
        /// عشان الأوامر تبان وإنت بتكتب.
        /// </summary>
        public static IEnumerable<(SearchableAction Action, bool Exact, string Remainder)> Match(string query)
        {
            var normalized = ArabicSearch.Normalize(query);
            if (normalized.Length < 3) yield break;

            var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

            foreach (var action in Entries)
            {
                var matched = false;
                foreach (var trigger in action.Triggers)
                {
                    var triggerWords = ArabicSearch.Normalize(trigger).Split(' ');
                    if (triggerWords.All(words.Contains))
                    {
                        var rest = words.Where(w => !triggerWords.Contains(w) && w is not ("ل" or "لل" or "على" or "علي")).ToList();
                        yield return (action, true, string.Join(' ', rest));
                        matched = true;
                        break;
                    }
                }

                if (matched) continue;

                // لسه بيكتب: البحث كله بداية عبارة (أو العبارة بداية البحث من غير الاسم)
                if (action.Triggers.Any(t => ArabicSearch.Normalize(t).StartsWith(normalized, StringComparison.Ordinal)))
                    yield return (action, false, "");
            }
        }
    }
}
