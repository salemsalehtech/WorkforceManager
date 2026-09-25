using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// ديالوج تعيين عيلة جماعي — يظهر بعد تحديد أكتر من منتج في شاشة
    /// المنتجات، وبيطبّق العيلة المختارة عليهم كلهم دفعة واحدة (شوف
    /// ProductsViewModel.AssignSelectedToFamilyAsync). "+ عيلة جديدة"
    /// بتتحفظ فورًا، نفس منطق ProductEditDialog بالحرف.
    /// </summary>
    public partial class FamilyPickerDialog : Window
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private List<FamilyChoice> _familyChoices = new();

        public FamilyPickerDialog(IReadOnlyList<ProductFamilyDto> families, int selectedCount, IServiceScopeFactory scopeFactory)
        {
            InitializeComponent();
            _scopeFactory = scopeFactory;

            CountText.Text = $"هيتطبّق على {selectedCount} منتج محدد";

            RebuildFamilyChoices(families, selectedId: null);
        }

        private void RebuildFamilyChoices(IReadOnlyList<ProductFamilyDto> families, int? selectedId)
        {
            _familyChoices = FamilyChoiceList.Build(families);
            FamilyBox.ItemsSource = _familyChoices;
            FamilyBox.SelectedItem = _familyChoices.FirstOrDefault(c => !c.IsCreateNew && c.FamilyId == selectedId)
                ?? _familyChoices[0];
        }

        /// <summary>العيلة المختارة بعد الحفظ — null لو "بدون" (بيشيل عيلة كل المنتجات المحددة)</summary>
        public int? SelectedFamilyId => (FamilyBox.SelectedItem as FamilyChoice)?.FamilyId;

        /// <summary>نفس منطق "+ عيلة جديدة" في ProductEditDialog بالحرف — حفظ فوري، مش مع التطبيق</summary>
        private async void FamilyBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (FamilyBox.SelectedItem is not FamilyChoice { IsCreateNew: true }) return;

            var previousSelection = e.RemovedItems.Count > 0 ? e.RemovedItems[0] as FamilyChoice : null;

            var prompt = new TextPromptDialog("عيلة جديدة", "اسم العيلة") { Owner = this };
            if (prompt.ShowDialog() != true)
            {
                FamilyBox.SelectedItem = previousSelection ?? _familyChoices[0];
                return;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ProductFamilyService>();
                var newId = await service.CreateAsync(prompt.Value);

                var families = await service.GetAllWithCountsAsync();
                RebuildFamilyChoices(families, newId);
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "خطأ في إضافة العيلة");
                FamilyBox.SelectedItem = previousSelection ?? _familyChoices[0];
            }
        }

        private void Apply_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}
