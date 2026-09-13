using System.Security.Cryptography;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// تشفير كلمات المرور والتحقق منها — القاعدة الوحيدة في البرنامج.
    ///
    /// بتستخدمها كلمة سر الدخول (<see cref="AuthService"/>) وكلمة سر
    /// العمليات (<see cref="OperationsPasswordService"/>). لو كل واحدة
    /// كتبت نسختها، كان ممكن واحدة تتحدّث لعدد تكرارات أعلى والتانية
    /// تفضل ضعيفة من غير ما حد ياخد باله.
    ///
    /// PBKDF2-SHA256 بملح عشوائي لكل كلمة سر، ومقارنة ثابتة الوقت.
    /// </summary>
    public static class PasswordHasher
    {
        /// <summary>عدد التكرارات — رقم عالي بيخلي تخمين كلمة المرور بطيء جدًا</summary>
        public const int Iterations = 100_000;

        private const int SaltSize = 16;
        private const int HashSize = 32;

        /// <summary>يشفّر كلمة مرور بملح عشوائي جديد، ويرجّع الاتنين Base64 للتخزين</summary>
        public static (string Hash, string Salt) Hash(string password)
        {
            var saltBytes = RandomNumberGenerator.GetBytes(SaltSize);
            var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
                password, saltBytes, Iterations, HashAlgorithmName.SHA256, HashSize);

            return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
        }

        /// <summary>
        /// يتحقق من كلمة مرور مقابل الـ Hash والملح المخزّنين.
        ///
        /// بيرجّع false لو البيانات المخزّنة متبوّظة (Base64 غلط) بدل ما
        /// يرمي: بيانات تالفة معناها "مش مطابقة"، مش انهيار البرنامج.
        /// </summary>
        public static bool Verify(string password, string storedHash, string storedSalt)
        {
            try
            {
                var saltBytes = Convert.FromBase64String(storedSalt);
                var computed = Rfc2898DeriveBytes.Pbkdf2(
                    password, saltBytes, Iterations, HashAlgorithmName.SHA256, HashSize);

                // مقارنة ثابتة الوقت — بتمنع استنتاج كلمة المرور من زمن المقارنة
                return CryptographicOperations.FixedTimeEquals(
                    computed, Convert.FromBase64String(storedHash));
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
