using Microsoft.EntityFrameworkCore;
using WorkforceManager.Core.Helpers;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Data.Repositories
{
    public class InitialBalanceRepository : IInitialBalanceRepository
    {
        private readonly AppDbContext _context;

        public InitialBalanceRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(InitialBalance balance) => await _context.InitialBalances.AddAsync(balance);

        public async Task AddRangeAsync(InitialBalanceRange range) => await _context.InitialBalanceRanges.AddAsync(range);

        public async Task<IReadOnlyList<(int FromStageId, int ToStageId, int Remaining)>> GetOpenRangeRemainingsAsync(int productId)
        {
            // فلتر الحذف الناعم (IsDeleted) بيتطبق تلقائي عبر الـ Global Query Filter
            var ranges = await _context.InitialBalanceRanges
                .Where(r => r.InitialBalance.ProductId == productId)
                .Include(r => r.InitialBalance).ThenInclude(b => b.Usages)
                .ToListAsync();

            return ranges
                .Select(r => (r.FromStageId, r.ToStageId, Remaining: r.PieceCount - InitialBalanceRangeMath.UsedQuantity(r, r.InitialBalance.Usages)))
                .Where(x => x.Remaining > 0)
                .ToList();
        }
    }
}
