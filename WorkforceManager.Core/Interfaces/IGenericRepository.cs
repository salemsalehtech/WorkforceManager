using System.Linq.Expressions;

namespace WorkforceManager.Core.Interfaces
{
    /// <summary>
    /// عمليات أساسية مشتركة (CRUD) لأي نموذج. تمنع تكرار نفس الكود
    /// في كل Repository، وتسمح لاحقًا باستبدال EF Core بأي تقنية
    /// تخزين تانية من غير ما نلمس طبقة الـ Business Logic.
    /// </summary>
    public interface IGenericRepository<T> where T : class
    {
        Task<T?> GetByIdAsync(int id);
        Task<IReadOnlyList<T>> GetAllAsync();
        Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate);
        Task AddAsync(T entity);
        void Update(T entity);
        void Remove(T entity);
        Task<int> SaveChangesAsync();
    }
}
