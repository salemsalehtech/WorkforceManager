using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// خانات اتشالت من الواجهة (أو من القاعدة خالص): "كود المنتج"،
    /// "ملاحظات المهارات"، "صورة المنتج"، وصورة عامل الإنتاج العادي.
    ///
    /// الاختبارات دي بتحرس **الفرق بينهم**:
    ///   • كود المنتج وصورة المنتج اتمسحوا من الداتابيز خالص (مكانش ليهم أي مستخدم)
    ///   • ملاحظات المهارات فضلت مخزّنة، لأن DatabaseSeeder بيقرا منها
    ///     تصنيف عمال الساعة والبحث بيدوّر جواها
    ///   • صورة العامل فضلت مخزّنة بس بقت حكرًا على الحسابات الإدارية
    ///     (مدير/رئيس قسم) — عامل الإنتاج العادي اتشالت صورته من الفورم
    ///     **و**اتفرضت القاعدة في الطبقة كمان (SetWorkerPhotoAsync)
    ///
    /// أهم واحد فيهم: **تعديل عامل مبيمسحش ملاحظاته أو صورته**. الخانات
    /// اتشالت من الفورم، فلو الخدمة فضلت بتكتب القيمة اللي جاية منها كان
    /// أول تعديل لأي عامل هيصفّر بيانات محتاجينها.
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

        // ======================= صورة العامل: حكرًا على الحسابات الإدارية =======================
        //
        // بعد فيتشر "إزالة الصور": الصورة بقت مالهاش لازمة إلا للحسابات
        // الإدارية (مدير/رئيس قسم). عامل الإنتاج العادي (بالقطعة أو
        // بالساعة، زي WorkerAhmedId) اتشالت صورته من كل الفورمات ومن
        // القاعدة، والقاعدة دي متفروضة في الطبقة دي (SetWorkerPhotoAsync)
        // مش بس بإخفاء الفورم — عشان أي استدعاء تاني (أو فورم قديم
        // متبنيش) ميعديش من غيرها.

        [Fact]
        public async Task SetWorkerPhotoAsync_throws_for_a_regular_production_worker()
        {
            // WorkerAhmedId عامل بالقطعة (HourlyRole = null) — مش حساب إداري
            using var scope = _db.CreateScope();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.GetService<WorkerManagementService>(scope)
                    .SetWorkerPhotoAsync(TestDatabase.WorkerAhmedId, new byte[] { 1, 2, 3 }));
        }

        [Fact]
        public async Task SetWorkerPhotoAsync_throws_for_an_hourly_worker_that_is_not_a_department_account()
        {
            // WorkerMonaHourlyId بالساعة (دور رص) بس برضه مش حساب إداري
            using var scope = _db.CreateScope();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.GetService<WorkerManagementService>(scope)
                    .SetWorkerPhotoAsync(TestDatabase.WorkerMonaHourlyId, new byte[] { 1, 2, 3 }));
        }

        [Fact]
        public async Task Department_account_photo_round_trips()
        {
            var photo = new byte[] { 1, 2, 3, 4 };

            int managerId;
            using (var scope = _db.CreateScope())
                managerId = (await _db.GetService<WorkerManagementService>(scope)
                    .CreateWorkerAsync("مدير قسم تجريبي", hourlyRole: HourlyRole.DepartmentManager)).Id;

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkerManagementService>(scope).SetWorkerPhotoAsync(managerId, photo);

            using var check = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(check);
            var manager = await db.Workers.SingleAsync(w => w.Id == managerId);

            Assert.Equal(photo, manager.PhotoData);
        }

        [Fact]
        public async Task Clearing_a_department_account_photo_stores_null_not_an_empty_array()
        {
            int managerId;
            using (var scope = _db.CreateScope())
                managerId = (await _db.GetService<WorkerManagementService>(scope)
                    .CreateWorkerAsync("مدير قسم تجريبي", hourlyRole: HourlyRole.DepartmentManager)).Id;

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkerManagementService>(scope)
                    .SetWorkerPhotoAsync(managerId, Array.Empty<byte>());

            using var check = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(check);
            var manager = await db.Workers.SingleAsync(w => w.Id == managerId);

            // مصفوفة فاضية ومفيش صورة نفس المعنى — تخزينهم بشكلين
            // كان هيخلي "عنده صورة؟" تجاوب غلط
            Assert.Null(manager.PhotoData);
        }

        [Fact]
        public async Task Editing_a_department_account_does_not_clear_their_photo()
        {
            var photo = new byte[] { 9, 8, 7 };

            int managerId;
            using (var scope = _db.CreateScope())
                managerId = (await _db.GetService<WorkerManagementService>(scope)
                    .CreateWorkerAsync("مدير قسم تجريبي", hourlyRole: HourlyRole.DepartmentManager)).Id;

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkerManagementService>(scope).SetWorkerPhotoAsync(managerId, photo);

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkerManagementService>(scope)
                    .UpdateWorkerAsync(managerId, "مدير قسم بعد التعديل",
                        hourlyRole: HourlyRole.DepartmentManager, dailyWageEgp: 300m);

            using var check = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(check);
            var manager = await db.Workers.SingleAsync(w => w.Id == managerId);

            Assert.Equal(photo, manager.PhotoData);
        }

        // ======================= صورة المنتج: اتشالت خالص =======================

        [Fact]
        public async Task Product_image_column_is_gone_from_the_database()
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

            Assert.DoesNotContain("ImageData", columns);
            Assert.Contains("Name", columns); // الجدول نفسه سليم
        }
    }
}
