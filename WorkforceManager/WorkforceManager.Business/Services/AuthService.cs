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

        public AuthService(IGenericRepository<AppUser> users)
        {
            _users = users;
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
        /// </summary>
        public async Task<AppUser?> ValidateLoginAsync(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
                return null;

            var trimmed = username.Trim();
            var user = (await _users.FindAsync(u => u.Username == trimmed)).FirstOrDefault();
            if (user is null) return null;

            return VerifyPassword(password, user.PasswordHash, user.PasswordSalt) ? user : null;
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

        /// <summary>كل حسابات الدخول (لعرضها في الإعدادات)</summary>
        public async Task<IReadOnlyList<AppUser>> GetUsersAsync() =>
            (await _users.GetAllAsync()).OrderBy(u => u.Username).ToList();

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
