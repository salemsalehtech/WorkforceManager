using WorkforceManager.Business.Services;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// تسجيل الهالك — رفض اليوم المقفول. **مفيش بوابة باسورد خالص هنا
    /// (Tier B)** — بقى متغطى بتوقيع نهاية اليوم بدل باسورد فوري
    /// (شوف SensitiveAction.RecordScrap و DailyOperationsSignOffServiceTests).
    /// </summary>
    public class ScrapServiceTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Day => TestDatabase.Today;

        [Fact]
        public async Task Recording_scrap_on_a_closed_production_day_is_rejected()
        {
            using (var scope = _db.CreateScope())
                await _db.GetService<DayClosureService>(scope).CloseAsync(Day);

            using var check = _db.CreateScope();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.GetService<ScrapService>(check).RecordAsync(
                    TestDatabase.BagStage1Id, Day, 100));

            Assert.Contains("مقفول", ex.Message);
        }

        [Fact]
        public async Task Recording_scrap_on_an_open_day_succeeds()
        {
            using var scope = _db.CreateScope();
            var record = await _db.GetService<ScrapService>(scope).RecordAsync(
                TestDatabase.BagStage1Id, Day, 100);

            Assert.Equal(100, record.PieceCount);
        }
    }
}
