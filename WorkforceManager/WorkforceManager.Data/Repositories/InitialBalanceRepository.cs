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
    }
}
