// ملف مولّد تلقائيًا من بيانات العميل الحقيقية (Salem.xlsx + اسماء الصنفرة)
// لا تعدل يدويًا — أعد توليده من ملفات الإكسل الأصلية لو البيانات اتغيرت
using System.Collections.Generic;
using System.Linq;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Data.Seed
{
    public static class RealDataSeed
    {
        public static List<Product> BuildProducts()
        {
            var products = new List<Product>();

            products.Add(new Product { Name = "GRS", IsActive = true, Stages = new List<ProductionStage> {
                new ProductionStage { StageName = "دبله", PiecesPerWorkday = 5000, SortOrder = 0, IsActive = true },
                new ProductionStage { StageName = "رقبه", PiecesPerWorkday = 5000, SortOrder = 1, IsActive = true },
                new ProductionStage { StageName = "لفه صغيره", PiecesPerWorkday = 5000, SortOrder = 2, IsActive = true },
                new ProductionStage { StageName = "دبله قدام", PiecesPerWorkday = 5000, SortOrder = 3, IsActive = true },
                new ProductionStage { StageName = "بطن خشن", PiecesPerWorkday = 5000, SortOrder = 4, IsActive = true },
                new ProductionStage { StageName = "ضربتين", PiecesPerWorkday = 3333, SortOrder = 5, IsActive = true },
                new ProductionStage { StageName = "بطحتين", PiecesPerWorkday = 2500, SortOrder = 6, IsActive = true },
                new ProductionStage { StageName = "لفه 400", PiecesPerWorkday = 5000, SortOrder = 7, IsActive = true },
                new ProductionStage { StageName = "بطن ناعم", PiecesPerWorkday = 5000, SortOrder = 8, IsActive = true },
                new ProductionStage { StageName = "لفه 600", PiecesPerWorkday = 5000, SortOrder = 9, IsActive = true },
                new ProductionStage { StageName = "لفه 800", PiecesPerWorkday = 5000, SortOrder = 10, IsActive = true },
            }});
            products.Add(new Product { Name = "MG", IsActive = true, Stages = new List<ProductionStage> {
                new ProductionStage { StageName = "رايش بطن", PiecesPerWorkday = 5000, SortOrder = 0, IsActive = true },
                new ProductionStage { StageName = "دبله", PiecesPerWorkday = 5000, SortOrder = 1, IsActive = true },
                new ProductionStage { StageName = "ضربه", PiecesPerWorkday = 5000, SortOrder = 2, IsActive = true },
                new ProductionStage { StageName = "رقبه", PiecesPerWorkday = 5000, SortOrder = 3, IsActive = true },
                new ProductionStage { StageName = "لفه 400", PiecesPerWorkday = 5000, SortOrder = 4, IsActive = true },
                new ProductionStage { StageName = "ديق 600", PiecesPerWorkday = 3333, SortOrder = 5, IsActive = true },
                new ProductionStage { StageName = "لفه 800", PiecesPerWorkday = 5000, SortOrder = 6, IsActive = true },
                new ProductionStage { StageName = "ديق 800", PiecesPerWorkday = 3333, SortOrder = 7, IsActive = true },
                new ProductionStage { StageName = "عريض 800", PiecesPerWorkday = 5000, SortOrder = 8, IsActive = true },
                new ProductionStage { StageName = "بطن 400", PiecesPerWorkday = 5000, SortOrder = 9, IsActive = true },
                new ProductionStage { StageName = "بطن 600", PiecesPerWorkday = 5000, SortOrder = 10, IsActive = true },
                new ProductionStage { StageName = "بطن 800", PiecesPerWorkday = 5000, SortOrder = 11, IsActive = true },
            }});
            products.Add(new Product { Name = "ماكس فتيل", IsActive = true, Stages = new List<ProductionStage> {
                new ProductionStage { StageName = "رقبه", PiecesPerWorkday = 5000, SortOrder = 0, IsActive = true },
                new ProductionStage { StageName = "ضربتين", PiecesPerWorkday = 2500, SortOrder = 1, IsActive = true },
                new ProductionStage { StageName = "ديق", PiecesPerWorkday = 3333, SortOrder = 2, IsActive = true },
                new ProductionStage { StageName = "بطن خشن", PiecesPerWorkday = 5000, SortOrder = 3, IsActive = true },
                new ProductionStage { StageName = "لفه 400", PiecesPerWorkday = 5000, SortOrder = 4, IsActive = true },
                new ProductionStage { StageName = "دبله 600", PiecesPerWorkday = 5000, SortOrder = 5, IsActive = true },
                new ProductionStage { StageName = "بطن ناعم", PiecesPerWorkday = 5000, SortOrder = 6, IsActive = true },
                new ProductionStage { StageName = "لقه 600", PiecesPerWorkday = 5000, SortOrder = 7, IsActive = true },
                new ProductionStage { StageName = "لفه 800", PiecesPerWorkday = 5000, SortOrder = 8, IsActive = true },
            }});
            products.Add(new Product { Name = "ماجيك", IsActive = true, Stages = new List<ProductionStage> {
                new ProductionStage { StageName = "رايش", PiecesPerWorkday = 2500, SortOrder = 0, IsActive = true },
                new ProductionStage { StageName = "بطحتين 400", PiecesPerWorkday = 2000, SortOrder = 1, IsActive = true },
                new ProductionStage { StageName = "رقبه صغيره خشن", PiecesPerWorkday = 3333, SortOrder = 2, IsActive = true },
                new ProductionStage { StageName = "رقبه كبيره خشن", PiecesPerWorkday = 3333, SortOrder = 3, IsActive = true },
                new ProductionStage { StageName = "لفه صغيره 600", PiecesPerWorkday = 5000, SortOrder = 4, IsActive = true },
                new ProductionStage { StageName = "بطن خشن", PiecesPerWorkday = 5000, SortOrder = 5, IsActive = true },
                new ProductionStage { StageName = "رقبه ضربتين", PiecesPerWorkday = 2500, SortOrder = 6, IsActive = true },
                new ProductionStage { StageName = "بطحتين ناعم", PiecesPerWorkday = 2000, SortOrder = 7, IsActive = true },
                new ProductionStage { StageName = "رقبه صغيره ناعم", PiecesPerWorkday = 3333, SortOrder = 8, IsActive = true },
                new ProductionStage { StageName = "رقبه كبيره ناعم", PiecesPerWorkday = 3333, SortOrder = 9, IsActive = true },
                new ProductionStage { StageName = "رقبه صغيره ديق", PiecesPerWorkday = 2500, SortOrder = 10, IsActive = true },
                new ProductionStage { StageName = "رقبه ناعم ديق", PiecesPerWorkday = 2500, SortOrder = 11, IsActive = true },
                new ProductionStage { StageName = "بطن ناعم", PiecesPerWorkday = 5000, SortOrder = 12, IsActive = true },
                new ProductionStage { StageName = "لفه ناعم", PiecesPerWorkday = 5000, SortOrder = 13, IsActive = true },
            }});
            products.Add(new Product { Name = "طقم عقله", IsActive = true, Stages = new List<ProductionStage> {
                new ProductionStage { StageName = "600", PiecesPerWorkday = 5000, SortOrder = 0, IsActive = true },
                new ProductionStage { StageName = "800", PiecesPerWorkday = 5000, SortOrder = 1, IsActive = true },
                new ProductionStage { StageName = "1000", PiecesPerWorkday = 5000, SortOrder = 2, IsActive = true },
                new ProductionStage { StageName = "1000", PiecesPerWorkday = 5000, SortOrder = 3, IsActive = true },
                new ProductionStage { StageName = "لمعه", PiecesPerWorkday = 5000, SortOrder = 4, IsActive = true },
                new ProductionStage { StageName = "تربيط", PiecesPerWorkday = 5000, SortOrder = 5, IsActive = true },
            }});
            products.Add(new Product { Name = "كوع بسن", IsActive = true, Stages = new List<ProductionStage> {
                new ProductionStage { StageName = "عريض خشن", PiecesPerWorkday = 2600, SortOrder = 0, IsActive = true },
                new ProductionStage { StageName = "ديق خشن", PiecesPerWorkday = 2600, SortOrder = 1, IsActive = true },
                new ProductionStage { StageName = "عريض ناعم", PiecesPerWorkday = 2600, SortOrder = 2, IsActive = true },
                new ProductionStage { StageName = "ديق ناعم", PiecesPerWorkday = 2600, SortOrder = 3, IsActive = true },
                new ProductionStage { StageName = "عريض لمعه", PiecesPerWorkday = 1666, SortOrder = 4, IsActive = true },
            }});
            products.Add(new Product { Name = "وصله تي", IsActive = true, Stages = new List<ProductionStage> {
                new ProductionStage { StageName = "بطن خشن", PiecesPerWorkday = 3333, SortOrder = 0, IsActive = true },
                new ProductionStage { StageName = "3 لفات خشن", PiecesPerWorkday = 1666, SortOrder = 1, IsActive = true },
                new ProductionStage { StageName = "ديق خشن", PiecesPerWorkday = 1666, SortOrder = 2, IsActive = true },
                new ProductionStage { StageName = "3 لفات ناعم", PiecesPerWorkday = 1666, SortOrder = 3, IsActive = true },
                new ProductionStage { StageName = "ديق ناعم", PiecesPerWorkday = 1666, SortOrder = 4, IsActive = true },
                new ProductionStage { StageName = "بطن 600", PiecesPerWorkday = 5000, SortOrder = 5, IsActive = true },
                new ProductionStage { StageName = "بطن 800", PiecesPerWorkday = 5000, SortOrder = 6, IsActive = true },
                new ProductionStage { StageName = "بطن 1000", PiecesPerWorkday = 5000, SortOrder = 7, IsActive = true },
            }});
            products.Add(new Product { Name = "حنفيه بزبوز", IsActive = true, Stages = new List<ProductionStage> {
                new ProductionStage { StageName = "مسدس", PiecesPerWorkday = 1666, SortOrder = 0, IsActive = true },
                new ProductionStage { StageName = "ضربتين ولفه", PiecesPerWorkday = 2500, SortOrder = 1, IsActive = true },
                new ProductionStage { StageName = "ديق رقبه", PiecesPerWorkday = 2500, SortOrder = 2, IsActive = true },
                new ProductionStage { StageName = "ديق بوز", PiecesPerWorkday = 2500, SortOrder = 3, IsActive = true },
                new ProductionStage { StageName = "بطن", PiecesPerWorkday = 2500, SortOrder = 4, IsActive = true },
                new ProductionStage { StageName = "عريض", PiecesPerWorkday = 2500, SortOrder = 5, IsActive = true },
                new ProductionStage { StageName = "بوز", PiecesPerWorkday = 2500, SortOrder = 6, IsActive = true },
                new ProductionStage { StageName = "لفه لمعه", PiecesPerWorkday = 1666, SortOrder = 7, IsActive = true },
                new ProductionStage { StageName = "عريض لمعه", PiecesPerWorkday = 1666, SortOrder = 8, IsActive = true },
                new ProductionStage { StageName = "بوز لمعه", PiecesPerWorkday = 1666, SortOrder = 9, IsActive = true },
            }});
            products.Add(new Product { Name = "كبشه ستار", IsActive = true, Stages = new List<ProductionStage> {
                new ProductionStage { StageName = "6 ضربات", PiecesPerWorkday = 1000, SortOrder = 0, IsActive = true },
                new ProductionStage { StageName = "لفه", PiecesPerWorkday = 2500, SortOrder = 1, IsActive = true },
                new ProductionStage { StageName = "وش", PiecesPerWorkday = 2500, SortOrder = 2, IsActive = true },
                new ProductionStage { StageName = "رايش", PiecesPerWorkday = 5000, SortOrder = 3, IsActive = true },
            }});
            products.Add(new Product { Name = "كبشه مروحه", IsActive = true, Stages = new List<ProductionStage> {
                new ProductionStage { StageName = "رايش", PiecesPerWorkday = 5000, SortOrder = 0, IsActive = true },
                new ProductionStage { StageName = "رايش وش", PiecesPerWorkday = 2500, SortOrder = 1, IsActive = true },
                new ProductionStage { StageName = "لفه", PiecesPerWorkday = 2500, SortOrder = 2, IsActive = true },
                new ProductionStage { StageName = "صوبع", PiecesPerWorkday = 2500, SortOrder = 3, IsActive = true },
                new ProductionStage { StageName = "بطن", PiecesPerWorkday = 3333, SortOrder = 4, IsActive = true },
            }});
            products.Add(new Product { Name = "كبشه مثلثه", IsActive = true, Stages = new List<ProductionStage> {
                new ProductionStage { StageName = "رايش", PiecesPerWorkday = 5000, SortOrder = 0, IsActive = true },
                new ProductionStage { StageName = "رايش وش", PiecesPerWorkday = 2500, SortOrder = 1, IsActive = true },
                new ProductionStage { StageName = "لفه", PiecesPerWorkday = 2500, SortOrder = 2, IsActive = true },
                new ProductionStage { StageName = "بطن", PiecesPerWorkday = 2500, SortOrder = 3, IsActive = true },
                new ProductionStage { StageName = "وش", PiecesPerWorkday = 2500, SortOrder = 4, IsActive = true },
            }});
            products.Add(new Product { Name = "كبشه تاج", IsActive = true, Stages = new List<ProductionStage> {
                new ProductionStage { StageName = "رايش", PiecesPerWorkday = 5000, SortOrder = 0, IsActive = true },
                new ProductionStage { StageName = "لفه خشن", PiecesPerWorkday = 2500, SortOrder = 1, IsActive = true },
                new ProductionStage { StageName = "لفه ناعم", PiecesPerWorkday = 2500, SortOrder = 2, IsActive = true },
                new ProductionStage { StageName = "بطن", PiecesPerWorkday = 2500, SortOrder = 3, IsActive = true },
                new ProductionStage { StageName = "وش", PiecesPerWorkday = 2000, SortOrder = 4, IsActive = true },
            }});
            products.Add(new Product { Name = "كبشه مشتمل", IsActive = true, Stages = new List<ProductionStage> {
                new ProductionStage { StageName = "رايش", PiecesPerWorkday = 5000, SortOrder = 0, IsActive = true },
                new ProductionStage { StageName = "رايش وش", PiecesPerWorkday = 2500, SortOrder = 1, IsActive = true },
                new ProductionStage { StageName = "لفه 400", PiecesPerWorkday = 2500, SortOrder = 2, IsActive = true },
                new ProductionStage { StageName = "لفه 600", PiecesPerWorkday = 2500, SortOrder = 3, IsActive = true },
                new ProductionStage { StageName = "لفه 800", PiecesPerWorkday = 2500, SortOrder = 4, IsActive = true },
                new ProductionStage { StageName = "بطن 400", PiecesPerWorkday = 2500, SortOrder = 5, IsActive = true },
                new ProductionStage { StageName = "بطن 600", PiecesPerWorkday = 2500, SortOrder = 6, IsActive = true },
                new ProductionStage { StageName = "وش", PiecesPerWorkday = 2500, SortOrder = 7, IsActive = true },
            }});
            products.Add(new Product { Name = "كبشه جلو", IsActive = true, Stages = new List<ProductionStage> {
                new ProductionStage { StageName = "رايش", PiecesPerWorkday = 5000, SortOrder = 0, IsActive = true },
                new ProductionStage { StageName = "رايش وش", PiecesPerWorkday = 2500, SortOrder = 1, IsActive = true },
                new ProductionStage { StageName = "لفه", PiecesPerWorkday = 2500, SortOrder = 2, IsActive = true },
                new ProductionStage { StageName = "بطن", PiecesPerWorkday = 2500, SortOrder = 3, IsActive = true },
                new ProductionStage { StageName = "وش", PiecesPerWorkday = 2500, SortOrder = 4, IsActive = true },
            }});
            products.Add(new Product { Name = "كبشه الماني", IsActive = true, Stages = new List<ProductionStage> {
                new ProductionStage { StageName = "رايش", PiecesPerWorkday = 5000, SortOrder = 0, IsActive = true },
                new ProductionStage { StageName = "الكبشه كامله", PiecesPerWorkday = 1000, SortOrder = 1, IsActive = true },
            }});
            products.Add(new Product { Name = "طبق مشتمل", IsActive = true, Stages = new List<ProductionStage> {
                new ProductionStage { StageName = "رايش", PiecesPerWorkday = 5000, SortOrder = 0, IsActive = true },
                new ProductionStage { StageName = "وش 400", PiecesPerWorkday = 5000, SortOrder = 1, IsActive = true },
                new ProductionStage { StageName = "وش 600", PiecesPerWorkday = 5000, SortOrder = 2, IsActive = true },
                new ProductionStage { StageName = "وش 600 زيت", PiecesPerWorkday = 5000, SortOrder = 3, IsActive = true },
                new ProductionStage { StageName = "لمعه+تطويق", PiecesPerWorkday = 600, SortOrder = 4, IsActive = true },
            }});
            products.Add(new Product { Name = "وصله مشتمل", IsActive = true, Stages = new List<ProductionStage> {
                new ProductionStage { StageName = "لفه 400", PiecesPerWorkday = 6400, SortOrder = 0, IsActive = true },
                new ProductionStage { StageName = "لفه 600", PiecesPerWorkday = 6400, SortOrder = 1, IsActive = true },
                new ProductionStage { StageName = "لفه 600 زيت", PiecesPerWorkday = 6400, SortOrder = 2, IsActive = true },
            }});

            return products;
        }

        /// <summary>
        /// كل عامل ومعاه كوده في ملف البيانات الأصلي.
        ///
        /// الكود ده **مش بيتخزّن في الداتابيز** — هو معرّف داخلي للبذرة
        /// بس، عشان WorkerSkillsSeed تقدر تشاور على العامل بحاجة قصيرة
        /// بدل اسم عربي من خمس كلمات في 38 مكان. الربط الفعلي بيتم
        /// بالاسم عن طريق <see cref="NameByCode"/>.
        /// </summary>
        private static List<(string Code, Worker Worker)> BuildRoster()
        {
            var roster = new List<(string Code, Worker Worker)>();

            roster.Add(("W001", new Worker { FullName = "مصطفى محمد مهدي عبد الحفيظ محمود", SkillsNotes = null, IsActive = true }));
            roster.Add(("W002", new Worker { FullName = "حسن حسين حسن حسين على", SkillsNotes = "جميع مراحل صنفره المحابس و اللوازم  /لمعه لوازم", IsActive = true }));
            roster.Add(("W003", new Worker { FullName = "ابوزيد عبدالله السيد عبدالله", SkillsNotes = "جميع مراحل صنفره المحابس و اللوازم", IsActive = true }));
            roster.Add(("W004", new Worker { FullName = "اشرف على بدوى", SkillsNotes = "جميع مراحل صنفره المحابس و اللوازم", IsActive = true }));
            roster.Add(("W005", new Worker { FullName = "ياسر كامل صوفان", SkillsNotes = "محبس GRS لفه صغيره/رقبه/دبله /بطن/بطحتين/كوع بسن كامل/وصله تي عريض خشن وناعم/محبس MG بطن ناعم /رقبه/دبله/حنفيه خلفي/محبس ماجيك كامل", IsActive = true }));
            roster.Add(("W006", new Worker { FullName = "علي بسيوني السيد بسيوني", SkillsNotes = "طقم عقله/دبله محبس", IsActive = true }));
            roster.Add(("W007", new Worker { FullName = "تامر جاد محمد علي", SkillsNotes = "جميع مراحل المحبس GRS ماعدا البطحتين/محبس MG ضيق / بطن/ضربه/دبلتين/رقبه/جميع مراحل اللوازم/ لمعه/محبس ماجيك لفات", IsActive = true }));
            roster.Add(("W008", new Worker { FullName = "حسام محمد عبد الجواد عبد الله", SkillsNotes = "جميع مراحل اللوازم/جميع مراحل المحبس GRS ماعدا البطحتين/محبس ماجيك لفات /بطن", IsActive = true }));
            roster.Add(("W009", new Worker { FullName = "خيري فراج احمد شحاتة", SkillsNotes = "جميع مراحل المحبس GRS ماعدا البطحتين/محبس MG ضيق / بطن/ضربه/دبلتين/رقبه/جميع مراحل الكوع/ وصله تي بطن و لفات/جميع مراحل ماجيك ماعدا البطحتين", IsActive = true }));
            roster.Add(("W010", new Worker { FullName = "عمرو عبد المنعم عبد القادر محمد", SkillsNotes = "جميع مراحل الكوبشه جيد", IsActive = true }));
            roster.Add(("W011", new Worker { FullName = "يوسف محمد علي عبد العاطي", SkillsNotes = "جميع مراحل الكوبشه جيد جدا", IsActive = true }));
            roster.Add(("W012", new Worker { FullName = "اسامة محمد الحسيني احمد الرفاعي", SkillsNotes = "جميع مراحل الكوبشه ممتاز", IsActive = true }));
            roster.Add(("W013", new Worker { FullName = "احمد محمد محمود الصاوي", SkillsNotes = "جميع مراحل الكوبشه ممتاز", IsActive = true }));
            roster.Add(("W014", new Worker { FullName = "صابر عبد المنعم عبد الحافظ حراز", SkillsNotes = "جميع مراحل الكوبشه جيد", IsActive = true }));
            roster.Add(("W015", new Worker { FullName = "محمد عادل ابراهيم محمد", SkillsNotes = "جميع مراحل صنفره المحابس و اللوازم", IsActive = true }));
            roster.Add(("W016", new Worker { FullName = "عبدالسلام عابدين عبد السلام", SkillsNotes = "دبله جميع المحبس/كوع بسن رايش/لمعه كتان/محبس ماجيك لفات", IsActive = true }));
            roster.Add(("W017", new Worker { FullName = "اسلام سعد الدين محمد", SkillsNotes = "جميع مراحل محبس GRS ماعدا البطحتين/ محبس MG لفه /رقبه/ضربه/محبس ماجيك بطم/ لفات", IsActive = true }));
            roster.Add(("W018", new Worker { FullName = "محمود محمد محمود", SkillsNotes = "لمعه /محبس GRS بطن خشن وناعم/دبلتين/ضربتين/لفه 400/600", IsActive = true }));
            roster.Add(("W019", new Worker { FullName = "محمد جمال مصطفى", SkillsNotes = "جميع مراحل محبس ماجيك/كوع بسن رايش/وصله تي 3 لفات/محبس GRS  دبله/رقبه/بطن/ضربتين/لفه 400/600/محبس MG دبله/ضربه/عريض/بطن 600", IsActive = true }));
            roster.Add(("W020", new Worker { FullName = "اشرف محمد اسماعيل", SkillsNotes = "لمعه /محبس GRS بطن خشن وناعم/دبله/كوع بسن رايش/تي بطن خشن /3لفات", IsActive = true }));
            roster.Add(("W021", new Worker { FullName = "محمد مصطفى احمد", SkillsNotes = "عامل تحت التدريب", IsActive = true }));
            roster.Add(("W022", new Worker { FullName = "عبدالله احمد محمد", SkillsNotes = "جميع المحابس رقبه /دبله", IsActive = true }));
            roster.Add(("W023", new Worker { FullName = "رجب حسان محمد", SkillsNotes = "لمعه لوازم/ كوع بسن رايش", IsActive = true }));
            roster.Add(("W024", new Worker { FullName = "يوسف احمد عبدالصمد", SkillsNotes = "جميع مراحل اللوازم/جميع مراحل محبس MG /محبس GRS  دبله/رقبه/بطن/ضربتين/لفه 400/600 /بطحتين", IsActive = true }));
            roster.Add(("W025", new Worker { FullName = "حمدي عبداللاه موسى", SkillsNotes = "طقم عقله/دبله محبس", IsActive = true }));
            roster.Add(("W026", new Worker { FullName = "جمال الصدام جمال", SkillsNotes = "رايش كبشه/رايش وش", IsActive = true }));
            roster.Add(("W027", new Worker { FullName = "محمود مصطفى احمد", SkillsNotes = "طقم عقله", IsActive = true }));
            roster.Add(("W028", new Worker { FullName = "خالد سعيد عوض", SkillsNotes = "طقم عقله", IsActive = true }));
            roster.Add(("W029", new Worker { FullName = "علي عادل عبدالغفار", SkillsNotes = "جميع مراحل الكوبشه جيد جدا", IsActive = true }));
            roster.Add(("W030", new Worker { FullName = "احمد عبدالعليم محمد", SkillsNotes = "جميع مراحل الكوبشه جيد جدا", IsActive = true }));
            roster.Add(("W031", new Worker { FullName = "ابراهيم علي محمد", SkillsNotes = "جميع مراحل الكوبشه جيد جدا", IsActive = true }));
            roster.Add(("W032", new Worker { FullName = "اسماعيل محمد اسماعيل", SkillsNotes = "جميع مراحل محبس ماجيك/كوع بسن رايش/وصله تي 3 لفات/محبس GRS  دبله/بطن", IsActive = true }));
            roster.Add(("W033", new Worker { FullName = "وليد احمد محمد", SkillsNotes = "جميع مراحل اللوازم/محبس GRS لفه 800/جميع مراحل محبس ماجيك", IsActive = true }));
            roster.Add(("W034", new Worker { FullName = "بدر عبدالعزيز السيد", SkillsNotes = "جميع مراحل اللوازم/محبس GRS لفه 600/جميع مراحل محبس ماجيك", IsActive = true }));
            roster.Add(("W035", new Worker { FullName = "احمد عاطف خيري", SkillsNotes = "محبس GRS لفه صغيره/رقبه/دبله /بطن/محبس MG بطن 400 /رقبه/دبله", IsActive = true }));
            roster.Add(("W036", new Worker { FullName = "عماد شعبان حريمز", SkillsNotes = "طقم عقله", IsActive = true }));
            roster.Add(("W037", new Worker { FullName = "عبدالرحمن صابر عبدالمنعم", SkillsNotes = "عامل تحت التدريب", IsActive = true }));
            roster.Add(("W038", new Worker { FullName = "احمد مرزوق", SkillsNotes = "عامل تحت التدريب", IsActive = true }));
            roster.Add(("W039", new Worker { FullName = "رمضان خميس", SkillsNotes = "عامل تحت التدريب", IsActive = true }));
            roster.Add(("W040", new Worker { FullName = "يوسف محمد حسب", SkillsNotes = "عامل رص", IsActive = true }));
            roster.Add(("W041", new Worker { FullName = "مروان سالم شحات", SkillsNotes = "عامل جوده", IsActive = true }));
            roster.Add(("W042", new Worker { FullName = "الحسن علي الجنبيهي", SkillsNotes = "عامل تحت التدريب", IsActive = true }));
            roster.Add(("W043", new Worker { FullName = "مصطفى محمود فهيم", SkillsNotes = "كوع بسن عريض/ رايش/وصله تي بطن/3لفات/جميع المحابس دبله/رقبه/ضربتين/محبس ماجيك بطن/لفات", IsActive = true }));
            roster.Add(("W044", new Worker { FullName = "سلامه تامر سلامه", SkillsNotes = "طقم عقله/دبله محبس", IsActive = true }));
            roster.Add(("W045", new Worker { FullName = "زياد عبدالرازق", SkillsNotes = "جميع مراحل محبس GRS ماعدا البطحتين/كوع بسن رايش/لمعه/وصله تي 3لفات /محبس ماجيك لفات/ بطن/محبس MG ضربه / دبله/لفه", IsActive = true }));
            roster.Add(("W046", new Worker { FullName = "زياد محمود محمد", SkillsNotes = "طقم عقله/دبله محبس", IsActive = true }));

            return roster;
        }

        /// <summary>العمال زي ما بيتخزّنوا في الداتابيز — من غير أي كود</summary>
        public static List<Worker> BuildWorkers() =>
            BuildRoster().Select(r => r.Worker).ToList();

        /// <summary>
        /// كود البذرة → اسم العامل. مشتقة من نفس القايمة، فمستحيل
        /// الاتنين يفرقوا عن بعض.
        /// </summary>
        public static Dictionary<string, string> NameByCode() =>
            BuildRoster().ToDictionary(r => r.Code, r => r.Worker.FullName);
    }
}