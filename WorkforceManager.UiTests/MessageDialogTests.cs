using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using MaterialDesignThemes.Wpf;
using WorkforceManager.UI.Views;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// نافذة الرسائل اللي بقت بديل MessageBox.Show في البرنامج كله.
    ///
    /// اللي بيتختبر هنا مش الشكل — الشكل بيتشاف بالعين. اللي بيتختبر هو
    /// اللي **بيغلط في صمت**: اسم أيقونة أو اسم فرشاة غلط بيطلع أول ما
    /// الرسالة تظهر عند المستخدم (يعني في أسوأ لحظة — وقت الخطأ نفسه)،
    /// وأي زرار بيتربط بنتيجة غلط بيخلي "لأ" تتنفّذ كأنها "أيوه".
    /// </summary>
    [Collection("WPF")]
    public class MessageDialogTests
    {
        private static readonly MessageKind[] AllKinds =
            Enum.GetValues<MessageKind>();

        // ======================= ربط النوع بالشكل =======================

        [Fact]
        public void كل_نوع_أيقونته_اسم_حقيقي_في_PackIconKind()
        {
            // الأيقونة بتتحوّل من نص وقت فتح النافذة — الغلطة مبتظهرش
            // في البناء ولا في أي اختبار تاني، بتظهر وقت العرض بس
            foreach (var kind in AllKinds)
            {
                var name = MessageAppearance.Icon(kind);
                Assert.True(Enum.TryParse<PackIconKind>(name, out _),
                    $"أيقونة {kind} اسمها '{name}' مش موجود في PackIconKind");
            }
        }

        [Fact]
        public void كل_نوع_فرشاته_موجودة_في_الثيمين()
        {
            // مفتاح فرشاة غلط مبيرميش — بيسيب الهيدر شفاف، فالنافذة
            // بتطلع من غير لون ومحدش بياخد باله غير المستخدم
            var missing = WpfThread.Run(() =>
            {
                return AllKinds
                    .SelectMany(k => new[] { MessageAppearance.HeaderBrush(k), MessageAppearance.HeaderInk(k) })
                    .Where(key => Application.Current.TryFindResource(key) is null)
                    .ToList();
            });

            Assert.Empty(missing);
        }

        [Fact]
        public void التحذير_وحده_زراره_أحمر()
        {
            // التحذير = فعل مش بيرجع. لو زراره بقى بلون الهوية زي السؤال
            // العادي، بيبقى مفيش أي فرق بصري بين "تحب تكمّل؟" و"تحب تمسح؟"
            Assert.Equal("DangerButton", MessageAppearance.ConfirmStyle(MessageKind.Warning));

            foreach (var kind in AllKinds.Where(k => k != MessageKind.Warning))
                Assert.Equal("PrimaryButton", MessageAppearance.ConfirmStyle(kind));
        }

        // ======================= الأزرار والنتيجة =======================

        [Fact]
        public void زرار_التأكيد_بيرجّع_موافقة_وزرار_الإلغاء_لأ()
        {
            // بنعرض النافذة فعلاً وندوس الزرار: DialogResult مينفعش
            // يتحط غير على نافذة اتعرضت، والمسار ده هو نفسه اللي
            // Notify.Ask بترجّع نتيجته للشاشة
            var (confirmed, cancelled) = WpfThread.Run(() =>
            {
                return (ShowAndClick("YesButton"), ShowAndClick("NoButton"));
            });

            Assert.True(confirmed);

            // "لأ" IsCancel، فـ WPF بيقفل بـ DialogResult = false لوحده
            Assert.False(cancelled);
        }

        /// <summary>
        /// بيعرض النافذة، يدوس الزرار أول ما تبان، ويرجّع النتيجة.
        ///
        /// الضغط بالـ AutomationPeer مش بـ RaiseEvent: الـ RaiseEvent
        /// بترمي الحدث بس، بينما IsCancel متعامل جوه Button.OnClick
        /// نفسها — يعني زرار "لأ" كان هيتقفل من غير ما يحط DialogResult.
        /// دي ضغطة حقيقية زي بتاعة المستخدم.
        /// </summary>
        private static bool ShowAndClick(string buttonName)
        {
            var dialog = Build(MessageKind.Question, twoButtons: true, defaultIsNo: false);

            dialog.Loaded += (_, _) =>
            {
                var peer = new ButtonAutomationPeer(Button(dialog, buttonName));
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            };

            return dialog.ShowDialog() == true;
        }

        [Fact]
        public void السؤال_الخطر_افتراضيه_لأ_والعادي_افتراضيه_أيوه()
        {
            // الزرار الافتراضي هو اللي Enter بتدوسه. ضغطة Enter بالغلط
            // مالهاش حق تمسح شغل — دي القاعدة اللي Notify.AskDangerous
            // قايمة عليها من الأصل، ولازم تفضل صح بعد تغيير النافذة
            var (dangerous, normal) = WpfThread.Run(() =>
            {
                var d = Build(MessageKind.Warning, twoButtons: true, defaultIsNo: true);
                var n = Build(MessageKind.Question, twoButtons: true, defaultIsNo: false);

                return ((Button(d, "NoButton").IsDefault, Button(d, "YesButton").IsDefault),
                        (Button(n, "NoButton").IsDefault, Button(n, "YesButton").IsDefault));
            });

            Assert.Equal((true, false), dangerous);
            Assert.Equal((false, true), normal);
        }

        [Fact]
        public void رسالة_الخبر_زرار_واحد_بس()
        {
            // الخبر والخطأ مفيهمش قرار — "لأ" جنب "تمام" بتسأل المستخدم
            // سؤال مالوش إجابة
            var (noVisibility, yesText) = WpfThread.Run(() =>
            {
                var dialog = Build(MessageKind.Error, twoButtons: false, defaultIsNo: false);
                return (Button(dialog, "NoButton").Visibility,
                        Button(dialog, "YesButton").Content as string);
            });

            Assert.Equal(Visibility.Collapsed, noVisibility);
            Assert.Equal("تمام", yesText);
        }

        [Fact]
        public void نص_الأزرار_عربي_في_كل_الأنواع()
        {
            // "Yes"/"No" الإنجليزي جوه برنامج عربي بالكامل كان جزء أصيل
            // من الباج — مش بس شكل النافذة
            var texts = WpfThread.Run(() =>
            {
                return AllKinds
                    .Select(k => Build(k, twoButtons: true, defaultIsNo: false))
                    .SelectMany(d => new[]
                    {
                        Button(d, "YesButton").Content as string,
                        Button(d, "NoButton").Content as string
                    })
                    .ToList();
            });

            Assert.All(texts, t => Assert.False(string.IsNullOrWhiteSpace(t)));
            Assert.All(texts, t => Assert.DoesNotContain(t!, new[] { "Yes", "No", "OK", "Cancel" }));
        }

        // ======================= أدوات =======================

        /// <summary>
        /// الـ constructor خاص عن قصد (النداء بيبقى من Ask/Show بس)،
        /// فالاختبار بينادي عليه بالـ Reflection — نفس أسلوب XamlLoadTests
        /// بدل ما نفتح الـ API عشان اختبار.
        /// </summary>
        private static Window Build(MessageKind kind, bool twoButtons, bool defaultIsNo)
        {
            var ctor = typeof(MessageDialog).GetConstructors(
                BindingFlags.Instance | BindingFlags.NonPublic).Single();

            return (Window)ctor.Invoke(new object[] { "رسالة الاختبار", "عنوان", kind, twoButtons, defaultIsNo });
        }

        private static Button Button(Window dialog, string name) =>
            (Button)dialog.FindName(name)!;

    }

    /// <summary>
    /// الاختبارات اللي بتعمل Application لازم متتشغّلش مع بعض — WPF
    /// بيسمح بواحدة بس في العملية كلها.
    /// </summary>
    [CollectionDefinition("WPF")]
    public class WpfCollection { }
}
