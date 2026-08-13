using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// بروفايل حساب إداري: حضوره وغيابه (آخر 30 يوم) + سلفه وحوافزه
    /// (آخر سنة). للقراءة بس لو مش مدير قسم — <see cref="_canManage"/>
    /// بتتحكم في ظهور أزرار التصحيح والإضافة والحذف.
    /// </summary>
    public partial class DepartmentAccountProfileDialog : Window
    {
        private record AttendanceRowItem(string DateText, string StatusText, string WorkdaysText, string StatusColor);

        private record AdjustmentRowItem(
            int AdjustmentId, string AmountText, string TypeName, string TypeColor, string DateAndNote, bool CanRemove);

        private record AdjustmentTypeOption(WageAdjustmentType Type, string Display);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly DepartmentAccountRow _account;
        private readonly bool _canManage;

        public DepartmentAccountProfileDialog(
            IServiceScopeFactory scopeFactory, DepartmentAccountRow account, bool canManage)
        {
            InitializeComponent();
            _scopeFactory = scopeFactory;
            _account = account;
            _canManage = canManage;
            DataContext = account;

            AddCorrectionButton.Visibility = canManage ? Visibility.Visible : Visibility.Collapsed;
            AddAdjustmentButton.Visibility = canManage ? Visibility.Visible : Visibility.Collapsed;

            AdjustmentTypeBox.ItemsSource = new[]
            {
                new AdjustmentTypeOption(WageAdjustmentType.Bonus, "حافز"),
                new AdjustmentTypeOption(WageAdjustmentType.Advance, "سلفة")
            };
            AdjustmentTypeBox.SelectedIndex = 0;

            Loaded += async (_, _) => await ReloadAsync();
        }

        private async Task ReloadAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var attendanceRepo = scope.ServiceProvider.GetRequiredService<IAttendanceRepository>();
            var hourlyRepo = scope.ServiceProvider.GetRequiredService<IHourlyWorkLogRepository>();
            var adjustmentRepo = scope.ServiceProvider.GetRequiredService<IWageAdjustmentRepository>();

            var to = DateTime.Today;
            var from = to.AddDays(-29);

            var attendanceByDay = (await attendanceRepo.GetByWorkerAndRangeAsync(_account.WorkerId, from, to))
                .ToDictionary(a => a.Date.Date);
            var hourlyByDay = (await hourlyRepo.GetByRangeAsync(from, to))
                .Where(h => h.WorkerId == _account.WorkerId)
                .ToDictionary(h => h.Date.Date);

            var attendanceRows = new List<AttendanceRowItem>();
            for (var day = to; day >= from; day = day.AddDays(-1))
            {
                attendanceByDay.TryGetValue(day, out var attendance);
                hourlyByDay.TryGetValue(day, out var hourly);

                attendanceRows.Add(new AttendanceRowItem(
                    day.ToString("yyyy/MM/dd dddd"),
                    attendance?.Status.ToArabicName() ?? "لسه مفيش سجل",
                    hourly is null ? "" : $"{hourly.WorkdaysCredited:0.##} يومية",
                    AttendanceVisuals.ColorFor(attendance?.Status)));
            }
            AttendanceList.ItemsSource = attendanceRows;

            var adjustments = (await adjustmentRepo.GetByRangeAsync(to.AddYears(-1), to))
                .Where(a => a.WorkerId == _account.WorkerId)
                .OrderByDescending(a => a.Date)
                .ToList();

            AdjustmentsList.ItemsSource = adjustments.Select(a => new AdjustmentRowItem(
                a.Id,
                $"{a.AmountEgp:N0} ج",
                a.Type.ToArabicName(),
                a.Type == WageAdjustmentType.Bonus ? "GoodBrush" : "DangerBrush",
                string.IsNullOrWhiteSpace(a.Note) ? $"{a.Date:yyyy/MM/dd}" : $"{a.Date:yyyy/MM/dd} — {a.Note}",
                _canManage)).ToList();

            NoAdjustmentsText.Visibility = adjustments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void AddCorrection_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new DepartmentOvertimeDialog(_account.FullName, _account.Role) { Owner = this };
            if (dialog.ShowDialog() != true) return;

            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<DepartmentAttendanceService>()
                .CorrectDayAsync(_account.WorkerId, dialog.Day, dialog.Status, dialog.EndHour24);

            await ReloadAsync();
        }

        private void AddAdjustment_Click(object sender, RoutedEventArgs e)
        {
            AdjustmentErrorText.ClearError();
            AddAdjustmentPanel.Visibility = AddAdjustmentPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private async void SaveAdjustment_Click(object sender, RoutedEventArgs e)
        {
            if (!decimal.TryParse(AdjustmentAmountBox.Text.Trim(), out var amount) || amount <= 0)
            {
                AdjustmentErrorText.ShowError("المبلغ لازم يكون رقم موجب");
                return;
            }

            var type = (WageAdjustmentType)(AdjustmentTypeBox.SelectedValue ?? WageAdjustmentType.Bonus);
            var typeName = type == WageAdjustmentType.Bonus ? "حافز" : "سلفة";

            var gate = SensitiveActionDialog.Ask(
                this, $"تسجيل {typeName}",
                $"{_account.FullName} — {amount:N0} ج {typeName}.",
                SensitiveActionKind.Save, passwordRequired: true, reasonRequired: false);
            if (gate is null) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<WageAdjustmentService>().RecordAdjustmentAsync(
                    _account.WorkerId, DateTime.Today, type, amount,
                    string.IsNullOrWhiteSpace(AdjustmentNoteBox.Text) ? null : AdjustmentNoteBox.Text.Trim(),
                    gate.Password);

                AdjustmentAmountBox.Text = "";
                AdjustmentNoteBox.Text = "";
                AddAdjustmentPanel.Visibility = Visibility.Collapsed;

                await ReloadAsync();
            }
            catch (Exception ex)
            {
                AdjustmentErrorText.ShowError(ex.Message);
            }
        }

        private async void RemoveAdjustment_Click(object sender, RoutedEventArgs e)
        {
            var adjustmentId = (int)((Button)sender).Tag;

            using var scope = _scopeFactory.CreateScope();
            var gate = scope.ServiceProvider.GetRequiredService<OperationsPasswordService>();

            var input = SensitiveActionDialog.Ask(
                this, "حذف حركة", "الحركة هتتشال نهائيًا.",
                SensitiveActionKind.Delete, await gate.IsConfiguredAsync());
            if (input is null) return;

            try
            {
                await scope.ServiceProvider.GetRequiredService<WageAdjustmentService>()
                    .RemoveAdjustmentAsync(adjustmentId, input.Password);
                await ReloadAsync();
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "خطأ في الحذف");
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
    }
}
