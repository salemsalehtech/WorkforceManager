using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Interfaces;

using WorkforceManager.Core.Helpers;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// مين مؤهل للمرحلة دي، مرتبين من الأحسن للأضعف.
    ///
    /// الترتيب بييجي من <see cref="SkillRatingService.GetRankedForStageAsync"/>
    /// — **نفس الدالة** اللي شاشة التسجيل اليومي بتستخدمها لما المستخدم
    /// بيدوّر على عامل يضيفه لمرحلة. من غير كده كان ممكن المدير يشوف
    /// ترتيب هنا وترتيب تاني هناك لنفس المرحلة.
    /// </summary>
    public partial class QualifiedWorkersDialog : Window
    {
        private QualifiedWorkersDialog(
            string productName, string stageName, IReadOnlyList<QualifiedWorkerRow> workers)
        {
            InitializeComponent();

            TitleText.Text = $"مين يعرف \"{stageName}\"؟";
            SubtitleText.Text = workers.Count == 0
                ? $"{productName} — مفيش عمال مؤهلين"
                : $"{productName} — {workers.Count} عامل، مرتبين من الأحسن للأضعف";

            WorkersList.ItemsSource = workers;

            var empty = workers.Count == 0;
            EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            ListScroller.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>يجهّز القايمة ويعرضها</summary>
        public static async Task ShowAsync(
            Window? owner, IServiceScopeFactory scopeFactory,
            int stageId, string productName, string stageName)
        {
            List<QualifiedWorkerRow> rows;

            using (var scope = scopeFactory.CreateScope())
            {
                var ranked = await scope.ServiceProvider.GetRequiredService<SkillRatingService>()
                    .GetRankedForStageAsync(stageId);

                // الصور مش جزء من DTO التقييم، فبنجيبها هنا عشان العامل
                // يتعرض بنفس شكله في كل الشاشات
                var photos = (await scope.ServiceProvider.GetRequiredService<IWorkerRepository>()
                        .GetAllAsync())
                    .ToDictionary(w => w.Id, w => w.PhotoData);

                rows = ranked
                    .Select((worker, index) => new QualifiedWorkerRow(worker, index + 1,
                        photos.GetValueOrDefault(worker.WorkerId)))
                    .ToList();
            }

            var dialog = new QualifiedWorkersDialog(productName, stageName, rows);
            if (owner is not null) dialog.Owner = owner;
            dialog.ShowDialog();
        }

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }

    /// <summary>سطر عامل في القايمة — تغليف عرض حوالين DTO التقييم</summary>
    public class QualifiedWorkerRow
    {
        private readonly RankedWorkerDto _worker;

        public QualifiedWorkerRow(RankedWorkerDto worker, int rank, byte[]? photoData)
        {
            _worker = worker;
            Rank = rank;
            PhotoData = photoData;
        }

        public int Rank { get; }
        public byte[]? PhotoData { get; }

        public string WorkerName => _worker.WorkerName;
        public string StarsText => _worker.StarsText;
        public string StarsLabel => SkillRatingService.StarsLabel(_worker.Stars);
        public string MeasuredText => _worker.MeasuredTooltip;

        public string Initials => NameInitials.From(WorkerName);
    }
}
