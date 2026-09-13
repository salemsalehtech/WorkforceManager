using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// كلمة سر العمليات بقت لكل حساب دخول لوحده — مش مشتركة للبرنامج
    /// كله زي قبل ميزة الحسابات الإدارية. اختبارات الأمان دي بتتأكد إن
    /// حسابين مختلفين معزولين تمامًا عن بعض.
    /// </summary>
    public class PerUserOperationsPasswordTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private async Task<int> AddLoginAsync(string username)
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            var user = new AppUser { Username = username, PasswordHash = "x", PasswordSalt = "x", DisplayName = username };
            db.AppUsers.Add(user);
            await db.SaveChangesAsync();
            return user.Id;
        }

        private void SignInAs(int appUserId, string username)
        {
            using var scope = _db.CreateScope();
            _db.GetService<CurrentUserContext>(scope).SignIn(username, username, appUserId);
        }

        [Fact]
        public async Task EachAccount_HasItsOwnOperationsPassword_IndependentOfTheOther()
        {
            var aliceId = await AddLoginAsync("alice");
            var bobId = await AddLoginAsync("bob");

            SignInAs(aliceId, "alice");
            using (var scope = _db.CreateScope())
                await _db.GetService<OperationsPasswordService>(scope).SetPasswordAsync(null, "alice-secret");

            SignInAs(bobId, "bob");
            using (var scope = _db.CreateScope())
                await _db.GetService<OperationsPasswordService>(scope).SetPasswordAsync(null, "bob-secret");

            // بوب يحاول يستخدم كلمة سر أليس — لازم يترفض
            using (var scope = _db.CreateScope())
            {
                var result = await _db.GetService<OperationsPasswordService>(scope)
                    .VerifyAsync(SensitiveAction.DeleteWorker, "alice-secret");
                Assert.False(result.IsAllowed);
            }

            // بوب بكلمة سره هو نفسه — لازم تعدّي
            using (var scope = _db.CreateScope())
            {
                var result = await _db.GetService<OperationsPasswordService>(scope)
                    .VerifyAsync(SensitiveAction.DeleteWorker, "bob-secret");
                Assert.True(result.IsAllowed);
            }

            // نرجع لأليس ونتأكد إن كلمتها لسه شغالة وما اتلمستش
            SignInAs(aliceId, "alice");
            using (var scope = _db.CreateScope())
            {
                var result = await _db.GetService<OperationsPasswordService>(scope)
                    .VerifyAsync(SensitiveAction.DeleteWorker, "alice-secret");
                Assert.True(result.IsAllowed);
            }
        }

        [Fact]
        public async Task ANewAccount_WithNoOperationsPasswordSetYet_GateIsOpen()
        {
            // فيه حساب تاني حدد كلمة سر عمليات، بس الحساب الجديد ده لسه ماحددش
            var existingId = await AddLoginAsync("existing");
            SignInAs(existingId, "existing");
            using (var scope = _db.CreateScope())
                await _db.GetService<OperationsPasswordService>(scope).SetPasswordAsync(null, "existing-secret");

            var newId = await AddLoginAsync("newbie");
            SignInAs(newId, "newbie");

            using var scope2 = _db.CreateScope();
            Assert.False(await _db.GetService<OperationsPasswordService>(scope2).IsConfiguredAsync());

            var result = await _db.GetService<OperationsPasswordService>(scope2)
                .VerifyAsync(SensitiveAction.DeleteWorker, "");
            Assert.True(result.IsAllowed);
            Assert.True(result.IsNotConfigured);
        }

        // ======================= التصحيح الإداري (SetPasswordForUserAsync) =======================

        [Fact]
        public async Task SetPasswordForUserAsync_SetsAnotherAccountsOperationsPassword_WithoutNeedingTheOldOne()
        {
            var bobId = await AddLoginAsync("bob"); // مالوش كلمة سر عمليات خالص لسه

            using (var scope = _db.CreateScope())
                await _db.GetService<OperationsPasswordService>(scope).SetPasswordForUserAsync(bobId, "bob-secret");

            SignInAs(bobId, "bob");
            using var checkScope = _db.CreateScope();
            var result = await _db.GetService<OperationsPasswordService>(checkScope)
                .VerifyAsync(SensitiveAction.DeleteWorker, "bob-secret");
            Assert.True(result.IsAllowed);
        }

        [Fact]
        public async Task SetPasswordForUserAsync_OverwritesAnExistingPassword_WithoutTheOldOne()
        {
            var bobId = await AddLoginAsync("bob");
            SignInAs(bobId, "bob");
            using (var scope = _db.CreateScope())
                await _db.GetService<OperationsPasswordService>(scope).SetPasswordAsync(null, "old-secret");

            // تصحيح إداري: مدير القسم بيغيّرها لبوب بدون ما يحتاج القديمة
            using (var scope = _db.CreateScope())
                await _db.GetService<OperationsPasswordService>(scope).SetPasswordForUserAsync(bobId, "new-secret");

            SignInAs(bobId, "bob");
            using var checkScope = _db.CreateScope();
            var gate = _db.GetService<OperationsPasswordService>(checkScope);

            Assert.False((await gate.VerifyAsync(SensitiveAction.DeleteWorker, "old-secret")).IsAllowed);
            Assert.True((await gate.VerifyAsync(SensitiveAction.DeleteWorker, "new-secret")).IsAllowed);
        }

        [Fact]
        public async Task SetPasswordForUserAsync_DoesNotAffectOtherAccounts()
        {
            var aliceId = await AddLoginAsync("alice");
            SignInAs(aliceId, "alice");
            using (var scope = _db.CreateScope())
                await _db.GetService<OperationsPasswordService>(scope).SetPasswordAsync(null, "alice-secret");

            var bobId = await AddLoginAsync("bob");
            using (var scope = _db.CreateScope())
                await _db.GetService<OperationsPasswordService>(scope).SetPasswordForUserAsync(bobId, "bob-secret");

            SignInAs(aliceId, "alice");
            using var checkScope = _db.CreateScope();
            var result = await _db.GetService<OperationsPasswordService>(checkScope)
                .VerifyAsync(SensitiveAction.DeleteWorker, "alice-secret");
            Assert.True(result.IsAllowed);
        }

        // ======================= الهاجر التلقائي (SeedDefaultDepartmentManagerAsync) =======================

        [Fact]
        public async Task Seeding_LinksTheFirstLoginAccount_AsTheDefaultDepartmentManager()
        {
            var userId = await AddLoginAsync("admin");

            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                await DatabaseSeeder.SeedDefaultDepartmentManagerAsync(db);
            }

            using var checkScope = _db.CreateScope();
            var checkDb = _db.GetService<AppDbContext>(checkScope);

            var user = await checkDb.AppUsers.FindAsync(userId);
            Assert.NotNull(user!.WorkerId);

            var worker = await checkDb.Workers.FindAsync(user.WorkerId!.Value);
            Assert.Equal(HourlyRole.DepartmentManager, worker!.HourlyRole);
        }

        [Fact]
        public async Task Seeding_MigratesTheOldSharedOperationsPassword_ToTheNewDefaultManager()
        {
            var userId = await AddLoginAsync("admin");

            // كلمة سر عمليات "قديمة" — بدون AppUserId، زي ما كانت قبل الميزة دي
            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                var (hash, salt) = ("hash", "salt");
                db.OperationsCredentials.Add(new OperationsCredential
                {
                    AppUserId = null,
                    PasswordHash = hash,
                    PasswordSalt = salt
                });
                await db.SaveChangesAsync();

                await DatabaseSeeder.SeedDefaultDepartmentManagerAsync(db);
            }

            using var checkScope = _db.CreateScope();
            var checkDb = _db.GetService<AppDbContext>(checkScope);
            var credential = await checkDb.OperationsCredentials.FirstAsync();

            Assert.Equal(userId, credential.AppUserId);
        }

        [Fact]
        public async Task Seeding_RepairsAnOrphanedManagerWorker_CreatedBeforeTheLoginFeatureExisted()
        {
            // حساب إداري اتعمل من شاشة الحسابات الإدارية قبل ما ميزة
            // "كل حساب له يوزر وباسورد" توصل — صف Worker بدور مدير قسم
            // موجود، بس مالوش أي AppUser بيشاور عليه. من غيره محدش يقدر
            // يفتح شاشة الحسابات كمدير عشان يربطه بنفسه.
            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                db.Workers.Add(new Worker { FullName = "مهندس عمرو", HourlyRole = HourlyRole.DepartmentManager, IsActive = true });
                await db.SaveChangesAsync();
            }

            var userId = await AddLoginAsync("salem");

            using (var scope = _db.CreateScope())
                await DatabaseSeeder.SeedDefaultDepartmentManagerAsync(_db.GetService<AppDbContext>(scope));

            using var checkScope = _db.CreateScope();
            var checkDb = _db.GetService<AppDbContext>(checkScope);

            var user = await checkDb.AppUsers.FindAsync(userId);
            Assert.NotNull(user!.WorkerId);

            var worker = await checkDb.Workers.FindAsync(user.WorkerId!.Value);
            Assert.Equal("مهندس عمرو", worker!.FullName); // اتربط بالمدير اليتيم الموجود، مش عمل واحد جديد

            Assert.Equal(1, await checkDb.Workers.CountAsync(w => w.HourlyRole == HourlyRole.DepartmentManager));
        }

        [Fact]
        public async Task Seeding_IsIdempotent_DoesNothingIfADepartmentAccountAlreadyExists()
        {
            var firstUserId = await AddLoginAsync("first");
            using (var scope = _db.CreateScope())
                await DatabaseSeeder.SeedDefaultDepartmentManagerAsync(_db.GetService<AppDbContext>(scope));

            var secondUserId = await AddLoginAsync("second");
            using (var scope = _db.CreateScope())
                await DatabaseSeeder.SeedDefaultDepartmentManagerAsync(_db.GetService<AppDbContext>(scope));

            using var checkScope = _db.CreateScope();
            var checkDb = _db.GetService<AppDbContext>(checkScope);

            var secondUser = await checkDb.AppUsers.FindAsync(secondUserId);
            Assert.Null(secondUser!.WorkerId); // الحساب التاني ماتلمسش — الأول بس اتربط

            var managerCount = await checkDb.Workers.CountAsync(w => w.HourlyRole == HourlyRole.DepartmentManager);
            Assert.Equal(1, managerCount);
        }
    }
}
