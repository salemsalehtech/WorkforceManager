using System.Reflection;

namespace WorkforceManager.UI
{
    /// <summary>
    /// رقم إصدار البرنامج الحقيقي — مصدره الوحيد &lt;Version&gt; في
    /// Directory.Build.props، منقول هنا من SettingsViewModel.AppVersionText
    /// عشان "تعلم مميزات التحديث" (Tour/LearnFeaturesContent.cs) يحتاج نفس
    /// الرقم بالظبط من غير ما يكرر نفس منطق القراءة من الأسمبلي في مكانين.
    /// </summary>
    public static class AppVersion
    {
        public static string Current
        {
            get
            {
                var assembly = Assembly.GetEntryAssembly();

                var informational = assembly
                    ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;

                // أدوات البناء بتلزق hash بتاع الكوميت بعد علامة + —
                // مالوش أي معنى للمستخدم
                var plus = informational?.IndexOf('+') ?? -1;
                if (plus > 0) informational = informational![..plus];

                return informational ?? assembly?.GetName().Version?.ToString(3) ?? "؟";
            }
        }
    }
}
