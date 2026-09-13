using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Helpers;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>صف حساب إداري (مدير/رئيس قسم) في شاشتهم المنفصلة تمامًا عن العمال</summary>
    public class DepartmentAccountRow
    {
        public int WorkerId { get; init; }
        public string FullName { get; init; } = "";
        public string? PhoneNumber { get; init; }
        public HourlyRole Role { get; init; }
        public string RoleName => Role.ToArabicName();
        public decimal DailyWageEgp { get; init; }
        public bool IsActive { get; init; }
        public byte[]? PhotoData { get; init; }
        public string? Username { get; init; }

        public string Initials => NameInitials.From(FullName);

        /// <summary>مدير القسم مالوش سعر يومية خالص — رئيس القسم زي أي عامل بالساعة</summary>
        public bool HasWage => Role != HourlyRole.DepartmentManager;

        public string WageText => DailyWageEgp > 0 ? $"{DailyWageEgp:N0} ج / يومية" : "سعر اليومية غير محدد";

        public bool HasPhone => !string.IsNullOrWhiteSpace(PhoneNumber);

        public string ToggleTooltip => IsActive ? "إيقاف" : "إعادة تفعيل";

        /// <summary>
        /// بادج توثيق مميّز لكل دور — مدير القسم دهبي مصمت، رئيس القسم
        /// فضّي مفرّغ، عشان الفرق يبان بنظرة من غير ما تقرا النص.
        /// </summary>
        public string BadgeIcon => Role == HourlyRole.DepartmentManager ? "CheckDecagram" : "CheckDecagramOutline";

        public string BadgeColorKey => Role == HourlyRole.DepartmentManager ? "GoldBrush" : "TextMutedBrush";

        public string BadgeTooltip => Role == HourlyRole.DepartmentManager
            ? "حساب موثّق — مدير قسم"
            : "حساب موثّق — رئيس قسم";
    }
}
