using WorkforceManager.Core.Enums;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// مين المستخدم اللي داخل دلوقتي — المصدر الوحيد للإجابة دي.
    ///
    /// قبل كده المستخدم كان بيتنسى فور نجاح الدخول، فأي حاجة محتاجة
    /// تعرف "مين عمل كده" مكانش قدامها غير إنها تسأل تاني أو تحطّ نص
    /// ثابت. دلوقتي الحذف الناعم وسجل العمليات الاتنين بيقروا من هنا،
    /// فاسم الفاعل بيفضل واحد في كل مكان.
    ///
    /// من هنا كمان بيتحدد **الحساب الإداري المرتبط** (لو الحساب ده حساب
    /// مدير/رئيس قسم) — ده اللي كلمة سر العمليات (OperationsPasswordService)
    /// وفرز الوصول في شاشة الحسابات الإدارية بيقيسوا عليه.
    ///
    /// Singleton في الـ DI: البرنامج ديسكتوب بمستخدم واحد داخل في المرة،
    /// فالحالة دي على مستوى التطبيق مش على مستوى الطلب.
    /// </summary>
    public class CurrentUserContext
    {
        /// <summary>
        /// الاسم المستخدم لما محدش يكون داخل — بيظهر في السجل لو حصلت
        /// عملية قبل الدخول (زي الترحيل التلقائي وقت التشغيل).
        /// </summary>
        public const string SystemActor = "النظام";

        private string? _username;
        private string? _displayName;
        private int? _appUserId;
        private int? _workerId;
        private HourlyRole? _departmentRole;

        /// <summary>اسم الدخول (null قبل تسجيل الدخول)</summary>
        public string? Username => _username;

        /// <summary>حساب الدخول (AppUser.Id) — null قبل تسجيل الدخول</summary>
        public int? AppUserId => _appUserId;

        /// <summary>الحساب الإداري (Worker.Id) المرتبط بحساب الدخول ده — null لحساب دخول عادي (لسه) مش مربوط</summary>
        public int? WorkerId => _workerId;

        /// <summary>دور الحساب الإداري (مدير/رئيس قسم) — null لحساب دخول مش مربوط بحساب إداري</summary>
        public HourlyRole? DepartmentRole => _departmentRole;

        /// <summary>الحساب الداخل مدير قسم؟ — ليه أكسس على كل الحسابات الإدارية</summary>
        public bool IsDepartmentManager => _departmentRole == HourlyRole.DepartmentManager;

        /// <summary>الحساب الداخل رئيس قسم؟ — بروفايله بس، مالوش أكسس على حسابات تانية</summary>
        public bool IsDepartmentHead => _departmentRole == HourlyRole.DepartmentHead;

        /// <summary>
        /// الاسم اللي بيتكتب في سجل العمليات وحقول الحذف — الاسم + وظيفته
        /// لو حساب إداري ("عمرو المهدي (مدير قسم)")، عشان خانة العمليات
        /// تقول مين عمل العملية وهو ايه بالظبط.
        /// مبيرجّعش null أبدًا — السجل لازم يبقى ليه فاعل مهما حصل.
        /// </summary>
        public string ActorName
        {
            get
            {
                var name = _displayName ?? _username ?? SystemActor;
                return _departmentRole is { } role ? $"{name} ({role.ToArabicName()})" : name;
            }
        }

        /// <summary>فيه حد داخل فعلاً؟</summary>
        public bool IsSignedIn => _username is not null;

        /// <summary>
        /// بيتنادى من شاشة الدخول بعد نجاح التحقق. <paramref name="appUserId"/>/
        /// <paramref name="workerId"/>/<paramref name="departmentRole"/> بيتحطوا
        /// null لحساب دخول عادي مش مربوط بحساب إداري.
        /// </summary>
        public void SignIn(
            string username, string? displayName,
            int? appUserId = null, int? workerId = null, HourlyRole? departmentRole = null)
        {
            _username = username;
            _displayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName;
            _appUserId = appUserId;
            _workerId = workerId;
            _departmentRole = departmentRole;
        }

        /// <summary>
        /// بيحدّث اسم الدخول/الاسم المعروض بس (بعد تغيير اسم الدخول من
        /// الإعدادات مثلاً) — من غير ما يلمس الهوية الإدارية
        /// (AppUserId/WorkerId/DepartmentRole) اللي اتحددت وقت الدخول
        /// الأصلي. <see cref="SignIn"/> نفسها كانت بتصفّرهم لو نوديناها
        /// من غير القيم دي، وده كان هيشيل صلاحيات المدير فور ما يغيّر
        /// اسم دخوله.
        /// </summary>
        public void UpdateDisplayName(string username, string? displayName)
        {
            _username = username;
            _displayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName;
        }

        /// <summary>
        /// بيمسح المستخدم الحالي — بينادي عليها زرار "تسجيل الخروج" في
        /// القايمة الجانبية (MainWindow) قبل ما يعرض شاشة الدخول تاني.
        /// </summary>
        public void SignOut()
        {
            _username = null;
            _displayName = null;
            _appUserId = null;
            _workerId = null;
            _departmentRole = null;
        }
    }
}
