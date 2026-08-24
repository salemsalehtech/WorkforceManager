using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WorkforceManager.Core.Interfaces;

namespace WorkforceManager.Data.Repositories
{
    /// <summary>
    /// تنفيذ <see cref="IUnitOfWork"/> فوق EF Core + SQLite.
    ///
    /// المهم هنا: المعاملة بتتفتح بـ <c>BEGIN IMMEDIATE</c> (يعني
    /// deferred: false) مش <c>BEGIN</c> العادية. الفرق مش شكلي:
    /// المعاملة العادية بتاخد قفل القراءة بس وبتأجل قفل الكتابة لأول
    /// INSERT — فنسختين بيتحققوا في نفس اللحظة الاتنين بيعدّوا التحقق
    /// وبعدين واحد بس بينجح في الكتابة (والتاني بياخد SQLITE_BUSY بعد
    /// ما يكون خد قراره على بيانات قديمة). بـ IMMEDIATE قفل الكتابة
    /// بيتاخد من أول لحظة، فالتحقق والكتابة الاتنين جوه نفس القفل.
    /// </summary>
    public class EfUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _context;

        public EfUnitOfWork(AppDbContext context) => _context = context;

        public async Task<IDataTransaction> BeginWriteTransactionAsync()
        {
            var connection = _context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await _context.Database.OpenConnectionAsync();

            if (_context.Database.CurrentTransaction is not null)
                return new NoOpTransaction();

            // SQLite بيدعم BEGIN IMMEDIATE عن طريق deferred: false — أي مزوّد
            // تاني (أو قاعدة بيانات داخل الذاكرة في الاختبارات) بيرجع لمعاملة
            // EF العادية، وساعتها الحماية بتيجي من الـ Serializable المعتاد
            var transaction = connection is SqliteConnection sqlite
                ? await _context.Database.UseTransactionAsync(
                    sqlite.BeginTransaction(IsolationLevel.Serializable, deferred: false))
                : await _context.Database.BeginTransactionAsync();

            return new EfDataTransaction(transaction!);
        }

        private sealed class NoOpTransaction : IDataTransaction
        {
            public Task CommitAsync() => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private sealed class EfDataTransaction : IDataTransaction
        {
            private readonly IDbContextTransaction _transaction;

            public EfDataTransaction(IDbContextTransaction transaction) => _transaction = transaction;

            public Task CommitAsync() => _transaction.CommitAsync();

            // من غير Commit الـ Dispose بيعمل Rollback تلقائي — فأي خروج
            // بسبب استثناء (تعارض، فشل تحقق...) بيسيب القاعدة زي ما كانت
            public ValueTask DisposeAsync() => _transaction.DisposeAsync();
        }
    }
}
