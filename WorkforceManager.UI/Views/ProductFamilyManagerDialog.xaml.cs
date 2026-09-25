using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;

namespace WorkforceManager.UI.Views
{
    /// <summary>صف عيلة في ديالوج الإدارة — قابل للتعديل الحي بعد Rename/Delete من غير إعادة تحميل الديالوج كله</summary>
    public partial class FamilyRow : ObservableObject
    {
        public int Id { get; init; }
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private int _productCount;

        public bool CanDelete => ProductCount == 0;
        public string DeleteTooltip => CanDelete ? "حذف" : $"مينفعش يتحذف — لسه فيه {ProductCount} منتج";

        partial void OnProductCountChanged(int value)
        {
            OnPropertyChanged(nameof(CanDelete));
            OnPropertyChanged(nameof(DeleteTooltip));
        }
    }

    /// <summary>
    /// ديالوج إدارة العائلات المصغّر — قايمة + تعديل اسم + حذف لو فاضية.
    /// مش شاشة كاملة عن قصد (تأكيد مع المستخدم وقت التخطيط).
    /// </summary>
    public partial class ProductFamilyManagerDialog : Window
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly System.Collections.ObjectModel.ObservableCollection<FamilyRow> _rows = new();

        /// <summary>الشاشة الأم بتعيد التحميل لو رجعت true — إعادة تسمية أو حذف حصل فعلاً</summary>
        public bool ChangesMade { get; private set; }

        public ProductFamilyManagerDialog(IServiceScopeFactory scopeFactory)
        {
            InitializeComponent();
            _scopeFactory = scopeFactory;
            FamiliesList.ItemsSource = _rows;
            Loaded += async (_, _) => await ReloadAsync();
        }

        private async Task ReloadAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var families = await scope.ServiceProvider
                .GetRequiredService<ProductFamilyService>()
                .GetAllWithCountsAsync();

            _rows.Clear();
            foreach (var f in families)
                _rows.Add(new FamilyRow { Id = f.Id, Name = f.Name, ProductCount = f.ProductCount });

            EmptyState.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            FamiliesList.Visibility = _rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void Rename_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: FamilyRow row }) return;

            var prompt = new TextPromptDialog("تعديل اسم العيلة", "اسم العيلة", row.Name) { Owner = this };
            if (prompt.ShowDialog() != true) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ProductFamilyService>()
                    .RenameAsync(row.Id, prompt.Value);

                row.Name = prompt.Value;
                ChangesMade = true;
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "خطأ في التعديل");
            }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: FamilyRow row }) return;
            if (!row.CanDelete) return;

            if (!Notify.Ask($"حذف عيلة \"{row.Name}\"؟", "تأكيد")) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ProductFamilyService>()
                    .DeleteAsync(row.Id);

                _rows.Remove(row);
                EmptyState.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                FamiliesList.Visibility = _rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
                ChangesMade = true;
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "خطأ في الحذف");
            }
        }

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}
