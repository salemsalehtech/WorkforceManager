using System.Text.Json;

namespace WorkforceManager.Data
{
    /// <summary>سطر اختيار واحد: النتيجة دي اتختارت كام مرة وآخر مرة إمتى، لاستعلام معين</summary>
    public class SearchRankingPick
    {
        /// <summary>هوية النتيجة — "Worker:42"، "Product:7"... نفس فئة+معرّف GlobalSearchResult</summary>
        public string ResultKey { get; set; } = "";

        public int PickCount { get; set; }
        public DateTime LastPickedAt { get; set; }
    }

    /// <summary>كل بيانات الترتيب بالاستخدام — استعلام مطبَّع → قايمة اختياراته</summary>
    public class SearchRankingData
    {
        /// <summary>المفتاح: نص البحث بعد ArabicSearch.Normalize — نفس التطبيع بالظبط، مفيش نسخة تانية</summary>
        public Dictionary<string, List<SearchRankingPick>> PicksByQuery { get; set; } = new();
    }

    /// <summary>
    /// قراءة وحفظ تاريخ اختيارات "بحث سريع" من ملف JSON بسيط جنب قاعدة
    /// البيانات — نفس نمط <see cref="AppSettingsStore"/> بالظبط: تلميح
    /// ترتيب خفيف مش بيانات عمل حقيقية، فمفيش داعي جدول DB/Migration.
    /// أي خطأ في القراءة (ملف تالف/ناقص) بيرجع بيانات فاضية بدل ما يكسر
    /// تشغيل البرنامج أو حتى البحث نفسه.
    /// </summary>
    public static class SearchRankingStore
    {
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        public static SearchRankingData Load(string? path = null)
        {
            var resolvedPath = path ?? AppPaths.SearchRankingPath;
            if (!File.Exists(resolvedPath)) return new SearchRankingData();

            try
            {
                return JsonSerializer.Deserialize<SearchRankingData>(File.ReadAllText(resolvedPath))
                       ?? new SearchRankingData();
            }
            catch
            {
                return new SearchRankingData();
            }
        }

        public static void Save(SearchRankingData data, string? path = null)
        {
            var resolvedPath = path ?? AppPaths.SearchRankingPath;
            Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath)!);
            File.WriteAllText(resolvedPath, JsonSerializer.Serialize(data, WriteOptions));
        }

        /// <summary>
        /// بيسجّل إن النتيجة دي اتختارت لاستعلام معين — بيزوّد PickCount لو
        /// كانت مختارة قبل كده لنفس الاستعلام (المطبَّع)، وإلا بيضيفها جديدة.
        /// قراءة وحفظ كاملين في نداء واحد — الحجم صغير جدًا (اختيارات بحث)
        /// فمفيش داعي تعقيد قفل/دمج تزامني.
        /// </summary>
        public static void RecordPick(string normalizedQuery, string resultKey, DateTime pickedAt, string? path = null)
        {
            if (string.IsNullOrWhiteSpace(normalizedQuery)) return;

            var data = Load(path);
            if (!data.PicksByQuery.TryGetValue(normalizedQuery, out var picks))
            {
                picks = new List<SearchRankingPick>();
                data.PicksByQuery[normalizedQuery] = picks;
            }

            var existing = picks.FirstOrDefault(p => p.ResultKey == resultKey);
            if (existing is null)
            {
                picks.Add(new SearchRankingPick { ResultKey = resultKey, PickCount = 1, LastPickedAt = pickedAt });
            }
            else
            {
                existing.PickCount++;
                existing.LastPickedAt = pickedAt;
            }

            Save(data, path);
        }

        /// <summary>
        /// آخر N استعلام اتختارت له نتيجة، الأحدث الأول — لعرض "آخر عمليات
        /// البحث" لما صندوق البحث لسه فاضي. بيعيد استخدام نفس بيانات
        /// PicksByQuery الموجودة أصلًا (مفيش تسجيل استعلام إضافي لكل حرف
        /// مكتوب، بس الاستعلامات اللي فعلًا اتختار لها نتيجة).
        /// </summary>
        public static IReadOnlyList<string> GetRecentQueries(int count, string? path = null)
        {
            var data = Load(path);

            return data.PicksByQuery
                .Where(kv => kv.Value.Count > 0)
                .Select(kv => (Query: kv.Key, LastPickedAt: kv.Value.Max(p => p.LastPickedAt)))
                .OrderByDescending(x => x.LastPickedAt)
                .Take(count)
                .Select(x => x.Query)
                .ToList();
        }
    }
}
