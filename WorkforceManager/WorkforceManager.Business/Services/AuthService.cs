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
            if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 4)
                throw new InvalidOperationException("كلمة المرور الجديدة لازم تكون 4 حروف/أرقام على الأقل");

            var user = await ValidateLoginAsync(username, currentPassword)
                ?? throw new InvalidOperationException("اسم المستخدم أو كلمة المرور الحالية غير صحيحة");

            // ملح جديد مع كل تغيير — الـ Hash القديم بيبقى ملوش أي قيمة
            var (hash, salt) = HashPassword(newPassword);
            user.PasswordHash = hash;
            user.PasswordSalt = salt;

            _users.Update(user);
            await _users.SaveChangesAsync();
        }

        // التشفير كله في PasswordHasher — مشترك مع كلمة سر العمليات عشان
        // ميحصلش إن واحدة تتقوّى والتانية تفضل ورا
        private static (string Hash, string Salt) HashPassword(string password) =>
            PasswordHasher.Hash(password);

        private static bool VerifyPassword(string password, string storedHash, string storedSalt) =>
            PasswordHasher.Verify(password, storedHash, storedSalt);
    }
}
