using WorkforceManager.Business.Services;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// تعديل الأجر اليومي للعامل — Tier A، فجوة كانت موجودة
    /// (SensitiveAction.EditWorkerWage معرّف ومحدش كان بيستخدمه). الباسورد
    /// مطلوب بس لما الأجر فعلاً يتغيّر؛ باقي حقول العامل بتعدّي من غيره.
    /// </summary>
    public class WorkerWageGateTests : IDisposable
    {
        private const string Password = "wage-secret";

        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private async Task SetPasswordAsync()
        {
            await _db.SignInTestUserAsync();

            using var scope = _db.CreateScope();
            await _db.GetService<OperationsPasswordService>(scope).SetPasswordAsync(null, Password);
        }

        [Fact]
        public async Task ChangingTheWage_WithoutAPassword_IsRefused()
        {
            await SetPasswordAsync();

            using var scope = _db.CreateScope();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.GetService<WorkerManagementService>(scope).UpdateWorkerAsync(
                    TestDatabase.WorkerAhmedId, "احمد", dailyWageEgp: 500));

            Assert.NotEmpty(ex.Message);

            var worker = await _db.GetService<Core.Interfaces.IWorkerRepository>(scope)
                .GetByIdAsync(TestDatabase.WorkerAhmedId);
            Assert.NotEqual(500, worker!.DailyWageEgp);
        }

        [Fact]
        public async Task ChangingTheWage_WithTheCorrectPassword_Succeeds()
        {
            await SetPasswordAsync();

            using var scope = _db.CreateScope();
            await _db.GetService<WorkerManagementService>(scope).UpdateWorkerAsync(
                TestDatabase.WorkerAhmedId, "احمد", dailyWageEgp: 500, operationsPassword: Password);

            var worker = await _db.GetService<Core.Interfaces.IWorkerRepository>(scope)
                .GetByIdAsync(TestDatabase.WorkerAhmedId);
            Assert.Equal(500, worker!.DailyWageEgp);
        }

        [Fact]
        public async Task ChangingOnlyTheName_WithAPasswordConfigured_NeedsNoPassword()
        {
            await SetPasswordAsync();

            decimal wage;
            using (var scope = _db.CreateScope())
                wage = (await _db.GetService<Core.Interfaces.IWorkerRepository>(scope)
                    .GetByIdAsync(TestDatabase.WorkerAhmedId))!.DailyWageEgp;

            using var check = _db.CreateScope();
            var worker = await _db.GetService<WorkerManagementService>(check).UpdateWorkerAsync(
                TestDatabase.WorkerAhmedId, "اسم جديد", dailyWageEgp: wage);

            Assert.Equal("اسم جديد", worker.FullName);
        }

        [Fact]
        public async Task ResubmittingTheSameWage_NeedsNoPassword()
        {
            await SetPasswordAsync();

            using var scope = _db.CreateScope();
            var current = (await _db.GetService<Core.Interfaces.IWorkerRepository>(scope)
                .GetByIdAsync(TestDatabase.WorkerAhmedId))!.DailyWageEgp;

            // نفس القيمة القديمة بالظبط — مفيش تغيير فعلي، فمفيش سبب يُسأل باسورد
            await _db.GetService<WorkerManagementService>(scope).UpdateWorkerAsync(
                TestDatabase.WorkerAhmedId, "احمد", dailyWageEgp: current);
        }
    }
}
