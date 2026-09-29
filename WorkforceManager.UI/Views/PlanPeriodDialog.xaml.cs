using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.Services;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// إنشاء فترة خطة مخصصة — شوف تعليق XAML. المعاينة الحية لعدد أيام
    /// الشغل بتنادي PlanPeriodService.PreviewWorkdaysAsync (نفس حسبة
    /// CreatePeriodAsync بالظبط، مش نسخة تانية)، عشان الرقم المعروض هنا
    /// ما يقدرش يختلف عن اللي بيتخزّن فعليًا عند الحفظ.
    /// </summary>
    public partial class PlanPeriodDialog : Window, ISaveShortcutDialog
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public PlanPeriodDialog(IServiceScopeFactory scopeFactory)
        {
            InitializeComponent();
            _scopeFactory = scopeFactory;
        }

        public DateTime SelectedStart { get; private set; }
        public DateTime SelectedEnd { get; private set; }

        private async void DatePicker_SelectedDateChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            PreviewBox.Visibility = Visibility.Collapsed;
            ErrorText.Visibility = Visibility.Collapsed;

            if (StartDatePicker.SelectedDate is not { } start || EndDatePicker.SelectedDate is not { } end)
                return;

            if (end.Date < start.Date) return; // Save_Click هيوريه رسالة الخطأ لو حاول يحفظ كده

            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<PlanPeriodService>();
            var workdays = await service.PreviewWorkdaysAsync(start, end);
            PreviewText.Text = $"{workdays} يوم شغل";
            PreviewBox.Visibility = Visibility.Visible;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (StartDatePicker.SelectedDate is not { } start || EndDatePicker.SelectedDate is not { } end)
            {
                ShowError("لازم تختار تاريخ البداية والنهاية");
                return;
            }

            if (end.Date < start.Date)
            {
                ShowError("تاريخ النهاية لازم يكون بعد أو يساوي تاريخ البداية");
                return;
            }

            SelectedStart = start.Date;
            SelectedEnd = end.Date;
            DialogResult = true;
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}
