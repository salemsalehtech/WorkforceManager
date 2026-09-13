using System.Security.Cryptography;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// مسؤولة عن تسجيل الدخول وإدارة كلمات المرور. كلمة المرور عمرها ما
    /// بتتخزن كنص — بنخزن ناتج تشفيرها بخوارزمية PBKDF2 (تكرار عالي +
    /// ملح عشوائي لكل مستخدم)، وهي نفس الطريقة المعتمدة في الأنظمة
    /// الاحترافية: حتى لو حد وصل لملف قاعدة البيانات مش هيعرف كلمات المرور.
    /// </summary>
    public class AuthService
    {
        /// <summary>بيانات الدخول الافتراضية لأول تشغيل (لازم تتغير بعد أول دخول)</summary>
        public const string DefaultUsername = "admin";
        public const string DefaultPassword = "admin";

        /// <summary>أقل طول لاسم المستخدم</summary>
        public const int MinUsernameLength = 3;

        /// <summary>أقل طول لكلمة المرور — نفس الحد في كل مسارات التغيير</summary>
        public const int MinPasswordLength = 4;

        private readonly IGenericRepository<AppUser> _users;
        private readonly IWorkerRepository _workers;

        public AuthService(IGenericRepository<AppUser> users, IWorkerRepository workers)
        {
            _users = users;
            _workers = workers;
        }

        /// <summary>
        /// أول تشغيل للبرنامج: لو مفيش أي مستخدمين، بينشئ حساب المدير
        /// الافتراضي (admin / admin) عشان الدخول ميتقفلش قدام المستخدم.
        /// </summary>
        public async Task EnsureDefaultUserAsync()
        {
            var users = await _users.GetAllAsync();
            if (users.Count > 0) return;

            var (hash, salt) = HashPassword(DefaultPassword);
            await _users.AddAsync(new AppUser
            {
                Username = DefaultUsername,
                PasswordHash = hash,
                PasswordSalt = salt,
                DisplayName = "مدير القسم"
            });
            await _users.SaveChangesAsync();
        }

        /// <summary>
        /// يتحقق من بيانات الدخول: بيرجع المستخدم لو صحيحة، أو null لو
        /// غلط — من غير ما يفرّق في الرسالة بين "الاسم غلط" و"الباسورد
        /// غلط" (معلومة زيادة للمتطفلين).
        ///
        /// لو الحساب ده مربوط بحساب إداري موقوف (Worker.IsActive = false،
        /// عن طريق DeactivateWorkerAsync أو "إيقاف" من شاشة الحسابات
        /// الإدارية)، الدخول بيترفض برسالة الخطأ العادية — إيقاف الحساب
        /// معناه هو نفسه ميقدرش يستخدم البرنامج، مش بس ميظهرش في القوايم.
        /// </summary>
        public async Task<AppUser?> ValidateLoginAsync(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
                return null;

            var trimmed = username.Trim();
            var user = (await _users.FindAsync(u => u.Username == trimmed)).FirstOrDefault();
            if (user is null) return null;

            if (!VerifyPassword(password, user.PasswordHash, user.PasswordSalt)) return null;

            if (user.WorkerId is { } workerId)
            {
                var worker = await _workers.GetByIdAsync(workerId);
                if (worker is null || !worker.IsActive) return null;
            }

            return user;
        }

        /// <summary>
        /// يغيّر كلمة مرور مستخدم بعد التحقق من كلمته الحالية.
        /// بيرمي استثناء برسالة واضحة لو الحالية غلط أو الجديدة ضعيفة.
        /// </summary>
        public async Task ChangePasswordAsync(string username, string currentPassword, string newPassword)
        {
            if (string.IsNullOrEmpty(newPassword) || newPassword.Length < MinPasswordLength)
                throw new InvalidOperationException(
                    $"كلمة المرور الجديدة لازم تكون {MinPasswordLength} حروف/أرقام على الأقل");

            var user = await ValidateLoginAsync(username, currentPassword)
                ?? throw new InvalidOperationException("اسم المستخدم أو كلمة المرور الحالية غير صحيحة");

            // ملح جديد مع كل تغيير — الـ Hash القديم بيبقى ملوش أي قيمة
            var (hash, salt) = HashPassword(newPassword);
            user.PasswordHash = hash;
            user.PasswordSalt = salt;

            _users.Update(user);
            await _users.SaveChangesAsync();
        }

        /// <summary>
        /// يغيّر اسم دخول مستخدم بعد التحقق من كلمة مروره الحالية.
        ///
        /// كلمة المرور مطلوبة رغم إن العملية مش بتلمس فلوس: تغيير اسم
        /// الدخول من جهاز مفتوح ومسيّب معناه إن صاحب الحساب يقفل بره
        /// حسابه من غير ما يعرف السبب.
        /// </summary>
        public async Task<AppUser> ChangeUsernameAsync(
            string currentUsername, string currentPassword, string newUsername)
        {
            var trimmed = (newUsername ?? "").Trim();
            if (trimmed.Length < MinUsernameLength)
                throw new InvalidOperationException(
                    $"اسم المستخدم لازم يكون {MinUsernameLength} حروف على الأقل");

            var user = await ValidateLoginAsync(currentUsername, currentPassword)
                ?? throw new InvalidOperationException("اسم المستخدم أو كلمة المرور الحالية غير صحيحة");

            if (string.Equals(user.Username, trimmed, StringComparison.OrdinalIgnoreCase))
                return user; // نفس الاسم — مفيش حاجة تتعمل

            await EnsureUsernameIsFreeAsync(trimmed);

            user.Username = trimmed;
            _users.Update(user);
            await _users.SaveChangesAsync();
            return user;
        }

        /// <summary>
        /// يضيف حساب دخول تاني.
        ///
        /// حساب عادي زي الأول بالظبط — **مفيش أدوار ولا صلاحيات**. اللي
        /// بيحمي العمليات الخطيرة هو كلمة سر العمليات مش نوع الحساب،
        /// فحساب بصلاحيات أقل كان هيدّي إحساس أمان مش موجود.
        /// </summary>
        public async Task<AppUser> AddUserAsync(string username, string password, string? displayName = null)
        {
            var trimmed = (username ?? "").Trim();
            if (trimmed.Length < MinUsernameLength)
                throw new InvalidOperationException(
                    $"اسم المستخدم لازم يكون {MinUsernameLength} حروف على الأقل");

            if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
                throw new InvalidOperationException(
                    $"كلمة المرور لازم تكون {MinPasswordLength} حروف/أرقام على الأقل");

            await EnsureUsernameIsFreeAsync(trimmed);

            var (hash, salt) = HashPassword(password);
            var user = new AppUser
            {
                Username = trimmed,
                PasswordHash = hash,
                PasswordSalt = salt,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? trimmed : displayName.Trim()
            };

            await _users.AddAsync(user);
            await _users.SaveChangesAsync();
            return user;
        }

        /// <summary>كل حسابات الدخول (لعرضها في شاشة الحسابات الإدارية)</summary>
        public async Task<IReadOnlyList<AppUser>> GetUsersAsync() =>
            (await _users.GetAllAsync()).OrderBy(u => u.Username).ToList();

        /// <summary>حساب الدخول المرتبط بحساب إداري معيّن (null لو لسه ماعندوش)</summary>
        public async Task<AppUser?> GetUserByWorkerIdAsync(int workerId) =>
            (await _users.FindAsync(u => u.WorkerId == workerId)).FirstOrDefault();

        /// <summary>
        /// يشيل حساب دخول نهائيًا — بينادى لما حساب إداري (Worker) بتاعه
        /// يتحذف حذف فعلي نهائي (WasPermanent)، عشان مايفضلش حساب دخول
        /// معلّق من غير حساب إداري يقدر حد يستخدمه بالغلط. لو الحذف كان
        /// إيقاف بس (Worker.IsActive = false)، الحساب ده بيترفض دخوله
        /// من ValidateLoginAsync لوحدها من غير ما يتحذف — رجوعه بعد
        /// إعادة التفعيل محتاج يفضل ممكن.
        /// </summary>
        public async Task DeleteUserForWorkerAsync(int workerId)
        {
            var user = await GetUserByWorkerIdAsync(workerId);
            if (user is null) return;

            _users.Remove(user);
            await _users.SaveChangesAsync();
        }

        /// <summary>
        /// يضيف حساب دخول لحساب إداري (مدير/رئيس قسم) — بيوزر وباسورد
        /// خاصين بيه، ومربوط بـ WorkerId من لحظة إنشائه.
        /// </summary>
        public async Task<AppUser> AddUserForWorkerAsync(
            int workerId, string username, string password, string? displayName = null)
        {
            var user = await AddUserAsync(username, password, displayName);
            user.WorkerId = workerId;
            _users.Update(user);
            await _users.SaveChangesAsync();
            return user;
        }

        /// <summary>
        /// يغيّر كلمة مرور حساب دخول **بدون** التحقق من كلمته الحالية —
        /// تصحيح إداري (مدير القسم بيصلّح حساب رئيس قسم نسي كلمة سره
        /// مثلاً)، مش تغيير المستخدم لكلمته هو نفسه (ده
        /// <see cref="ChangePasswordAsync"/>).
        /// </summary>
        public async Task SetPasswordForUserAsync(int appUserId, string newPassword)
        {
            if (string.IsNullOrEmpty(newPassword) || newPassword.Length < MinPasswordLength)
                throw new InvalidOperationException(
                    $"كلمة المرور لازم تكون {MinPasswordLength} حروف/أرقام على الأقل");

            var user = await _users.GetByIdAsync(appUserId)
                ?? throw new InvalidOperationException("حساب الدخول غير موجود");

            var (hash, salt) = HashPassword(newPassword);
            user.PasswordHash = hash;
            user.PasswordSalt = salt;

            _users.Update(user);
            await _users.SaveChangesAsync();
        }

        /// <summary>
        /// يغيّر اسم دخول حساب **بدون** التحقق من كلمة مروره — تصحيح
        /// إداري زي <see cref="SetPasswordForUserAsync"/> بالظبط.
        /// </summary>
        public async Task<AppUser> SetUsernameForUserAsync(int appUserId, string newUsername)
        {
            var trimmed = (newUsername ?? "").Trim();
            if (trimmed.Length < MinUsernameLength)
                throw new InvalidOperationException(
                    $"اسم المستخدم لازم يكون {MinUsernameLength} حروف على الأقل");

            var user = await _users.GetByIdAsync(appUserId)
                ?? throw new InvalidOperationException("حساب الدخول غير موجود");

            if (string.Equals(user.Username, trimmed, StringComparison.OrdinalIgnoreCase))
                return user;

            await EnsureUsernameIsFreeAsync(trimmed);

            user.Username = trimmed;
            _users.Update(user);
            await _users.SaveChangesAsync();
            return user;
        }

        /// <summary>
        /// الاسم متاح؟ المقارنة بتتجاهل حالة الحروف عشان "Admin" و"admin"
        /// ميبقوش حسابين مختلفين — والمستخدم اللي هيكتب واحد منهم
        /// مش هيعرف ليه مش بيدخل.
        /// </summary>
        private async Task EnsureUsernameIsFreeAsync(string username)
        {
            var taken = (await _users.GetAllAsync())
                .Any(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

            if (taken)
                throw new InvalidOperationException($"اسم المستخدم \"{username}\" مستخدم بالفعل");
        }

        // التشفير كله في PasswordHasher — مشترك مع كلمة سر العمليات عشان
        // ميحصلش إن واحدة تتقوّى والتانية تفضل ورا
        private static (string Hash, string Salt) HashPassword(string password) =>
            PasswordHasher.Hash(password);

        private static bool VerifyPassword(string password, string storedHash, string storedSalt) =>
            PasswordHasher.Verify(password, storedHash, storedSalt);
    }
}
