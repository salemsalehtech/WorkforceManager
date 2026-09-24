using System.ComponentModel.DataAnnotations;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// عيلة منتجات (مثال: "عقلة"، "طقم زاما 15") — اسم بس، مفيش "نوع"
    /// أو تصنيف إضافي. عضوية منتج فيها اختيارية دايمًا (شوف
    /// <see cref="Product.FamilyId"/>) ومفيهاش رقم خطة بيتكتب مباشرة —
    /// أي مجموع على مستوى العيلة محسوب من منتجاتها وقت العرض، مش مخزّن هنا.
    ///
    /// مفيش حذف ناعم هنا عن قصد: العيلة مجرد تصنيف بلا تاريخ خاص بيها،
    /// وحذفها مسموح بس لو فاضية (شوف ProductFamilyService.DeleteAsync) —
    /// فحذف حقيقي مباشر مايضيّعش حاجة.
    /// </summary>
    public class ProductFamily
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "اسم العيلة مطلوب")]
        [MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        public virtual ICollection<Product> Products { get; set; } = new List<Product>();
    }
}
