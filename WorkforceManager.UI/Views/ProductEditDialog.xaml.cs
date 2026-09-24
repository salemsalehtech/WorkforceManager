using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>عامل رص معروض في قايمة اختيار عامل الرص الثابت بتاع المنتج (null = بدون)</summary>
    public record RackingWorkerChoice(int? WorkerId, string Name);

    /// <summary>
    /// عيلة معروضة في قايمة اختيار عيلة المنتج — null Id بمعنيين: "بدون"
    /// العادي، أو "+ عيلة جديدة" لو <see cref="IsCreateNew"/>.
    /// </summary>
    public record FamilyChoice(int? FamilyId, string Name, bool IsCreateNew = false);

    public record MaterialChoice(Material? Value, string Display);

    /// <summary>
    /// نافذة إضافة/تعديل منتج. بتتحقق من الاسم بس (الإجباري الوحيد) —
    /// أي قواعد أعمق مسؤولية ProductManagementService.
    ///
    /// **"+ عيلة جديدة" بتتحفظ فورًا في القاعدة** لحظة اختيارها (مش مع
    /// حفظ المنتج) — نفس فكرة أي "quick add" جوه combo: لو المستخدم لغى
    /// الفورم بعدها، العيلة تفضل موجودة كخيار مستقبلي، وده مقبول لأنها
    /// كيان مستقل عن المنتج نفسه.
    /// </summary>
    public partial class ProductEditDialog : Window
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private List<FamilyChoice> _familyChoices = new();

        public ProductEditDialog(
            IReadOnlyList<RackingWorkerChoice> rackingWorkers,
            IReadOnlyList<ProductFamilyDto> families,
            IServiceScopeFactory scopeFactory)
        {
            InitializeComponent();
            Loaded += (_, _) => NameBox.Focus();

            _scopeFactory = scopeFactory;

            RackingWorkerBox.ItemsSource = rackingWorkers;
            RackingWorkerBox.SelectedIndex = 0; // "بدون" أول عنصر دايمًا

            RebuildFamilyChoices(families, selectedId: null);

            MaterialBox.ItemsSource = new List<MaterialChoice>
            {
                new(null, "بدون"),
                new(Material.Copper, "نحاس"),
                new(Material.Zamak, "زاما")
            };
            MaterialBox.SelectedIndex = 0;
        }

        private void RebuildFamilyChoices(IReadOnlyList<ProductFamilyDto> families, int? selectedId)
        {
            _familyChoices = new List<FamilyChoice> { new(null, "بدون") };
            _familyChoices.AddRange(families.Select(f => new FamilyChoice(f.Id, f.Name)));
            _familyChoices.Add(new FamilyChoice(null, "+ عيلة جديدة…", IsCreateNew: true));

            FamilyBox.ItemsSource = _familyChoices;
            FamilyBox.SelectedItem = _familyChoices.FirstOrDefault(c => !c.IsCreateNew && c.FamilyId == selectedId)
                ?? _familyChoices[0];
        }

        // ------- القيم اللي الشاشة الأم بتقرأها بعد الحفظ -------

        public string ProductName => NameBox.Text.Trim();
        public string? ProductDescription => string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim();

        /// <summary>عامل الرص المختار — null لو "بدون" أو مفيش اختيار</summary>
        public int? RackingWorkerId => (RackingWorkerBox.SelectedItem as RackingWorkerChoice)?.WorkerId;

        public int? FamilyId => (FamilyBox.SelectedItem as FamilyChoice)?.FamilyId;

        /// <summary>وزن القطعة بالجرام — null لو الحقل فاضي</summary>
        public decimal? PieceWeightGrams =>
            decimal.TryParse(WeightBox.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var w)
                ? w : null;

        public Material? SelectedMaterial => (MaterialBox.SelectedItem as MaterialChoice)?.Value;

        /// <summary>
        /// صورة المنتج بعد الحفظ (null = مفيش صورة أو المستخدم شالها).
        /// بتبقى مصغّرة ومضغوطة جاهزة للتخزين.
        /// </summary>
        public byte[]? ImageData { get; private set; }

        /// <summary>
        /// اتغيّرت الصورة في الجلسة دي؟ الشاشة الأم بتحفظ الصورة بس لو
        /// اتغيّرت فعلاً — عشان تعديل الاسم لوحده ميعملش كتابة زيادة
        /// للصورة كلها في قاعدة البيانات.
        /// </summary>
        public bool ImageChanged { get; private set; }

        /// <summary>تعبئة الفورم ببيانات منتج موجود (وضع التعديل)</summary>
        public void LoadProduct(
            string name, string? description, byte[]? imageData = null, int? rackingWorkerId = null,
            int? familyId = null, decimal? pieceWeightGrams = null, Material? material = null)
        {
            NameBox.Text = name;
            DescriptionBox.Text = description ?? "";

            ImageData = imageData;
            ImageChanged = false; // التحميل مش تغيير
            ShowImagePreview();

            var choices = (IReadOnlyList<RackingWorkerChoice>)RackingWorkerBox.ItemsSource;
            RackingWorkerBox.SelectedItem = choices.FirstOrDefault(c => c.WorkerId == rackingWorkerId)
                ?? choices[0];

            FamilyBox.SelectedItem = _familyChoices.FirstOrDefault(c => !c.IsCreateNew && c.FamilyId == familyId)
                ?? _familyChoices[0];

            WeightBox.Text = pieceWeightGrams?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";

            var materialChoices = (IReadOnlyList<MaterialChoice>)MaterialBox.ItemsSource;
            MaterialBox.SelectedItem = materialChoices.FirstOrDefault(c => c.Value == material)
                ?? materialChoices[0];
        }

        /// <summary>يعرض الصورة الحالية أو أيقونة "مفيش صورة"</summary>
        private void ShowImagePreview()
        {
            var source = StoredImageHelper.ToImageSource(ImageData);

            ImagePreview.Source = source;
            ImagePreview.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
            NoImageIcon.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
            RemoveImageButton.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void PickImage_Click(object sender, RoutedEventArgs e)
        {
            var picker = new OpenFileDialog
            {
                Title = "اختار صورة المنتج",
                Filter = StoredImageHelper.FileDialogFilter,
                CheckFileExists = true
            };

            if (picker.ShowDialog(this) != true) return;

            try
            {
                // التصغير والضغط بيحصلوا هنا — اللي بيتخزن صورة صغيرة مش الأصل
                ImageData = StoredImageHelper.LoadForStorage(picker.FileName);
                ImageChanged = true;
                ShowImagePreview();

                ErrorText.ClearError();
            }
            catch (Exception ex)
            {
                ErrorText.ShowError(ex.Message);
            }
        }

        private void RemoveImage_Click(object sender, RoutedEventArgs e)
        {
            ImageData = null;
            ImageChanged = true;
            ShowImagePreview();
        }

        /// <summary>
        /// "+ عيلة جديدة" اتختارت: بيفتح TextPromptDialog، يحفظ فورًا عن
        /// طريق ProductFamilyService، وبيضيف العيلة الجديدة لنفس القايمة
        /// ويختارها — من غير ما يقفل فورم المنتج نفسه.
        /// </summary>
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
                RebuildFamilyChoices(
                    families.Select(f => new ProductFamilyDto(f.Id, f.Name, f.ProductCount)).ToList(), newId);
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "خطأ في إضافة العيلة");
                FamilyBox.SelectedItem = previousSelection ?? _familyChoices[0];
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                ErrorText.ShowError("اسم المنتج مطلوب");
                NameBox.Focus();
                return;
            }

            if (!string.IsNullOrWhiteSpace(WeightBox.Text) && PieceWeightGrams is null)
            {
                ErrorText.ShowError("وزن القطعة لازم يكون رقم صحيح أو عشري");
                WeightBox.Focus();
                return;
            }

            if (PieceWeightGrams is <= 0)
            {
                ErrorText.ShowError("وزن القطعة يجب أن يكون رقمًا موجبًا");
                WeightBox.Focus();
                return;
            }

            DialogResult = true;
        }

        /// <summary>النافذة بلا إطار نظام — السحب من الشريط العلوي</summary>
        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
    }
}
