using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// نقلة البيانات من المسار القديم للمسار المشترك.
    ///
    /// النسخ اللي قبل ملف التثبيت كانت بتحط قاعدة البيانات في مجلد
    /// المستخدم (%LocalAppData%). النسخة المثبَّتة بقت تحطها في المجلد
    /// المشترك (%ProgramData%) عشان تعيش بعيد عن مجلد البرنامج — اللي
    /// بيتمسح ويتكتب من أول وجديد مع كل ترقية.
    ///
    /// المشكلة اللي الاختبارات دي بتقفلها: من غير نقلة، أي جهاز شغّال على
    /// المسار القديم هيفتح النسخة الجديدة ويلاقيها **فاضية** — البيانات
    /// موجودة على الهارد بس البرنامج بيدوّر في مكان تاني، والمستخدم مش
    /// هيبقى عنده أي طريقة يفهم إيه اللي حصل.
    ///
    /// والخطر التاني في الاتجاه المضاد: نقلة بتشتغل أكتر من مرة بتدوس
    /// على شغل المستخدم بنسخة قديمة. عشان كده الشرط "المسار الجديد فيه
    /// قاعدة بيانات؟ يبقى مفيش نقلة" مغطّى هنا بأكتر من اختبار.
    /// </summary>
    public class AppPathsTests : IDisposable
    {
        private readonly List<string> _folders = new();

        public void Dispose()
        {
            foreach (var folder in _folders)
                try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { /* مؤقت */ }
        }

        private string NewFolder(bool create = true)
        {
            var folder = Path.Combine(Path.GetTempPath(), $"wm-paths-{Guid.NewGuid():N}");
            if (create) Directory.CreateDirectory(folder);
            _folders.Add(folder);
            return folder;
        }

        private static void Write(string folder, string name, string content) =>
            File.WriteAllText(Path.Combine(folder, name), content);

        private static string Read(string folder, string name) =>
            File.ReadAllText(Path.Combine(folder, name));

        [Fact]
        public void TheDatabaseMovesToTheNewFolder_WhenItIsEmpty()
        {
            var legacy = NewFolder();
            var target = NewFolder();

            Write(legacy, "workforce.db", "بيانات العميل");

            Assert.True(AppPaths.MigrateLegacyData(legacy, target));
            Assert.Equal("بيانات العميل", Read(target, "workforce.db"));
        }

        [Fact]
        public void TheWalAndShmFilesMoveWithTheDatabase()
        {
            // SQLite شغال في وضع WAL: آخر عمليات مكتوبة ممكن تكون لسه في
            // ملف -wal لوحده. نسخ الـ .db بس بينقل قاعدة **ناقصة** —
            // ونقص من غير رسالة خطأ، وده أسوأ نوع فقد.
            var legacy = NewFolder();
            var target = NewFolder();

            Write(legacy, "workforce.db", "الأساسي");
            Write(legacy, "workforce.db-wal", "آخر العمليات");
            Write(legacy, "workforce.db-shm", "فهرس مشترك");

            AppPaths.MigrateLegacyData(legacy, target);

            Assert.Equal("آخر العمليات", Read(target, "workforce.db-wal"));
            Assert.Equal("فهرس مشترك", Read(target, "workforce.db-shm"));
        }

        [Fact]
        public void TheSettingsFileMovesToo()
        {
            // مجلد النسخ الخارجي ومدد الاحتفاظ واسم المصنع كلهم في الملف
            // ده — لو اتساب ورا، المستخدم هيلاقي إعداداته اترجّعت للافتراضي
            var legacy = NewFolder();
            var target = NewFolder();

            Write(legacy, "workforce.db", "قاعدة");
            Write(legacy, "settings.json", "{\"DarkMode\":true}");

            AppPaths.MigrateLegacyData(legacy, target);

            Assert.Equal("{\"DarkMode\":true}", Read(target, "settings.json"));
        }

        [Fact]
        public void TheOldFolderIsLeftUntouched_SoThereIsAWayBack()
        {
            var legacy = NewFolder();
            var target = NewFolder();

            Write(legacy, "workforce.db", "بيانات العميل");

            AppPaths.MigrateLegacyData(legacy, target);

            Assert.True(File.Exists(Path.Combine(legacy, "workforce.db")));
        }

        [Fact]
        public void NothingMoves_WhenTheNewFolderAlreadyHasADatabase()
        {
            // ده أهم اختبار في الملف: التحديث بيشغّل القرار ده كل مرة
            // البرنامج بيفتح. لو النقلة اشتغلت تاني، شغل المستخدم بيتمسح
            // بقاعدة قديمة من غير أي إنذار.
            var legacy = NewFolder();
            var target = NewFolder();

            Write(legacy, "workforce.db", "قديمة");
            Write(target, "workforce.db", "شغل المستخدم الحالي");

            Assert.False(AppPaths.MigrateLegacyData(legacy, target));
            Assert.Equal("شغل المستخدم الحالي", Read(target, "workforce.db"));
        }

        [Fact]
        public void NothingMoves_WhenTheOldFolderHasNoDatabase()
        {
            // التثبيت النضيف: مفيش مسار قديم أصلاً
            var legacy = NewFolder();
            var target = NewFolder();

            Assert.False(AppPaths.MigrateLegacyData(legacy, target));
            Assert.Empty(Directory.GetFiles(target));
        }

        [Fact]
        public void NothingMoves_WhenTheOldFolderDoesNotExistAtAll()
        {
            var legacy = NewFolder(create: false);
            var target = NewFolder();

            Assert.False(AppPaths.MigrateLegacyData(legacy, target));
        }

        [Fact]
        public void TheNewFolderIsCreated_IfItIsNotThereYet()
        {
            var legacy = NewFolder();
            var target = NewFolder(create: false);

            Write(legacy, "workforce.db", "بيانات");

            Assert.True(AppPaths.MigrateLegacyData(legacy, target));
            Assert.True(File.Exists(Path.Combine(target, "workforce.db")));
        }

        [Fact]
        public void WithoutAPortableMarker_TheDataLivesOutsideTheProgramFolder()
        {
            // القاعدة اللي التثبيت كله قايم عليها: مجلد البرنامج بيتمسح
            // ويتكتب من أول وجديد مع كل ترقية، والمستخدم العادي ممنوع يكتب
            // فيه لما يبقى في Program Files. أي رجوع للسلوك القديم معناه
            // فقد بيانات عند أول تحديث — من غير رسالة، ومن غير رجعة.
            var exeDir = NewFolder();
            var shared = NewFolder(create: false);

            var resolved = AppPaths.ResolveDataFolderFor(exeDir, shared);

            Assert.Equal(shared, resolved);
            Assert.False(resolved.StartsWith(exeDir, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void WithAPortableMarker_TheDataStaysNextToTheProgram()
        {
            // النسخة المحمولة لسه شغالة زي ما هي: مجلد كامل تنقله على
            // فلاشة ويشتغل ببياناته من غير تثبيت
            var exeDir = NewFolder();
            var shared = NewFolder(create: false);

            File.WriteAllText(Path.Combine(exeDir, "portable.marker"), "");

            Assert.Equal(
                Path.Combine(exeDir, "Data"),
                AppPaths.ResolveDataFolderFor(exeDir, shared));
        }

        [Fact]
        public void TheSharedRootIsProgramData_NotPerWindowsUser()
        {
            // مشترك عن قصد: لو المدير والمحاسب بيدخلوا الجهاز بحسابين
            // ويندوز مختلفين، لازم يشوفوا نفس البيانات — مش كل واحد مصنع
            // لوحده.
            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "WorkforceManager");

            Assert.Equal(expected, AppPaths.SharedDataRoot);
            Assert.NotEqual(AppPaths.LegacyDataRoot, AppPaths.SharedDataRoot);
        }
    }
}
