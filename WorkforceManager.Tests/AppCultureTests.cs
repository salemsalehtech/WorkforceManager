using System.Globalization;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// ثقافة ar-EG الافتراضية (المستخدمة عشان التقويم الميلادي وأسماء الشهور
    /// العربية — شوف AppCulture) جايبة معاها فاصلة عشرية "٫" وفاصلة آلاف "٬"
    /// وعلامة سالب فيها حرف اتجاه مخفي، فأي رقم في الشاشة (صافي أيام العمل،
    /// الأجور) كان بيظهر بفواصل غريبة على أي مكان بيستخدم $"{...}" أو
    /// .ToString() من غير تنسيق صريح — يعني كل شاشة تقريبًا.
    ///
    /// الاختبارات دي بتقفل الرجوع للعطل: لو أي تعديل مستقبلي رجّع
    /// AppCulture.Build() لثقافة ar-EG الخام من غير التصحيح، الأرقام
    /// تترجع تتكسر تاني في كل الشاشات مرة واحدة.
    /// </summary>
    public class AppCultureTests
    {
        [Fact]
        public void DecimalsUseAPlainDot_NotTheArabicDecimalSeparator()
        {
            var culture = AppCulture.Build();

            Assert.Equal("4.5", (4.5m).ToString("0.##", culture));
        }

        [Fact]
        public void ThousandsUseAPlainComma_NotTheArabicThousandsSeparator()
        {
            var culture = AppCulture.Build();

            Assert.Equal("5,060", (5060m).ToString("N0", culture));
        }

        [Fact]
        public void NegativeNumbersUseAPlainHyphen_WithNoHiddenDirectionMark()
        {
            // ar-EG الخام بيحط U+061C (علامة اتجاه مخفية) قبل الشرطة —
            // بتبان زي رقم متكسر أو محل السالب مش واضح
            var culture = AppCulture.Build();

            Assert.Equal("-4.5", (-4.5m).ToString("0.##", culture));
        }

        [Fact]
        public void DigitShapesStayEuropean_RegardlessOfSurroundingArabicText()
        {
            // ar-EG الخام: DigitSubstitution = Context — يعني WPF بيرسم شكل
            // الرقم حسب النص المحيط، وكل نص في التطبيق عربي RTL، فحتى مع
            // فواصل سليمة كان الرقم لسه بيتشكّل "٤" بصريًا وقت الرسم. None
            // يجبر الشكل الأوروبي دايمًا بصرف النظر عن السياق.
            var culture = AppCulture.Build();

            Assert.Equal(DigitShapes.None, culture.NumberFormat.DigitSubstitution);
        }

        [Fact]
        public void TheCalendarStaysGregorian()
        {
            // التصحيح لازم يمسّ الأرقام بس، والسبب الأصلي لتثبيت الثقافة
            // (التقويم الهجري بيبوّظ اسم النسخ الاحتياطية) يفضل مقفول
            var culture = AppCulture.Build();

            Assert.IsType<System.Globalization.GregorianCalendar>(culture.DateTimeFormat.Calendar);
        }

        [Fact]
        public void MonthNamesStayArabic()
        {
            var culture = AppCulture.Build();

            Assert.Equal("يناير", culture.DateTimeFormat.GetMonthName(1));
        }
    }
}
