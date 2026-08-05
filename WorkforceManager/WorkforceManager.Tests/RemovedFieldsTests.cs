using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.Services;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// خانتين اتشالوا من الواجهة: "كود المنتج" و"ملاحظات المهارات".
    ///
    /// الاختبارات دي بتحرس **الفرق بينهم**:
    ///   • كود المنتج اتمسح من الداتابيز خالص (مكانش ليه أي مستخدم)
    ///   • ملاحظات المهارات فضلت مخزّنة، لأن DatabaseSeeder بيقرا منها
    ///     تصنيف عمال الساعة والبحث بيدوّر جواها
    ///
    /// أهم واحد فيهم: **تعديل عامل مبيمسحش ملاحظاته**. الخانة اتشالت من
    /// الفورم، فلو الخدمة فضلت بتكتب القيمة اللي جاية منها كان أول تعديل
    /// لأي عامل هيصفّر بيانات محتاجينها.
    /// </summary>
    public class RemovedFieldsTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        // ======================= ملاحظات المهارات =======================

        [Fact]
        public async Task Editing_a_worker_never_touches_their_skills_notes()
        {
            const string notes = "عامل تحت التدريب";

            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                var worker = await db.Workers.SingleAsync(w => w.Id == TestDatabase.WorkerAhmedId);
                worker.SkillsNotes = notes;
                await db.SaveChangesAsync();
            }

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkerManagementService>(scope)
                    .UpdateWorkerAsync(TestDatabase.WorkerAhmedId, "أحمد بعد التعديل",
                        phoneNumber: "0100", dailyWageEgp: 250m);

            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                var worker = await db.Workers.SingleAsync(w => w.Id == TestDatabase.WorkerAhmedId);

                Assert.Equal("أحمد بعد التعديل", worker.FullName); // التعديل اشتغل
                Assert.Equal(notes, worker.SkillsNotes);           // والملاحظات زي ما هي
            }
        }

        [Fact]
        public async Task A_new_worker_is_created_with_no_notes()
        {
            // الخانة مبقتش موجودة في الفورم، فمفيش حد بيكتب فيها من دلوقتي
            using var scope = _db.CreateScope();
            var created = await _db.GetService<WorkerManagementService>(scope)
                .CreateWorkerAsync("عامل جديد");

            Assert.Null(created.SkillsNotes);
        }

        [Fact]
        public async Task Skills_notes_column_still_exists_for_the_seeder()
        {
            // DatabaseSeeder.SeedHourlyRolesAsync بيقرا منه كل تشغيل عشان
            // يصنّف عمال الرص/الجودة/التدريب. لو العمود اتمسح، أي تركيب
            // جديد هيطلع من غير تصنيف
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);

            var canQuery = await db.Workers.AnyAsync(w => w.SkillsNotes == null);
            Assert.True(canQuery);
        }

        // ======================= كود المنتج =======================

        [Fact]
        public async Task Creating_a_product_takes_a_name_and_description_only()
        {
            using var scope = _db.CreateScope();
            var created = await _db.GetService<ProductManagementService>(scope)
                .CreateProductAsync("منتج جديد", "وصف");

            Assert.Equal("منتج جديد", created.Name);
            Assert.Equal("وصف", created.Description);
        }

        [Fact]
        public async Task Product_code_column_is_gone_from_the_database()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);

            var columns = new List<string>();
            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT name FROM pragma_table_info('Products')";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
            }

            Assert.DoesNotContain("ProductCode", columns);
            Assert.Contains("Name", columns); // الجدول نفسه سليم
        }

        [Fact]
        public async Task Worker_photo_column_exists_and_round_trips()
        {
            var photo = new byte[] { 1, 2, 3, 4 };

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkerManagementService>(scope)
                    .SetWorkerPhotoAsync(TestDatabase.WorkerAhmedId, photo);

            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                var worker = await db.Workers.SingleAsync(w => w.Id == TestDatabase.WorkerAhmedId);

                Assert.Equal(photo, worker.PhotoData);
            }
        }

        [Fact]
        public async Task Clearing_a_worker_photo_stores_null_not_an_empty_array()
        {
            using (var scope = _db.CreateScope())
                await _db.GetService<WorkerManagementService>(scope)
                    .SetWorkerPhotoAsync(TestDatabase.WorkerAhmedId, Array.Empty<byte>());

            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                var worker = await db.Workers.SingleAsync(w => w.Id == TestDatabase.WorkerAhmedId);

                // مصفوفة فاضية ومفيش صورة نفس المعنى — تخزينهم بشكلين
                // كان هيخلي "عنده صورة؟" تجاوب غلط
                Assert.Null(worker.PhotoData);
            }
        }

        [Fact]
        public async Task Editing_a_worker_does_not_clear_their_photo()
        {
            var photo = new byte[] { 9, 8, 7 };

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkerManagementService>(scope)
                    .SetWorkerPhotoAsync(TestDatabase.WorkerAhmedId, photo);

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkerManagementService>(scope)
                    .UpdateWorkerAsync(TestDatabase.WorkerAhmedId, "أحمد", dailyWageEgp: 300m);

            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                var worker = await db.Workers.SingleAsync(w => w.Id == TestDatabase.WorkerAhmedId);

                Assert.Equal(photo, worker.PhotoData);
            }
        }
    }
}
