using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// بوابة العمليات الحساسة — المكان الوحيد اللي بيتحقق من كلمة سر
    /// العمليات في البرنامج كله.
    ///
    /// أي شاشة عايزة تحذف حاجة أو تلمس فلوس بتنادي
    /// <see cref="VerifyAsync"/> وبتقف عند النتيجة. ممنوع أي شاشة تقارن
    /// كلمة سر بنفسها — لأن ساعتها قاعدة القفل بعد المحاولات الغلط
    /// هتبقى مطبّقة في مكان ومنساها في مكان تاني.
    ///
    /// أول تشغيل: لو مفيش كلمة سر متسجّلة، البوابة بتفتح من غير سؤال
    /// (<see cref="IsConfiguredAsync"/> = false) عشان البرنامج ميتقفلش
    /// قدام مصنع شغّال. الشاشة بتنبّه المستخدم إنه يحطّها من الإعدادات.
    /// </summary>
    public class OperationsPasswordService
    {
        /// <summary>عدد المحاولات الغلط المسموحة قبل القفل المؤقت</summary>
        public const int MaxFailedAttempts = 5;

        /// <summary>مدة القفل بالدقايق بعد استنفاد المحاولات</summary>
        public const int LockoutMinutes = 10;

        /// <summary>أقل طول مقبول لكلمة سر العمليات</summary>
        public const int MinPasswordLength = 4;

        private readonly IGenericRepository<OperationsCredential> _credentials;
        private readonly ActivityLogService _log;

        public OperationsPasswordService(
            IGenericRepository<OperationsCredential> credentials, ActivityLogService log)
        {
            _credentials = credentials;
            _log = log;
        }

        /// <summary>فيه كلمة سر عمليات متسجّلة؟</summary>
        public async Task<bool> IsConfiguredAsync() => await LoadAsync() is not null;

        /// <summary>
        /// يتحقق من كلمة السر لعملية معينة.
        ///
        /// النتيجة بتقول نجح ولا لأ **والسبب** — الشاشة بتعرض الرسالة زي
        /// ما هي، فمفيش نص خطأ متكرر في كل شاشة.
        /// </summary>
        /// <param name="action">
        /// العملية المطلوبة. متسجّلة عشان تظهر في رسالة الطلب ("كلمة السر
        /// مطلوبة لحذف عامل") وتدخل في سجل العمليات بعدين.
        /// </param>
        public async Task<OperationsGateResult> VerifyAsync(SensitiveAction action, string password)
        {
            var credential = await LoadAsync();

            // مفيش كلمة سر متسجّلة = البوابة مفتوحة. مقصود: البرنامج
            // موجود على أجهزة شغّالة من قبل الميزة دي، وقفلها فجأة كان
            // هيوقف المصنع
            if (credential is null)
                return OperationsGateResult.NotConfigured();

            if (credential.LockedUntil is { } until && until > DateTime.Now)
            {
                var minutes = Math.Max(1, (int)Math.Ceiling((until - DateTime.Now).TotalMinutes));
                return OperationsGateResult.Fail(
                    $"كلمة سر العمليات متقفلة بسبب محاولات غلط كتير — جرّب تاني بعد {minutes} دقيقة");
            }

            if (PasswordHasher.Verify(password, credential.PasswordHash, credential.PasswordSalt))
            {
                // نجاح بيصفّر العداد: المحاولات الغلط المتتالية هي اللي
                // بتقفل، مش المتفرقة على مدار اليوم
                if (credential.FailedAttempts != 0 || credential.LockedUntil is not null)
                {
                    credential.FailedAttempts = 0;
                    credential.LockedUntil = null;
                    _credentials.Update(credential);
                    await _credentials.SaveChangesAsync();
                }

                return OperationsGateResult.Success();
            }

            credential.FailedAttempts++;
            var remaining = MaxFailedAttempts - credential.FailedAttempts;

            if (remaining <= 0)
            {
                credential.LockedUntil = DateTime.Now.AddMinutes(LockoutMinutes);
                credential.FailedAttempts = 0;
            }

            _credentials.Update(credential);
            await _credentials.SaveChangesAsync();

            return OperationsGateResult.Fail(remaining <= 0
                ? $"كلمة سر غلط — اتقفلت {LockoutMinutes} دقايق بعد {MaxFailedAttempts} محاولات"
                : $"كلمة سر العمليات غلط — فاضل {remaining} محاولة");
        }

        /// <summary>
        /// يحطّ كلمة سر العمليات أول مرة أو يغيّرها.
        ///
        /// التغيير بيطلب الكلمة القديمة (لو فيه واحدة) — من غير كده أي
        /// حد يقعد على الجهاز يقدر يغيّرها ويعدّي البوابة كلها.
        /// </summary>
        public async Task SetPasswordAsync(string? currentPassword, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Trim().Length < MinPasswordLength)
                throw new InvalidOperationException(
                    $"كلمة سر العمليات لازم تكون {MinPasswordLength} حروف/أرقام على الأقل");

            var credential = await LoadAsync();

            if (credential is not null)
            {
                var check = await VerifyAsync(SensitiveAction.DeleteWorker, currentPassword ?? "");
                if (!check.IsAllowed)
                    throw new InvalidOperationException("كلمة سر العمليات الحالية غير صحيحة");
            }

            var (hash, salt) = PasswordHasher.Hash(newPassword.Trim());

            if (credential is null)
            {
                await _credentials.AddAsync(new OperationsCredential
                {
                    PasswordHash = hash,
                    PasswordSalt = salt,
                    UpdatedAt = DateTime.Now
                });
            }
            else
            {
                credential.PasswordHash = hash;
                credential.PasswordSalt = salt;
                credential.FailedAttempts = 0;
                credential.LockedUntil = null;
                credential.UpdatedAt = DateTime.Now;
                _credentials.Update(credential);
            }

            await _credentials.SaveChangesAsync();

            // دي البوابة اللي بتحمي كل العمليات التانية، فمين غيّرها
            // وإمتى جزء من نفس السؤال
            await _log.LogAsync(
                ActivityEventType.OperationsPasswordChanged, "OperationsCredential", 0,
                entityName: "كلمة سر العمليات",
                details: credential is null ? "اتحطت لأول مرة" : "اتغيّرت");
        }

        /// <summary>الصف الوحيد في الجدول (null = لسه ما اتسجلتش)</summary>
        private async Task<OperationsCredential?> LoadAsync() =>
            (await _credentials.GetAllAsync()).FirstOrDefault();
    }

    /// <summary>
    /// نتيجة البوابة. نوع خاص مش bool عشان سبب الرفض يوصل للشاشة —
    /// "كلمة غلط" و"متقفلة 10 دقايق" رسالتين مختلفتين للمستخدم.
    /// </summary>
    public class OperationsGateResult
    {
        /// <summary>العملية مسموح لها تكمّل</summary>
        public bool IsAllowed { get; private init; }

        /// <summary>مفيش كلمة سر متسجّلة أصلاً — عدّى وبلّغ المستخدم</summary>
        public bool IsNotConfigured { get; private init; }

        /// <summary>رسالة الرفض جاهزة للعرض (فاضية لو نجح)</summary>
        public string Message { get; private init; } = string.Empty;

        public static OperationsGateResult Success() => new() { IsAllowed = true };

        public static OperationsGateResult NotConfigured() =>
            new() { IsAllowed = true, IsNotConfigured = true };

        public static OperationsGateResult Fail(string message) =>
            new() { IsAllowed = false, Message = message };
    }
}
