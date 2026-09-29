using WorkforceManager.UI.ViewModels;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// تتبّع الإكمال جوّه wizard إنشاء الخطة — صح أخضر لكل منتج لما رقم
    /// صحيح يتكتب (حتى لو صفر)، ولكل عيلة لما كل منتجاتها تتسجل، ولفترة
    /// لما كل عيلاتها تخلص. شوف PlanCreationWizardRows.cs.
    /// </summary>
    public class PlanCreationWizardRowsTests
    {
        private static WizardProductRow Product(string name = "منتج") => new() { ProductId = 1, ProductName = name };

        [Fact]
        public void Product_IsNotEntered_WhenTextIsEmpty()
        {
            var p = Product();
            Assert.False(p.IsEntered);
        }

        [Fact]
        public void Product_IsEntered_WhenValidNumberTyped_EvenZero()
        {
            var p = Product();
            p.QuantityText = "0";
            Assert.True(p.IsEntered);
            Assert.Equal(0, p.Quantity);
        }

        [Fact]
        public void Product_IsNotEntered_WhenTextIsInvalid()
        {
            var p = Product();
            p.QuantityText = "abc";
            Assert.False(p.IsEntered);
        }

        [Fact]
        public void Product_BecomesNotEntered_IfClearedBackToEmpty()
        {
            var p = Product();
            p.QuantityText = "50";
            Assert.True(p.IsEntered);

            p.QuantityText = "";
            Assert.False(p.IsEntered);
        }

        [Fact]
        public void Family_IsComplete_OnlyWhenEveryProductEntered()
        {
            var p1 = Product("أ");
            var p2 = Product("ب");
            var family = new WizardFamilyGroupRow("عيلة", new List<WizardProductRow> { p1, p2 });

            Assert.False(family.IsComplete);
            Assert.Equal(0, family.EnteredCount);

            p1.QuantityText = "10";
            Assert.False(family.IsComplete); // لسه p2 ناقص
            Assert.Equal(1, family.EnteredCount);

            p2.QuantityText = "20";
            Assert.True(family.IsComplete);
            Assert.Equal(2, family.EnteredCount);

            p2.QuantityText = ""; // رجع فاضي — الصح يتشال
            Assert.False(family.IsComplete);
        }

        [Fact]
        public void Family_WithNoProducts_IsNotComplete()
        {
            var family = new WizardFamilyGroupRow("عيلة فاضية", new List<WizardProductRow>());
            Assert.False(family.IsComplete);
        }

        [Fact]
        public void Period_IsComplete_OnlyWhenEveryFamilyComplete()
        {
            var p1 = Product("أ");
            var p2 = Product("ب");
            var family1 = new WizardFamilyGroupRow("عيلة 1", new List<WizardProductRow> { p1 });
            var family2 = new WizardFamilyGroupRow("عيلة 2", new List<WizardProductRow> { p2 });
            var period = new WizardPeriodRow(new List<WizardFamilyGroupRow> { family1, family2 }) { SubPeriodId = 0, Label = "الشهر كامل" };

            Assert.Equal(0, period.EnteredCount);
            Assert.Equal(2, period.TotalCount);
            Assert.False(period.IsComplete);

            p1.QuantityText = "5";
            Assert.Equal(1, period.EnteredCount);
            Assert.False(period.IsComplete); // family2 لسه

            p2.QuantityText = "0";
            Assert.Equal(2, period.EnteredCount);
            Assert.True(period.IsComplete);
        }
    }
}
