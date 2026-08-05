using System.Windows;
using System.Windows.Controls;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// صورة العامل — العنصر الوحيد اللي بيرسم العامل في التطبيق كله:
    /// قائمة العمال، كارت أحسن عامل، قايمة المؤهلين للمرحلة.
    ///
    /// القاعدة: صورة لو موجودة، وإلا الحروف الأولى من اسمه. من غير المكان
    /// الموحّد ده كل شاشة كانت هترسم الأفاتار بطريقتها، وأول ما نغيّر
    /// الشكل نلاقي شاشة فاضلة بالشكل القديم.
    /// </summary>
    public partial class WorkerAvatar : UserControl
    {
        /// <summary>نسبة حجم الخط لقُطر الدايرة — بتخلي الحروف متناسبة عند أي حجم</summary>
        private const double FontScale = 0.36;

        public WorkerAvatar()
        {
            InitializeComponent();
            ApplySize();
        }

        // ------- صورة العامل المخزّنة -------

        /// <summary>
        /// النوع <c>object</c> مش <c>byte[]</c> عن قصد: XAML بيرفض
        /// الخصائص اللي نوعها مصفوفة جوه أي DataTemplate
        /// ("Tags of type 'PropertyArrayStart' are not supported in
        /// template sections")، والأفاتار ده أصلاً بيتحط جوه قوالب
        /// القوايم. فبناخده object وبنحوّله هنا.
        /// </summary>
        public static readonly DependencyProperty PhotoDataProperty =
            DependencyProperty.Register(nameof(PhotoData), typeof(object), typeof(WorkerAvatar),
                new PropertyMetadata(null, OnPhotoChanged));

        public object? PhotoData
        {
            get => GetValue(PhotoDataProperty);
            set => SetValue(PhotoDataProperty, value);
        }

        // ------- الحروف الأولى (بتبان لما مفيش صورة) -------

        public static readonly DependencyProperty InitialsProperty =
            DependencyProperty.Register(nameof(Initials), typeof(string), typeof(WorkerAvatar),
                new PropertyMetadata(string.Empty, OnInitialsChanged));

        public string Initials
        {
            get => (string)GetValue(InitialsProperty);
            set => SetValue(InitialsProperty, value);
        }

        // ------- قُطر الدايرة -------

        public static readonly DependencyProperty DiameterProperty =
            DependencyProperty.Register(nameof(Diameter), typeof(double), typeof(WorkerAvatar),
                new PropertyMetadata(44d, OnDiameterChanged));

        public double Diameter
        {
            get => (double)GetValue(DiameterProperty);
            set => SetValue(DiameterProperty, value);
        }

        private static void OnPhotoChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
            ((WorkerAvatar)d).ApplyPhoto();

        private static void OnInitialsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
            ((WorkerAvatar)d).InitialsText.Text = e.NewValue as string ?? string.Empty;

        private static void OnDiameterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
            ((WorkerAvatar)d).ApplySize();

        private void ApplySize()
        {
            Width = Height = Diameter;
            InitialsText.FontSize = Diameter * FontScale;
        }

        /// <summary>
        /// بيبدّل بين الصورة والحروف. بيانات صورة تالفة بترجّع null من
        /// الهيلبر، والعنصر ساعتها بيرجع للحروف بدل ما يفضل فاضي.
        /// </summary>
        private void ApplyPhoto()
        {
            var source = StoredImageHelper.ToImageSource(PhotoData as byte[]);

            PhotoBrush.ImageSource = source;
            PhotoCircle.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
