using WorkforceManager.Business.Services;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// بوابة أمان الهالك — كلمة سر العمليات ورفض اليوم المقفول، بنفس
    /// قاعدة أي عملية بتلمس فلوس تانية (شوف SensitiveAction.RecordScrap).
    /// كانت الشاشة القديمة مفيهاش أي بوابة خالص.
    /// </summary>
    public class ScrapServiceTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Day => TestDatabase.Today;
        private const string Password = "1234";

        private async Task SetPasswordAsync()
        {
            await _db.SignInTestUserAsync();

            using var scope = _db.CreateScope();
            await _db.GetService<OperationsPasswordService>(scope).SetPasswordAsync(null, Password);
        }

        [Fact]
        public async Task Recording_scrap_without_the_operations_password_is_rejected()
        {
            await SetPasswordAsync();

            using var scope = _db.CreateScope();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.GetService<ScrapService>(scope).RecordAsync(
                    TestDatabase.BagStage1Id, Day, 100, "غلط"));

            Assert.DoesNotContain("مقفول", ex.Message);
        }

        [Fact]
        public async Task Recording_scrap_on_a_closed_production_day_is_rejected()
        {
            using (var scope = _db.CreateScope())
                await _db.GetService<DayClosureService>(scope).CloseAsync(Day);

            using var check = _db.CreateScope();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.GetService<ScrapService>(check).RecordAsync(
                    TestDatabase.BagStage1Id, Day, 100, ""));

            Assert.Contains("مقفول", ex.Message);
        }

        [Fact]
        public async Task Recording_scrap_with_a_valid_password_on_an_open_day_succeeds()
        {
            await SetPasswordAsync();

            using var scope = _db.CreateScope();
            var record = await _db.GetService<ScrapService>(scope).RecordAsync(
                TestDatabase.BagStage1Id, Day, 100, Password);

            Assert.Equal(100, record.PieceCount);
        }
    }
}
