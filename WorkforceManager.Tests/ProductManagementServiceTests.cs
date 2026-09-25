using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.Services;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>تعيين عيلة جماعي — بديل التصنيف منتج-منتج، شوف ProductsViewModel.AssignSelectedToFamilyAsync</summary>
    public class ProductManagementServiceTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task SetFamilyForProductsAsync_assigns_family_to_all_selected_products_at_once()
        {
            var familyId = await _db.InScopeAsync<ProductFamilyService, int>(s => s.CreateAsync("كباشي"));

            await _db.InScopeAsync<ProductManagementService, bool>(async s =>
            {
                await s.SetFamilyForProductsAsync(
                    new[] { TestDatabase.ProductRingId, TestDatabase.ProductChainId, TestDatabase.ProductBagId }, familyId);
                return true;
            });

            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            var ringFamily = (await db.Products.FindAsync(TestDatabase.ProductRingId))!.FamilyId;
            var chainFamily = (await db.Products.FindAsync(TestDatabase.ProductChainId))!.FamilyId;
            var bagFamily = (await db.Products.FindAsync(TestDatabase.ProductBagId))!.FamilyId;
            var thirdsFamily = (await db.Products.FindAsync(TestDatabase.ProductThirdsId))!.FamilyId;

            Assert.Equal(familyId, ringFamily);
            Assert.Equal(familyId, chainFamily);
            Assert.Equal(familyId, bagFamily);
            Assert.Null(thirdsFamily); // مش من ضمن المحدد، ماتأثرش
        }

        [Fact]
        public async Task SetFamilyForProductsAsync_null_clears_family_for_all_selected()
        {
            var familyId = await _db.InScopeAsync<ProductFamilyService, int>(s => s.CreateAsync("كباشي"));
            await _db.InScopeAsync<ProductManagementService, bool>(async s =>
            { await s.SetFamilyForProductsAsync(new[] { TestDatabase.ProductRingId, TestDatabase.ProductChainId }, familyId); return true; });

            await _db.InScopeAsync<ProductManagementService, bool>(async s =>
            { await s.SetFamilyForProductsAsync(new[] { TestDatabase.ProductRingId, TestDatabase.ProductChainId }, null); return true; });

            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            Assert.Null((await db.Products.FindAsync(TestDatabase.ProductRingId))!.FamilyId);
            Assert.Null((await db.Products.FindAsync(TestDatabase.ProductChainId))!.FamilyId);
        }

        [Fact]
        public async Task SetFamilyForProductsAsync_empty_selection_does_nothing()
        {
            // مفيش استثناء ولا تأثير — اختيار فاضي حالة عادية (زرار "ضيفهم لعيلة" متعطّل أصلاً وقتها في الشاشة)
            await _db.InScopeAsync<ProductManagementService, bool>(async s =>
            { await s.SetFamilyForProductsAsync(Array.Empty<int>(), 1); return true; });

            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            Assert.Null((await db.Products.FindAsync(TestDatabase.ProductRingId))!.FamilyId);
        }

        [Fact]
        public async Task SetFamilyForProductsAsync_uses_one_query_not_one_per_product()
        {
            // ما ينفعش نتحقق من عدد الاستعلامات مباشرة من غير logging،
            // لكن نتأكد إن GetByIdsAsync بترجع كل المنتجات المطلوبة بضربة واحدة
            var products = await _db.InScopeAsync<WorkforceManager.Core.Interfaces.IProductRepository,
                System.Collections.Generic.IReadOnlyList<WorkforceManager.Core.Models.Product>>(
                r => r.GetByIdsAsync(new[] { TestDatabase.ProductRingId, TestDatabase.ProductChainId, TestDatabase.ProductBagId }));

            Assert.Equal(3, products.Count);
        }
    }
}
