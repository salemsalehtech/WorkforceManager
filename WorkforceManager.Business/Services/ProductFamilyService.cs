using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Data;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// إدارة عائلات المنتجات — اسم بس، عضوية منتج فيها اختيارية دايمًا.
    /// شوف ProductFamily لسبب عدم وجود حذف ناعم هنا: عيلة بلا منتجات
    /// بتتحذف حذف حقيقي مباشر، مفيش تاريخ بيضيع.
    /// </summary>
    public class ProductFamilyService
    {
        private readonly AppDbContext _db;

        public ProductFamilyService(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>كل العائلات مع عدد منتجات كل واحدة — استعلام واحد</summary>
        public async Task<List<ProductFamilyDto>> GetAllWithCountsAsync() =>
            await _db.ProductFamilies
                .OrderBy(f => f.SortOrder).ThenBy(f => f.Name)
                .Select(f => new ProductFamilyDto(f.Id, f.Name, f.Products.Count))
                .ToListAsync();

        /// <summary>عيلة جديدة بالاسم — بيرفض اسم فاضي أو مكرر حرفيًا (الفهرس الفريد بيضمنها برضه، هنا رسالة أوضح)</summary>
        public async Task<int> CreateAsync(string name)
        {
            var trimmed = (name ?? "").Trim();
            if (trimmed.Length == 0)
                throw new ArgumentException("اسم العيلة مطلوب", nameof(name));

            if (await _db.ProductFamilies.AnyAsync(f => f.Name == trimmed))
                throw new InvalidOperationException($"في عيلة اسمها \"{trimmed}\" بالفعل");

            // عيلة جديدة بتتحط في الآخر
            var nextOrder = await _db.ProductFamilies.Select(f => (int?)f.SortOrder).MaxAsync() ?? -1;
            var family = new Core.Models.ProductFamily { Name = trimmed, SortOrder = nextOrder + 1 };
            _db.ProductFamilies.Add(family);
            await _db.SaveChangesAsync();
            return family.Id;
        }

        /// <summary>
        /// ↑/↓ في إدارة العائلات: بتبدّل العيلة مع اللي فوقها/تحتها في الترتيب. قبل التبديل بنرقّم الكل من جديد
        /// (0، 1، 2…) بالترتيب الحالي، فأي تكرار قديم في SortOrder (عائلات اتعملت قبل العمود ده) مابيبوّظش الحركة.
        /// بيرجّع false لو العيلة أصلًا أول/آخر واحدة.
        /// </summary>
        public async Task<bool> MoveAsync(int familyId, bool up)
        {
            var families = await _db.ProductFamilies.OrderBy(f => f.SortOrder).ThenBy(f => f.Name).ToListAsync();
            for (var i = 0; i < families.Count; i++) families[i].SortOrder = i;

            var index = families.FindIndex(f => f.Id == familyId);
            var neighbor = up ? index - 1 : index + 1;
            if (index < 0 || neighbor < 0 || neighbor >= families.Count)
            {
                await _db.SaveChangesAsync();
                return false;
            }

            (families[index].SortOrder, families[neighbor].SortOrder) = (families[neighbor].SortOrder, families[index].SortOrder);
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task RenameAsync(int familyId, string newName)
        {
            var trimmed = (newName ?? "").Trim();
            if (trimmed.Length == 0)
                throw new ArgumentException("اسم العيلة مطلوب", nameof(newName));

            var family = await _db.ProductFamilies.FindAsync(familyId)
                ?? throw new InvalidOperationException("العيلة دي مش موجودة");

            if (await _db.ProductFamilies.AnyAsync(f => f.Id != familyId && f.Name == trimmed))
                throw new InvalidOperationException($"في عيلة اسمها \"{trimmed}\" بالفعل");

            family.Name = trimmed;
            await _db.SaveChangesAsync();
        }

        /// <summary>حذف عيلة — مرفوض لو لسه فيها منتجات، بدل ما يفضّيهم بصمت من عيلتهم</summary>
        public async Task DeleteAsync(int familyId)
        {
            var family = await _db.ProductFamilies
                .Include(f => f.Products)
                .FirstOrDefaultAsync(f => f.Id == familyId)
                ?? throw new InvalidOperationException("العيلة دي مش موجودة");

            if (family.Products.Count > 0)
                throw new InvalidOperationException(
                    $"مينفعش تتحذف — لسه فيها {family.Products.Count} منتج. شيلهم من العيلة الأول");

            _db.ProductFamilies.Remove(family);
            await _db.SaveChangesAsync();
        }
    }
}
