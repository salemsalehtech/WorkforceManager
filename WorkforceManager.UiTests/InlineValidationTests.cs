using System.Windows;
using WorkforceManager.UI.ViewModels;
using WorkforceManager.UI.Views;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// أخطاء الخانات تحتها (FieldError) بدل الإشعارات الطايرة — القواعد، وفورمات
    /// التسجيل اليومي (التحقق بيحصل قبل أي لمسة لقاعدة البيانات، فالـViewModel
    /// بيتبني من غير DI)
    /// </summary>
    public class InlineValidationTests
    {
        private static DailyEntryViewModel DailyEntry() => new(scopeFactory: null!);

        private static AttendanceRow Worker() => new(1, "أحمد علي", isHourly: false, roleText: "");

        [Theory]
        [InlineData(null, "مطلوب")]
        [InlineData("", "مطلوب")]
        [InlineData("   ", "مطلوب")]
        [InlineData("قيمة", "")]
        public void Required_Text(string? value, string expected)
        {
            Assert.Equal(expected, FieldRules.Required(value, "مطلوب"));
        }

        [Theory]
        [InlineData("", "خطأ")]
        [InlineData("abc", "خطأ")]
        [InlineData("0", "خطأ")]
        [InlineData("-5", "خطأ")]
        [InlineData("150", "")]
        [InlineData("12.5", "")]
        public void PositiveDecimal(string text, string expected)
        {
            Assert.Equal(expected, FieldRules.PositiveDecimal(text, "خطأ"));
        }

        [Theory]
        [InlineData("", "")]      // فاضي = مفيش قيمة، مش غلط
        [InlineData("0", "")]
        [InlineData("40", "")]
        [InlineData("-1", "خطأ")]
        [InlineData("2.5", "خطأ")]
        [InlineData("x", "خطأ")]
        public void OptionalNonNegativeInt(string text, string expected)
        {
            Assert.Equal(expected, FieldRules.OptionalNonNegativeInt(text, "خطأ"));
        }

        [Fact]
        public void PenaltyForm_ShowsAllErrorsAtOnce()
        {
            var vm = DailyEntry();
            vm.SelectedDeduction = null;

            Assert.False(vm.ValidatePenaltyForm());
            Assert.Equal("اختار العامل الأول", vm.PenaltyWorkerError);
            Assert.Equal("اكتب سبب الجزاء", vm.PenaltyReasonError);
            Assert.Equal("اختار قيمة الخصم", vm.PenaltyDeductionError);
        }

        [Fact]
        public void PenaltyForm_EditingAFieldClearsOnlyItsError()
        {
            var vm = DailyEntry();
            vm.ValidatePenaltyForm();

            vm.PenaltyReason = "شرب سجاير";

            Assert.Equal("", vm.PenaltyReasonError);
            Assert.Equal("اختار العامل الأول", vm.PenaltyWorkerError);
        }

        [Fact]
        public void PenaltyForm_Valid_NoErrors()
        {
            var vm = DailyEntry();
            vm.PenaltyWorker = Worker();
            vm.PenaltyReason = "شرب سجاير";

            Assert.True(vm.ValidatePenaltyForm());
            Assert.Equal("", vm.PenaltyWorkerError + vm.PenaltyReasonError + vm.PenaltyDeductionError);
        }

        [Fact]
        public void AdjustmentForm_ShowsAllErrorsAtOnce_ThenClearsPerField()
        {
            var vm = DailyEntry();
            vm.AdjustmentAmount = "صفر";

            Assert.False(vm.ValidateAdjustmentForm());
            Assert.Equal("اختار العامل الأول", vm.AdjustmentWorkerError);
            Assert.Equal("اختار النوع (سلفة/حافز)", vm.AdjustmentTypeError);
            Assert.Equal("اكتب مبلغ صحيح أكبر من صفر", vm.AdjustmentAmountError);

            vm.AdjustmentAmount = "500";
            Assert.Equal("", vm.AdjustmentAmountError);
            Assert.Equal("اختار العامل الأول", vm.AdjustmentWorkerError);
        }

        [Fact]
        public void AdjustmentForm_Valid()
        {
            var vm = DailyEntry();
            vm.AdjustmentWorker = Worker();
            vm.SelectedAdjustmentType = vm.AdjustmentTypeOptions[0];
            vm.AdjustmentAmount = "500";

            Assert.True(vm.ValidateAdjustmentForm());
        }

        [Fact]
        public async Task Memory_SaveWithoutProduct_ShowsInlineError_ClearedOnPick()
        {
            // التحقق قبل أي لمسة لقاعدة البيانات — فمفيش DI محتاج
            var vm = new MemoryViewModel(scopeFactory: null!);

            await vm.SaveCommand.ExecuteAsync(null);
            Assert.Equal("اختار المنتج الأول", vm.ProductError);

            vm.SelectedProduct = new MemoryProductOption { ProductId = 1, ProductName = "دبلة" };
            Assert.Equal("", vm.ProductError);
        }

        [Fact]
        public void MonthlyPlanRow_DailyTargetError_ClearsWhenTextEdited()
        {
            var row = new MonthlyPlanProductRow { ProductId = 1 };
            row.DailyTargetError = "الخطة اليومية لازم تكون رقم صحيح أو فاضية";

            row.DailyTargetText = "40";

            Assert.Equal("", row.DailyTargetError);
        }

        [Fact]
        public void FieldError_CollapsedWhenEmpty_VisibleWithMessage()
        {
            var (empty, shown, cleared) = WpfThread.Run(() =>
            {
                var error = new FieldError();
                var e = error.Visibility;
                error.Message = "اكتب سبب الجزاء";
                var s = error.Visibility;
                error.Message = "";
                return (e, s, error.Visibility);
            });

            Assert.Equal(Visibility.Collapsed, empty);
            Assert.Equal(Visibility.Visible, shown);
            Assert.Equal(Visibility.Collapsed, cleared);
        }
    }
}
