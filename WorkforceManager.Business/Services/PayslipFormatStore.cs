using System.Text.Json;
using WorkforceManager.Data;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// فورمات محفوظ لقسايم الأجر المطبوعة: اسم + مجموعة البنود الظاهرة
    /// (شوف <see cref="PayslipStripField"/>). بيتخزن بأسماء القيم مش
    /// القيم نفسها، زي <see cref="Data.AppSettings.PayslipStripFields"/> —
    /// عشان ده أهو المشترك بين الطبقتين.
    /// </summary>
    public class PayslipFormat
    {
        public required string Name { get; set; }
        public List<string> Fields { get; set; } = new();

        /// <summary>فورمات جاهز مع البرنامج — مبيتحذفش</summary>
        public bool IsBuiltIn { get; set; }

        public HashSet<PayslipStripField> ToFieldSet() =>
            Fields
                .Select(name => Enum.TryParse<PayslipStripField>(name, out var f) ? f : (PayslipStripField?)null)
                .Where(f => f is not null)
                .Select(f => f!.Value)
                .ToHashSet();

        public static PayslipFormat FromFields(string name, IEnumerable<PayslipStripField> fields, bool isBuiltIn = false) =>
            new() { Name = name, Fields = fields.Select(f => f.ToString()).ToList(), IsBuiltIn = isBuiltIn };
    }

    /// <summary>
    /// بيحفظ ويقرا فورمات قسايم الأجر من ملف JSON جنب قاعدة البيانات —
    /// نفس فكرة <see cref="ReportTemplateStore"/> بالظبط، بس لبنود
    /// القسيمة بس مش لتقرير كامل. المدير اللي بيطبع "قسيمة مفصّلة"
    /// لقسم وبيطبع "قسيمة مختصرة" لقسم تاني كان بيعلّم/يشيل البنود من
    /// الأول كل مرة يبدّل — الفورمات المحفوظ بيخليه ضغطة واحدة.
    /// </summary>
    public static class PayslipFormatStore
    {
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        private static string PathFor(string? custom) =>
            custom ?? System.IO.Path.Combine(AppPaths.DataFolder, "payslip-formats.json");

        /// <summary>
        /// الفورمات الجاهزة: الكامل (كل البنود، القسيمة الأصلية) والمختصر
        /// (عدد اليوميات والجزاء والحافز — والمبلغ النهائي ثابت دايمًا).
        /// </summary>
        public static List<PayslipFormat> BuiltIn() => new()
        {
            PayslipFormat.FromFields("الفورمات الكامل", PayslipStripExcelService.AllFields, isBuiltIn: true),
            PayslipFormat.FromFields("مختصر", new[]
            {
                PayslipStripField.NetWorkdays,
                PayslipStripField.PenaltyDeduction,
                PayslipStripField.Bonus
            }, isBuiltIn: true)
        };

        /// <summary>الجاهزة + اللي المستخدم حفظه. ملف تالف مبيمنعش الشاشة من الفتح</summary>
        public static List<PayslipFormat> Load(string? path = null) =>
            BuiltIn().Concat(LoadSaved(path)).ToList();

        public static List<PayslipFormat> LoadSaved(string? path = null)
        {
            var file = PathFor(path);
            if (!File.Exists(file)) return new List<PayslipFormat>();

            try
            {
                var list = JsonSerializer.Deserialize<List<PayslipFormat>>(File.ReadAllText(file));
                return list?.Where(f => !f.IsBuiltIn).ToList() ?? new List<PayslipFormat>();
            }
            catch
            {
                // ملف تالف عمره ما يمنع شاشة التقارير من الفتح
                return new List<PayslipFormat>();
            }
        }

        /// <summary>بيضيف فورمات أو بيستبدل اللي بنفس الاسم</summary>
        public static void Save(PayslipFormat format, string? path = null)
        {
            var saved = LoadSaved(path);

            saved.RemoveAll(f => string.Equals(f.Name, format.Name, StringComparison.OrdinalIgnoreCase));

            format.IsBuiltIn = false;
            saved.Add(format);

            Write(saved, path);
        }

        public static void Delete(string name, string? path = null)
        {
            var saved = LoadSaved(path);
            saved.RemoveAll(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            Write(saved, path);
        }

        private static void Write(List<PayslipFormat> formats, string? path)
        {
            var file = PathFor(path);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(formats, WriteOptions));
        }
    }
}
