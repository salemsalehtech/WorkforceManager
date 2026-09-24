using System;
using System.Linq;
using System.Threading.Tasks;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Models;
using Xunit;

namespace WorkforceManager.Tests
{
    public class ProductFamilyServiceTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task CreateAsync_rejects_duplicate_name()
        {
            await _db.InScopeAsync<ProductFamilyService, int>(s => s.CreateAsync("عقلة"));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.InScopeAsync<ProductFamilyService, int>(s => s.CreateAsync("عقلة")));
        }

        [Fact]
        public async Task CreateAsync_rejects_empty_name()
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _db.InScopeAsync<ProductFamilyService, int>(s => s.CreateAsync("   ")));
        }

        [Fact]
        public async Task DeleteAsync_rejects_family_with_products()
        {
            var familyId = await _db.InScopeAsync<ProductFamilyService, int>(s => s.CreateAsync("طقم زاما 15"));

            using (var scope = _db.CreateScope())
            {
                var product = await _db.GetService<Data.AppDbContext>(scope).Products.FindAsync(TestDatabase.ProductRingId);
                product!.FamilyId = familyId;
                await _db.GetService<Data.AppDbContext>(scope).SaveChangesAsync();
            }

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.InScopeAsync<ProductFamilyService, bool>(async s => { await s.DeleteAsync(familyId); return true; }));

            Assert.Contains("1", ex.Message);
        }

        [Fact]
        public async Task DeleteAsync_succeeds_when_family_is_empty()
        {
            var familyId = await _db.InScopeAsync<ProductFamilyService, int>(s => s.CreateAsync("عيلة فاضية"));

            await _db.InScopeAsync<ProductFamilyService, bool>(async s => { await s.DeleteAsync(familyId); return true; });

            var all = await _db.InScopeAsync<ProductFamilyService,
                System.Collections.Generic.List<Business.DTOs.ProductFamilyDto>>(s => s.GetAllWithCountsAsync());
            Assert.DoesNotContain(all, f => f.Id == familyId);
        }

        [Fact]
        public async Task RenameAsync_rejects_collision_with_another_family()
        {
            var idA = await _db.InScopeAsync<ProductFamilyService, int>(s => s.CreateAsync("أ"));
            await _db.InScopeAsync<ProductFamilyService, int>(s => s.CreateAsync("ب"));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.InScopeAsync<ProductFamilyService, bool>(async s => { await s.RenameAsync(idA, "ب"); return true; }));
        }
    }
}
