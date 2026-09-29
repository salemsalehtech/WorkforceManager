using CommunityToolkit.Mvvm.ComponentModel;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// منتج واحد جوّه wizard إنشاء الخطة — IsEntered = المستخدم كتب رقم
    /// صحيح فعلاً (حتى لو صفر)، مش مجرد إن الخانة مش فاضية. أساس علامة
    /// الصح الخضرا لكل منتج ولكل عيلة.
    /// </summary>
    public partial class WizardProductRow : ObservableObject
    {
        public int ProductId { get; init; }
        public string ProductName { get; init; } = "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Quantity))]
        private string _quantityText = "";

        [ObservableProperty] private bool _isEntered;

        public int Quantity => int.TryParse(QuantityText, out var q) ? q : 0;

        partial void OnQuantityTextChanged(string value) =>
            IsEntered = int.TryParse(value, out var q) && q >= 0;
    }

    /// <summary>
    /// عيلة جوّه wizard إنشاء الخطة — صح أخضر لما كل منتجاتها تتسجل.
    /// بتسمع لكل منتج جواها عشان EnteredCount/IsComplete يتحدثوا تلقائي،
    /// وبتبعت PropertyChanged لنفسها عشان الفترة اللي فوقها تسمعها هي كمان.
    /// </summary>
    public partial class WizardFamilyGroupRow : ObservableObject
    {
        public string HeaderText { get; }
        public List<WizardProductRow> Products { get; }

        public WizardFamilyGroupRow(string headerText, List<WizardProductRow> products)
        {
            HeaderText = headerText;
            Products = products;
            foreach (var p in products)
                p.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(WizardProductRow.IsEntered))
                    {
                        OnPropertyChanged(nameof(EnteredCount));
                        OnPropertyChanged(nameof(IsComplete));
                    }
                };
        }

        public int EnteredCount => Products.Count(p => p.IsEntered);
        public int TotalCount => Products.Count;
        public bool IsComplete => TotalCount > 0 && EnteredCount == TotalCount;
    }

    /// <summary>
    /// فترة واحدة جوّه wizard إنشاء الخطة — SubPeriodId=0 يعني الشهر كامل،
    /// غير كده Id حقيقي في MonthlyPlanSubPeriod (اتعمل وقت اختيار "تخصيص فترة").
    /// </summary>
    public partial class WizardPeriodRow : ObservableObject
    {
        public int SubPeriodId { get; init; }
        public string Label { get; init; } = "";
        public List<WizardFamilyGroupRow> FamilyGroups { get; }

        public WizardPeriodRow(List<WizardFamilyGroupRow> familyGroups)
        {
            FamilyGroups = familyGroups;
            foreach (var f in familyGroups)
                f.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(WizardFamilyGroupRow.EnteredCount))
                    {
                        OnPropertyChanged(nameof(EnteredCount));
                        OnPropertyChanged(nameof(IsComplete));
                    }
                };
        }

        public int TotalCount => FamilyGroups.Sum(f => f.TotalCount);
        public int EnteredCount => FamilyGroups.Sum(f => f.EnteredCount);
        public bool IsComplete => TotalCount > 0 && EnteredCount == TotalCount;
    }
}
