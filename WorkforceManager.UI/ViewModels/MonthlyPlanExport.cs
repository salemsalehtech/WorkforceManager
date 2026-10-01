using System.IO;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// كتابة ملف إكسل الخطة الشهرية — مكان واحد بيستخدمه زرار "تصدير إكسل" والحفظ
    /// التلقائي بعد توقيع نهاية اليوم، عشان الملفين يطلعوا بنفس الشكل بالظبط
    /// (اللوحة، الخطة الشهرية، تفاصيل، يوم بيوم).
    /// </summary>
    public static class MonthlyPlanExport
    {
        public static readonly string[] ArabicMonthNames =
        {
            "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو",
            "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"
        };

        public static string MonthLabel(int year, int month) => $"{ArabicMonthNames[month - 1]} {year}";

        /// <summary>فولدر تقارير الخطة اليومية التلقائية — في المستندات عشان يتلاقى وينبعت بسهولة</summary>
        public static string AutoReportsFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WMS تقارير", "الخطة الشهرية");

        /// <summary>بيرجع false لو مفيش أي بيانات تتصدّر (شهر فاضي)</summary>
        public static async Task<bool> WriteAsync(IServiceScopeFactory scopeFactory, int year, int month, DateTime asOfDate, string filePath)
        {
            using var scope = scopeFactory.CreateScope();
            var trackingService = scope.ServiceProvider.GetRequiredService<MonthlyPlanTrackingService>();
            var tracking = await trackingService.GetTrackingAsync(year, month, asOfDate);
            if (tracking.Count == 0) return false;
            var daily = await trackingService.GetDailyBreakdownAsync(year, month, asOfDate);

            // اسم القسم عنوان الشيت ("قسم الصنفرة") زي شيت المصنع اللي اتعوّدوا عليه
            var settings = Data.AppSettingsStore.Load();
            scope.ServiceProvider.GetRequiredService<MonthlyPlanExcelService>()
                .Export(tracking, $"{MonthLabel(year, month)} — لحد {asOfDate:yyyy/MM/dd}", filePath,
                    new ReportExportOptions { FactoryName = settings.FactoryName, DepartmentName = settings.DepartmentName },
                    daily);
            return true;
        }

        /// <summary>
        /// نسخة النهارده في فولدر التقارير — بتتنده بعد توقيع نهاية اليوم. فشلها
        /// مايوقفش التوقيع (التوقيع اتم خلاص)، بيرجع المسار أو null.
        /// </summary>
        public static async Task<string?> WriteDailyAutoReportAsync(IServiceScopeFactory scopeFactory, DateTime day)
        {
            try
            {
                Directory.CreateDirectory(AutoReportsFolder);
                var path = Path.Combine(AutoReportsFolder, $"الخطة الشهرية {day:yyyy-MM-dd}.xlsx");
                return await Task.Run(() => WriteAsync(scopeFactory, day.Year, day.Month, day, path)) ? path : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
