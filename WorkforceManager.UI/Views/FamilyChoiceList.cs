using WorkforceManager.Business.DTOs;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// بناء قايمة اختيار عيلة موحّد — "بدون" أول عنصر، بعدها العائلات
    /// الموجودة، وآخر عنصر "+ عيلة جديدة…". مشتركة بين ProductEditDialog
    /// وFamilyPickerDialog عشان الاتنين يبنوا نفس القايمة بالظبط.
    /// </summary>
    public static class FamilyChoiceList
    {
        public static List<FamilyChoice> Build(IReadOnlyList<ProductFamilyDto> families)
        {
            var choices = new List<FamilyChoice> { new(null, "بدون") };
            choices.AddRange(families.Select(f => new FamilyChoice(f.Id, f.Name)));
            choices.Add(new FamilyChoice(null, "+ عيلة جديدة…", IsCreateNew: true));
            return choices;
        }
    }
}
