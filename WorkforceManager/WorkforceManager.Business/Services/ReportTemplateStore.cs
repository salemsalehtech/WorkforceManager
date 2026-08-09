using System.Text.Json;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Data;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// قالب محفوظ: اسم + وصف التقرير.
    ///
    /// المدة **مش** بتتحفظ كتاريخين ثابتين — بتتحفظ كنوع (الأسبوع ده،
    /// الشهر ده...). قالب اسمه "أجور الشهر" لازم يجيب الشهر الحالي كل
    /// مرة، مش مارس اللي حفظته فيه.
    /// </summary>
    public class ReportTemplate
    {
        public required string Name { get; set; }
        public ReportSubject Subject { get; set; }
        public ReportGrouping GroupBy { get; set; }
        public ReportPeriodKind Period { get; set; } = ReportPeriodKind.ThisWeek;
        public WorkerKindFilter WorkerKind { get; set; } = WorkerKindFilter.All;

        /// <summary>قالب جاهز مع البرنامج — مبيتحذفش</summary>
        public bool IsBuiltIn { get; set; }

        public ReportSpec ToSpec()
        {
            var (from, to) = ReportPeriod.Resolve(Period);

            return new ReportSpec
            {
                Subject = Subject,
                GroupBy = GroupBy,
                From = from,
                To = to,
                WorkerKind = WorkerKind
            };
        }
    }

    /// <summary>المدة كنوع مش كتاريخين — عشان القالب يفضل صالح كل شهر</summary>
    public enum ReportPeriodKind
    {
        Today = 1,
        ThisWeek = 2,
        ThisMonth = 3,
        LastThirtyDays = 4,
        Custom = 5
    }

    /// <summary>بيحوّل نوع المدة لتاريخين — مكان واحد لكل الشاشات</summary>
    public static class ReportPeriod
    {
        public static (DateTime From, DateTime To) Resolve(ReportPeriodKind kind)
        {
            var today = DateTime.Today;

            return kind switch
            {
                ReportPeriodKind.Today => (today, today),
                // تعريف الأسبوع من مكانه الوحيد — خميس ← أربع
                ReportPeriodKind.ThisWeek => WeeklySummaryService.GetWorkWeekRange(today),
                ReportPeriodKind.ThisMonth => (new DateTime(today.Year, today.Month, 1), today),
                ReportPeriodKind.LastThirtyDays => (today.AddDays(-30), today),
                _ => (today, today)
            };
        }

        public static string Name(ReportPeriodKind kind) => kind switch
        {
            ReportPeriodKind.Today => "النهارده",
            ReportPeriodKind.ThisWeek => "الأسبوع ده",
            ReportPeriodKind.ThisMonth => "الشهر ده",
            ReportPeriodKind.LastThirtyDays => "آخر 30 يوم",
            _ => "مدة مخصوصة"
        };
    }

    /// <summary>
    /// بيحفظ ويقرا قوالب التقارير من ملف JSON جنب قاعدة البيانات.
    ///
    /// القوالب هي اللي بتخلي المرونة تنفع فعلاً: من غيرها المدير اللي
    /// بيعمل نفس التقرير كل شهر بيظبّطه من الأول كل شهر — يعني المرونة
    /// بتكلّفه شغل بدل ما توفّر عليه.
    ///
    /// الأربع تقارير اللي كانت شاشات مستقلة بقت قوالب جاهزة، فمفيش
    /// حاجة اتشالت من تحت إيد المستخدم — بالعكس، بقى يقدر يعدّلها.
    /// </summary>
    public static class ReportTemplateStore
    {
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        private static string PathFor(string? custom) =>
            custom ?? System.IO.Path.Combine(AppPaths.DataFolder, "report-templates.json");

        /// <summary>
        /// القوالب الجاهزة = التقارير الأربعة اللي كانت مكتوبة بإيدها.
        /// بتترجّع دايمًا حتى لو الملف مش موجود أو تالف.
        /// </summary>
        public static List<ReportTemplate> BuiltIn() => new()
        {
            new ReportTemplate
            {
                Name = "كشف الأسبوع",
                Subject = ReportSubject.Wages,
                GroupBy = ReportGrouping.Worker,
                Period = ReportPeriodKind.ThisWeek,
                IsBuiltIn = true
            },
            new ReportTemplate
            {
                Name = "كشف أجور الشهر",
                Subject = ReportSubject.Wages,
                GroupBy = ReportGrouping.Worker,
                Period = ReportPeriodKind.ThisMonth,
                IsBuiltIn = true
            },
            new ReportTemplate
            {
                Name = "إنتاج المنتجات",
                Subject = ReportSubject.Production,
                GroupBy = ReportGrouping.Product,
                Period = ReportPeriodKind.ThisMonth,
                IsBuiltIn = true
            },
            new ReportTemplate
            {
                Name = "إنتاج العمال",
                Subject = ReportSubject.Production,
                GroupBy = ReportGrouping.Worker,
                Period = ReportPeriodKind.ThisWeek,
                IsBuiltIn = true
            },
            new ReportTemplate
            {
                Name = "الغياب والجزاءات",
                Subject = ReportSubject.Attendance,
                GroupBy = ReportGrouping.Worker,
                Period = ReportPeriodKind.ThisMonth,
                IsBuiltIn = true
            },
            new ReportTemplate
            {
                Name = "تغطية المهارات",
                Subject = ReportSubject.Skills,
                GroupBy = ReportGrouping.Stage,
                Period = ReportPeriodKind.ThisWeek,
                IsBuiltIn = true
            }
        };

        /// <summary>الجاهزة + اللي المستخدم حفظه. ملف تالف مبيمنعش الشاشة من الفتح</summary>
        public static List<ReportTemplate> Load(string? path = null)
        {
            var saved = LoadSaved(path);

            return BuiltIn().Concat(saved).ToList();
        }

        public static List<ReportTemplate> LoadSaved(string? path = null)
        {
            var file = PathFor(path);
            if (!File.Exists(file)) return new List<ReportTemplate>();

            try
            {
                var list = JsonSerializer.Deserialize<List<ReportTemplate>>(File.ReadAllText(file));
                return list?.Where(t => !t.IsBuiltIn).ToList() ?? new List<ReportTemplate>();
            }
            catch
            {
                // ملف تالف عمره ما يمنع شاشة التقارير من الفتح
                return new List<ReportTemplate>();
            }
        }

        /// <summary>بيضيف قالب أو بيستبدل اللي بنفس الاسم</summary>
        public static void Save(ReportTemplate template, string? path = null)
        {
            var saved = LoadSaved(path);

            saved.RemoveAll(t => string.Equals(t.Name, template.Name, StringComparison.OrdinalIgnoreCase));

            template.IsBuiltIn = false;
            saved.Add(template);

            Write(saved, path);
        }

        public static void Delete(string name, string? path = null)
        {
            var saved = LoadSaved(path);
            saved.RemoveAll(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            Write(saved, path);
        }

        private static void Write(List<ReportTemplate> templates, string? path)
        {
            var file = PathFor(path);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(templates, WriteOptions));
        }
    }
}
