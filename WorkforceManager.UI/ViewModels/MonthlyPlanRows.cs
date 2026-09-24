using CommunityToolkit.Mvvm.ComponentModel;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// منتج واحد في شاشة الخطة الشهرية — QuantityText نص قابل للتعديل
    /// (مش int مباشرة) عشان الـTextBox يقدر يمسك حالة وسطية أثناء الكتابة،
    /// والتحويل الفعلي بيحصل عند الحفظ (LostFocus)، شوف MonthlyPlanView.xaml.cs.
    /// </summary>
    public partial class MonthlyPlanProductRow : ObservableObject
    {
        public int ProductId { get; init; }
        public string ProductName { get; init; } = "";
        public bool IsComplete { get; init; }

        [ObservableProperty] private string _quantityText = "0";

        /// <summary>القيمة الرقمية الحالية — 0 لو النص مش رقم صحيح</summary>
        public int Quantity => int.TryParse(QuantityText, out var q) ? q : 0;
    }

    /// <summary>
    /// عيلة (أو "بدون عيلة") في شاشة الخطة الشهرية — الخطة هنا دايمًا SUM
    /// محسوب من منتجاتها، مفيش قيمة بتتكتب على مستوى العيلة خالص.
    /// </summary>
    public partial class MonthlyPlanFamilyGroupRow : ObservableObject
    {
        public string HeaderText { get; init; } = "";
        public List<MonthlyPlanProductRow> Products { get; init; } = new();

        /// <summary>مجموع خطط منتجاتها — للقراءة بس</summary>
        public int Subtotal => Products.Sum(p => p.Quantity);

        /// <summary>بيتنادى بعد أي تعديل كمية جوه القسم ده عشان Subtotal يتحدّث على الشاشة</summary>
        public void RefreshSubtotal() => OnPropertyChanged(nameof(Subtotal));
    }
}
