using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// توقيع نهاية اليوم — بديل طلب باسورد العمليات في كل حفظ/تعديل/حذف
    /// روتيني على حدة (Tier B، شوف SensitiveAction). المستخدم بيشتغل طول
    /// اليوم بحرية، وفي الآخر بيراجع كل حاجة حصلت ويوقّع عليها بباسورد
    /// عملياته مرة واحدة — التوقيع علم **عالمي** لكل تاريخ تقويمي، مش لكل
    /// حساب دخول لوحده (شوف DailyOperationsSignOff).
    ///
    /// الأفعال الخطيرة فعلاً (حذف عامل، تعديل الأجر، إعدادات النظام،
    /// الحركات المالية) لسه بتاخد باسورد فوري من بوابتها القديمة (Tier A)
    /// ومالهاش أي علاقة بالخدمة دي.
    /// </summary>
    public class DailyOperationsSignOffService
    {
        private readonly IDailyOperationsSignOffRepository _repo;
        private readonly ActivityLogService _log;
        private readonly OperationsPasswordService _gate;
        private readonly IUnitOfWork _unitOfWork;

        public DailyOperationsSignOffService(
            IDailyOperationsSignOffRepository repo, ActivityLogService log,
            OperationsPasswordService gate, IUnitOfWork unitOfWork)
        {
            _repo = repo;
            _log = log;
            _gate = gate;
            _unitOfWork = unitOfWork;
        }

        /// <summary>اليوم ده موقّع؟</summary>
        public Task<bool> IsSignedOffAsync(DateTime date) => _repo.IsSignedOffAsync(date);

        /// <summary>
        /// اليوم ده **مغطّى بالكامل** بتوقيع؟ يعني فيه توقيع، و**مفيش أي
        /// عملية اتسجّلت بعده**.
        ///
        /// ده السؤال الحقيقي اللي منع الإغلاق بيتبني عليه، مش مجرد "فيه
        /// صف توقيع ولا لأ": المستخدم ممكن يوقّع 6:37 وبعدين يحذف إنتاج
        /// يوم 8:00 — الحذف ده مالوش أي إمضاء، والتوقيع القديم مش بيغطيه
        /// (اتوقّع قبل ما يحصل أصلاً). فأي نشاط بعد آخر توقيع بيرجّع اليوم
        /// "مش موقّع" لحد ما يتوقّع تاني.
        ///
        /// أحداث <see cref="ActivityEventType.DaySignedOff"/> نفسها مستثناة:
        /// حدث التوقيع بيتكتب **بعد** ما الصف يتحفظ بأجزاء من الثانية
        /// (LogAsync بعد الـ commit)، فمن غير الاستثناء ده كل توقيع كان
        /// هيبطّل نفسه فورًا ويطلب توقيع تاني للأبد.
        /// </summary>
        public async Task<bool> IsFullySignedOffAsync(DateTime date)
        {
            var signOff = await _repo.GetByDateAsync(date);
            if (signOff is null) return false;

            return (await GetActivitySinceLastSignOffAsync(date)).Count == 0;
        }

        /// <summary>
        /// العمليات اللي حصلت في اليوم ده و**لسه محتاجة توقيع**: اللي بعد
        /// آخر توقيع، أو كلها لو اليوم ما اتوقّعش خالص. ده اللي بيتعرض في
        /// ملخص المراجعة قبل التوقيع — عرض عمليات موقّعة خلاص كان هيخلي
        /// المستخدم يوقّع على نفس الحاجة مرتين من غير ما يعرف.
        /// </summary>
        public async Task<IReadOnlyList<ActivityEvent>> GetActivitySinceLastSignOffAsync(DateTime date)
        {
            var signOff = await _repo.GetByDateAsync(date);
            var events = await _log.GetByRangeAsync(date, date);

            return events
                .Where(e => e.EventType != ActivityEventType.DaySignedOff)
                .Where(e => signOff is null || e.OccurredAt > signOff.SignedOffAt)
                .OrderByDescending(e => e.OccurredAt)
                .ToList();
        }

        /// <summary>
        /// كل الأيام الفايتة (قبل <paramref name="today"/>) اللي عليها نشاط
        /// ومحدش وقّعها — دي اللي بتظهر في ديالوج اللحاق وقت بدء التشغيل.
        /// اليوم نفسه مستثنى عن قصد: لسه شغال، والتوقيع عليه بيحصل عند
        /// الإغلاق مش هنا.
        ///
        /// "اليوم" بياخده المستدعي (زي كل تاريخ تاني في الطبقة دي) بدل ما
        /// الخدمة تقرا الساعة بنفسها — نفس السبب إن DayClosureService مالوش
        /// DateTime.Today جوّاه: يفضل قابل للاختبار بتاريخ ثابت بدل ما يتغيّر
        /// كل يوم يتشغّل فيه الاختبار.
        ///
        /// **بذرة الـ cutover التلقائية**: أول استدعاء على قاعدة بيانات
        /// الجدول فيها فاضي بالكامل (أول تشغيل بعد الـ Migration، وممكن
        /// يكون فيه سنين من التاريخ القديم قبل الميزة دي) بيسجّل يوم قبل
        /// <paramref name="today"/> كموقّع تلقائيًا — من غيرها كان هيضطر
        /// يعرض آلاف الأيام القديمة في ديالوج حاجز أول ما البرنامج يفتح،
        /// وده مستحيل يتعمل. نفس فلسفة HistoricalPendingMigrationService:
        /// مرة واحدة، idempotent، وبتحصل قبل أي فحص تاني. مفيش تسجيل
        /// Activity Log للبذرة دي — مش توقيع مستخدم حقيقي، مجرد نقطة بداية.
        /// </summary>
        public async Task<IReadOnlyList<DateTime>> GetUnsignedPastDatesAsync(DateTime today)
        {
            today = today.Date;
            var mostRecent = await _repo.GetMostRecentDateAsync();

            if (mostRecent is null)
            {
                var cutoverDate = today.AddDays(-1);
                await _repo.AddAsync(new DailyOperationsSignOff
                {
                    Date = cutoverDate,
                    SignedOffAt = DateTime.Now
                });
                await _repo.SaveChangesAsync();
                mostRecent = cutoverDate;
            }

            // **بيبدأ من يوم آخر توقيع نفسه، مش من اللي بعده**: اليوم اللي
            // اتوقّع الساعة 6 وحصل عليه شغل الساعة 8 لسه محتاج توقيع تاني
            // (نفس قاعدة IsFullySignedOffAsync)، فاستبعاده هنا كان هيخلي
            // شغل زي ده يعدّي من غير أي إمضاء للأبد
            var from = mostRecent.Value;
            var to = today.AddDays(-1);
            if (from > to) return Array.Empty<DateTime>();

            var events = await _log.GetByRangeAsync(from, to);
            var signedAtByDate = (await _repo.FindAsync(s => s.Date >= from && s.Date <= to))
                .ToDictionary(s => s.Date, s => s.SignedOffAt);

            return events
                .Where(e => e.EventType != ActivityEventType.DaySignedOff)
                .Where(e => !signedAtByDate.TryGetValue(e.OccurredAt.Date, out var signedAt)
                            || e.OccurredAt > signedAt)
                .Select(e => e.OccurredAt.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();
        }

        /// <summary>
        /// يوقّع على يوم (عادة النهارده، عند الضغط على "حفظ نهائي" أو عند
        /// إغلاق البرنامج). باسورد المستخدم الحالي — أي حساب دخول بباسورد
        /// عملياته يقدر يوقّع، والتوقيع بيقفل اليوم للتطبيق كله.
        ///
        /// **التوقيع بيتحدّث مش بيترفض لو اليوم موقّع قبل كده**: الشغل
        /// اللي بيحصل بعد توقيع بيرجّع اليوم "مش موقّع"
        /// (<see cref="IsFullySignedOffAsync"/>)، فلازم ينفع يتوقّع تاني.
        /// الصف بيفضل واحد لكل يوم (الفهرس الفريد على Date) وبيحمل **آخر**
        /// وقت توقيع؛ سلسلة التوقيعات كاملة عايشة في سجل العمليات، اللي هو
        /// الأثر المقصود أصلاً.
        /// </summary>
        public async Task<DailyOperationsSignOff> SignOffAsync(DateTime date, string operationsPassword)
        {
            var gate = await _gate.VerifyAsync(SensitiveAction.DailySignOff, operationsPassword);
            if (!gate.IsAllowed)
                throw new InvalidOperationException(gate.Message);

            await using var transaction = await _unitOfWork.BeginWriteTransactionAsync();

            var existing = await _repo.GetByDateAsync(date);
            var isResign = existing is not null;

            var signOff = existing ?? new DailyOperationsSignOff { Date = date.Date };
            signOff.SignedOffAt = DateTime.Now;

            if (isResign) _repo.Update(signOff);
            else await _repo.AddAsync(signOff);

            await _repo.SaveChangesAsync();
            await transaction.CommitAsync();

            await _log.LogAsync(
                ActivityEventType.DaySignedOff, "DailyOperationsSignOff", signOff.Id,
                entityName: $"يوم {date:yyyy/MM/dd}",
                details: isResign ? "توقيع تاني — حصل شغل بعد التوقيع اللي قبله" : null);

            return signOff;
        }

        /// <summary>
        /// لحاق أيام فايتة ما اتوقّعتش (البرنامج قفل فجأة قبل التوقيع) —
        /// باسورد واحد بيغطي كل الأيام دفعة واحدة، مش باسورد لكل يوم.
        /// </summary>
        public async Task AcknowledgeLateAsync(IReadOnlyList<DateTime> dates, string operationsPassword)
        {
            if (dates.Count == 0) return;

            var gate = await _gate.VerifyAsync(SensitiveAction.DailySignOff, operationsPassword);
            if (!gate.IsAllowed)
                throw new InvalidOperationException(gate.Message);

            await using var transaction = await _unitOfWork.BeginWriteTransactionAsync();

            var now = DateTime.Now;
            var signOffs = new List<DailyOperationsSignOff>();
            var fresh = new List<DailyOperationsSignOff>();

            // نفس منطق SignOffAsync: اليوم اللي عليه توقيع قديم وحصل بعده
            // شغل بيوصل هنا كمان، فلازم نحدّث توقيعه مش نضيف صف تاني
            // (الفهرس الفريد على Date هيرفض التكرار أصلاً)
            foreach (var date in dates.Select(d => d.Date).Distinct())
            {
                var existing = await _repo.GetByDateAsync(date);
                if (existing is not null)
                {
                    existing.SignedOffAt = now;
                    _repo.Update(existing);
                    signOffs.Add(existing);
                }
                else
                {
                    var created = new DailyOperationsSignOff { Date = date, SignedOffAt = now };
                    fresh.Add(created);
                    signOffs.Add(created);
                }
            }

            if (fresh.Count > 0) await _repo.AddRangeAsync(fresh);
            await _repo.SaveChangesAsync();
            await transaction.CommitAsync();

            foreach (var signOff in signOffs)
                await _log.LogAsync(
                    ActivityEventType.DaySignedOff, "DailyOperationsSignOff", signOff.Id,
                    entityName: $"يوم {signOff.Date:yyyy/MM/dd}",
                    details: "توقيع لاحق — البرنامج قفل قبل ما يتوقّع اليوم ده في وقته");
        }
    }
}
