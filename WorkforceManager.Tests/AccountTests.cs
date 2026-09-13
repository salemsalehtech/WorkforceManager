using WorkforceManager.Business.Services;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// حسابات الدخول: تغيير الاسم، تغيير كلمة المرور، وإضافة حساب تاني.
    ///
    /// أخطر حاجة هنا إن غلطة تقفل المستخدم بره برنامجه: اسم اتغيّر لاسم
    /// محجوز، أو كلمة مرور اتغيّرت من غير التحقق من القديمة. كل مسار
    /// بيعدّل حساب لازم يتحقق من كلمة المرور الحالية الأول.
    /// </summary>
    public class AccountTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private const string Password = "admin";

        /// <summary>بينشئ حساب المدير الافتراضي (admin/admin) زي أول تشغيل</summary>
        private async Task<AuthService> AuthAsync(IServiceScopeHolder scope)
        {
            var auth = _db.GetService<AuthService>(scope.Scope);
            await auth.EnsureDefaultUserAsync();
            return auth;
        }

        /// <summary>غلاف بسيط عشان الـ scope يتقفل صح مع الاستخدام</summary>
        private sealed class IServiceScopeHolder : IDisposable
        {
            public IServiceScopeHolder(Microsoft.Extensions.DependencyInjection.IServiceScope scope) => Scope = scope;
            public Microsoft.Extensions.DependencyInjection.IServiceScope Scope { get; }
            public void Dispose() => Scope.Dispose();
        }

        private IServiceScopeHolder NewScope() => new(_db.CreateScope());

        // ======================= تغيير اسم المستخدم =======================

        [Fact]
        public async Task Changing_the_username_needs_the_current_password()
        {
            using var scope = NewScope();
            var auth = await AuthAsync(scope);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                auth.ChangeUsernameAsync(AuthService.DefaultUsername, "غلط", "salem"));

            // الاسم القديم لسه شغال
            Assert.NotNull(await auth.ValidateLoginAsync(AuthService.DefaultUsername, Password));
        }

        [Fact]
        public async Task Changing_the_username_lets_the_user_log_in_with_the_new_one()
        {
            using var scope = NewScope();
            var auth = await AuthAsync(scope);

            await auth.ChangeUsernameAsync(AuthService.DefaultUsername, Password, "salem");

            Assert.NotNull(await auth.ValidateLoginAsync("salem", Password));
            Assert.Null(await auth.ValidateLoginAsync(AuthService.DefaultUsername, Password));
        }

        [Fact]
        public async Task A_too_short_username_is_refused()
        {
            using var scope = NewScope();
            var auth = await AuthAsync(scope);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                auth.ChangeUsernameAsync(AuthService.DefaultUsername, Password, "ab"));
        }

        [Fact]
        public async Task A_username_that_is_already_taken_is_refused()
        {
            using var scope = NewScope();
            var auth = await AuthAsync(scope);

            await auth.AddUserAsync("salem", "1234");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                auth.ChangeUsernameAsync(AuthService.DefaultUsername, Password, "salem"));

            Assert.Contains("مستخدم بالفعل", ex.Message);
        }

        [Fact]
        public async Task Username_comparison_ignores_letter_case()
        {
            // "Admin" و"admin" لازم يبقوا نفس الاسم — وإلا المستخدم اللي
            // هيكتب واحد منهم مش هيعرف ليه مش بيدخل
            using var scope = NewScope();
            var auth = await AuthAsync(scope);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                auth.AddUserAsync("ADMIN", "1234"));
        }

        // ======================= تغيير كلمة المرور =======================

        [Fact]
        public async Task Changing_the_login_password_needs_the_current_one()
        {
            using var scope = NewScope();
            var auth = await AuthAsync(scope);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                auth.ChangePasswordAsync(AuthService.DefaultUsername, "غلط", "جديدة123"));

            Assert.NotNull(await auth.ValidateLoginAsync(AuthService.DefaultUsername, Password));
        }

        [Fact]
        public async Task The_new_password_replaces_the_old_one()
        {
            using var scope = NewScope();
            var auth = await AuthAsync(scope);

            await auth.ChangePasswordAsync(AuthService.DefaultUsername, Password, "جديدة123");

            Assert.NotNull(await auth.ValidateLoginAsync(AuthService.DefaultUsername, "جديدة123"));
            Assert.Null(await auth.ValidateLoginAsync(AuthService.DefaultUsername, Password));
        }

        [Fact]
        public async Task A_too_short_password_is_refused()
        {
            using var scope = NewScope();
            var auth = await AuthAsync(scope);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                auth.ChangePasswordAsync(AuthService.DefaultUsername, Password, "12"));
        }

        // ======================= حساب تاني =======================

        [Fact]
        public async Task A_second_account_can_log_in_independently()
        {
            using var scope = NewScope();
            var auth = await AuthAsync(scope);

            await auth.AddUserAsync("salem", "1234", "سالم صالح");

            // الاتنين شغالين — الحساب الجديد مش بديل للأول
            Assert.NotNull(await auth.ValidateLoginAsync("salem", "1234"));
            Assert.NotNull(await auth.ValidateLoginAsync(AuthService.DefaultUsername, Password));
        }

        [Fact]
        public async Task Changing_one_account_password_never_touches_the_other()
        {
            using var scope = NewScope();
            var auth = await AuthAsync(scope);

            await auth.AddUserAsync("salem", "1234");
            await auth.ChangePasswordAsync("salem", "1234", "9999");

            Assert.NotNull(await auth.ValidateLoginAsync("salem", "9999"));
            Assert.NotNull(await auth.ValidateLoginAsync(AuthService.DefaultUsername, Password));
        }

        [Fact]
        public async Task Accounts_are_listed_for_the_settings_screen()
        {
            using var scope = NewScope();
            var auth = await AuthAsync(scope);

            await auth.AddUserAsync("salem", "1234");

            var users = await auth.GetUsersAsync();
            Assert.Equal(2, users.Count);
        }

        [Fact]
        public async Task Two_accounts_can_share_a_password_without_sharing_a_hash()
        {
            // الملح عشوائي لكل مستخدم، فنفس كلمة المرور بتدي هاش مختلف —
            // من غير كده كسر حساب واحد بيكسر كل اللي بنفس الكلمة
            using var scope = NewScope();
            var auth = await AuthAsync(scope);

            var first = await auth.AddUserAsync("salem", "نفس_الكلمة");
            var second = await auth.AddUserAsync("ahmed", "نفس_الكلمة");

            Assert.NotEqual(first.PasswordHash, second.PasswordHash);
            Assert.NotEqual(first.PasswordSalt, second.PasswordSalt);
        }
    }
}
