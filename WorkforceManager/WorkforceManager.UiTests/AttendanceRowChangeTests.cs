using WorkforceManager.Core.Enums;
using WorkforceManager.UI.ViewModels;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// <see cref="AttendanceRow.HasUnsavedChange"/> بقى **بيقرر إيه اللي
    /// يتكتب في قاعدة البيانات**، مش بس لون السطر — فالقاعدة دي محتاجة
    /// اختبار زي أي قاعدة حسابية.
    ///
    /// الخلفية: شاشة الحضور كانت بتبعت كل صف عليه حالة مع كل حفظ، فتعديل
    /// عامل واحد كان بيعيد كتابة الوردية كلها ويقول "تم حفظ حضور 13 عامل".
    /// ده كان بيخلي المستخدم يفتكر إن الباقي اتسجّل تاني، وكان بيعيد كمان
    /// حساب يوميات العمال بالساعة ومصالحة جزاءات الغياب لناس مالهمش دعوة.
    /// </summary>
    public class AttendanceRowChangeTests
    {
        private static AttendanceRow Row(
            AttendanceStatus? saved = null,
            int? savedEndHour = null,
            bool isHourly = false) =>
            new(workerId: 1, fullName: "أحمد", isHourly: isHourly, roleText: "بالقطعة")
            {
                SavedStatus = saved,
                SavedEndHour = savedEndHour
            };

        [Fact]
        public void AWorkerWhoseSavedStatusIsUnchanged_IsNotSentToTheDatabase()
        {
            var row = Row(saved: AttendanceStatus.Present);
            row.SelectStatusSilently(AttendanceStatus.Present);

            Assert.False(row.HasUnsavedChange);
        }

        [Fact]
        public void ChangingTheStatus_MarksTheRowForSaving()
        {
            var row = Row(saved: AttendanceStatus.AbsentWithoutPermission);
            row.SelectStatusSilently(AttendanceStatus.Present);

            Assert.True(row.HasUnsavedChange);
        }

        [Fact]
        public void AWorkerWithNoSavedRecordYet_CountsAsAChange()
        {
            // الحضور التلقائي للعامل اللي له شغل مسجّل — لسه متحفظش
            var row = Row(saved: null);
            row.SelectStatusSilently(AttendanceStatus.Present);

            Assert.True(row.HasUnsavedChange);
        }

        [Fact]
        public void AnHourlyWorkerWhoseShiftChanged_IsAChange_EvenWhenTheStatusDidNot()
        {
            // نفس الحالة (حاضر) بس الشيفت اتغيّر من 4 لـ 6 — ده بيغيّر
            // يومياته وأجره، فلازم يتحفظ
            var row = Row(saved: AttendanceStatus.Present, savedEndHour: 16, isHourly: true);
            row.SelectStatusSilently(AttendanceStatus.Present);
            row.SelectShiftSilently(18);

            Assert.True(row.HasUnsavedChange);
        }

        [Fact]
        public void AnHourlyWorkerWithTheSameShift_IsNotAChange()
        {
            var row = Row(saved: AttendanceStatus.Present, savedEndHour: 16, isHourly: true);
            row.SelectStatusSilently(AttendanceStatus.Present);
            row.SelectShiftSilently(16);

            Assert.False(row.HasUnsavedChange);
        }
    }
}
