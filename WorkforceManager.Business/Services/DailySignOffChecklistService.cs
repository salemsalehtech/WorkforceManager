using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Interfaces;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// اكتمال بيانات اليوم قبل التوقيع — محور مختلف عن
    /// DailyOperationsSignOffService عن قصد (تغطية سجل العمليات مش لها
    /// علاقة باكتمال البيانات)، فمفصولة في خدمة لوحدها بدل ما تتحط جنب
    /// بعض في خدمة واحدة بمسؤوليتين مختلفتين.
    ///
    /// خمس قراءات رخيصة مفهرسة (Product/Worker النشطين، إنتاج/حضور
    /// النهارده، خطط الذاكرة المستحقة) وبعدين مقارنة في الميموري — صفر
    /// استعلام إضافي لكل عنصر (نفس فلسفة WorkerRepository.CountNeedingAttentionAsync).
    /// </summary>
    public class DailySignOffChecklistService
    {
        private readonly IProductRepository _products;
        private readonly IWorkerRepository _workers;
        private readonly IDailyProductionRepository _production;
        private readonly IAttendanceRepository _attendance;
        private readonly ProductionMemoryService _memory;

        public DailySignOffChecklistService(
            IProductRepository products, IWorkerRepository workers,
            IDailyProductionRepository production, IAttendanceRepository attendance,
            ProductionMemoryService memory)
        {
            _products = products;
            _workers = workers;
            _production = production;
            _attendance = attendance;
            _memory = memory;
        }

        public async Task<DailySignOffChecklist> BuildAsync(DateTime date)
        {
            var activeProducts = await _products.GetActiveWithStagesAsync();
            var activeWorkers = await _workers.GetActiveWithSkillsAsync();
            var todaysProduction = await _production.GetByDateAsync(date);
            var todaysAttendance = await _attendance.GetByDateAsync(date);
            var dueMemories = await _memory.GetDueAsync(date);

            var producedProductIds = todaysProduction.Select(p => p.ProductionStage.ProductId).ToHashSet();
            var attendedWorkerIds = todaysAttendance.Select(a => a.WorkerId).ToHashSet();

            var emptyProducts = activeProducts
                .Where(p => !producedProductIds.Contains(p.Id))
                .Select(p => p.Name)
                .OrderBy(n => n)
                .ToList();

            var missingAttendance = activeWorkers
                .Where(w => !attendedWorkerIds.Contains(w.Id))
                .Select(w => w.FullName)
                .OrderBy(n => n)
                .ToList();

            // مش m.IsDueToday عن قصد: الخاصية دي مربوطة بـDateTime.Today
            // الحقيقي (ساعة الجهاز)، بينما BuildAsync بياخد date كباراميتر
            // صريح (نفس فلسفة DailyOperationsSignOffService) عشان الاختبار
            // يقدر يمشّي تاريخ تاني من غير ما يلمس ساعة الجهاز
            var memoryGaps = dueMemories
                .Where(m => m.CompletedAt is null && m.RemindOn.Date == date.Date && !producedProductIds.Contains(m.ProductId))
                .Select(m => m.ProductName)
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            return new DailySignOffChecklist
            {
                ProductsWithNoEntries = emptyProducts,
                WorkersWithNoAttendance = missingAttendance,
                MemoryDueWithNoEntries = memoryGaps
            };
        }
    }
}
