namespace WorkforceManager.Business.DTOs
{
    /// <summary>عيلة منتجات مع عدد منتجاتها — لقايمة الاختيار ودياولوج "إدارة العائلات"</summary>
    public record ProductFamilyDto(int Id, string Name, int ProductCount);
}
