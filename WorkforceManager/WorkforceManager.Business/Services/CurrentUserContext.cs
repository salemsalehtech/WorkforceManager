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

        /// <summary>اسم الدخول (null قبل تسجيل الدخول)</summary>
        public string? Username => _username;

        /// <summary>
        /// الاسم اللي بيتكتب في سجل العمليات وحقول الحذف.
        /// مبيرجّعش null أبدًا — السجل لازم يبقى ليه فاعل مهما حصل.
        /// </summary>
        public string ActorName => _displayName ?? _username ?? SystemActor;

        /// <summary>فيه حد داخل فعلاً؟</summary>
        public bool IsSignedIn => _username is not null;

        /// <summary>بيتنادى من شاشة الدخول بعد نجاح التحقق</summary>
        public void SignIn(string username, string? displayName)
        {
            _username = username;
            _displayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName;
        }

        /// <summary>
        /// بيمسح المستخدم الحالي.
        ///
        /// مفيش حد بينادي عليها لسه — قفل الخمول (اللي هيقفل الشاشة بعد
        /// مدة سكون ويطلب كلمة الدخول) هو اللي هيستخدمها. متسابة عن قصد
        /// مش سهو: حذفها وإرجاعها بعد أسبوع تغيير من غير فايدة.
        /// </summary>
        public void SignOut()
        {
            _username = null;
            _displayName = null;
        }
    }
}
