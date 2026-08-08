using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;
using WorkforceManager.UI.Views;

namespace WorkforceManager.UI.ViewModels
{
    // صفوف شاشة المنتجات: الفلاتر والترتيب وبطاقة المنتج وصف المرحلة.

    public enum ProductFilter { WorkedThisPeriod, All, Inactive }

    /// <summary>ترتيب المنتجات بحجم إنتاجها في الفترة</summary>
    public enum ProductVolumeSort { None, HighestFirst, LowestFirst }

    // ------- خيارات فلاتر شاشة المنتجات (null = الفلتر مش مفعّل) -------

    public record StageNameFilterOption(string? StageName, string Display);

    public record WorkerNameFilterOption(int? WorkerId, string Display);

    public record VolumeFilterOption(ProductVolumeSort Sort, string Display);

    /// <summary>منتج واحد في قائمة الشاشة، بمراحله المحمّلة معاه</summary>
    public class ProductRow
    {
        public int ProductId { get; init; }
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public bool IsActive { get; init; }
        public List<StageRow> Stages { get; init; } = new();

        // ------- نشاط المنتج في الفترة المعروضة -------

        /// <summary>القطع المسجّلة على كل مراحله في الفترة</summary>
        public int PiecesInPeriod { get; init; }

        /// <summary>عدد أيام الشغل عليه في الفترة</summary>
        public int DaysWorkedInPeriod { get; init; }

        /// <summary>العمال اللي اشتغلوا عليه في الفترة</summary>
        public IReadOnlySet<int> WorkerIds { get; init; } = new HashSet<int>();

        /// <summary>اشتغل عليه فعلًا في الفترة؟ (ده معنى "شغّال")</summary>
        public bool WorkedInPeriod => PiecesInPeriod > 0;

        /// <summary>ملخص النشاط على الكارت</summary>
        public string ActivityText => WorkedInPeriod
            ? $"{PiecesInPeriod:N0} قطعة في {DaysWorkedInPeriod} يوم"
            : "مفيش شغل في الفترة دي";

        /// <summary>صورة المنتج المخزّنة (null = مفيش صورة)</summary>
        public byte[]? ImageData { get; init; }

        /// <summary>
        /// الصورة جاهزة للعرض. بتتبني مرة واحدة مع بناء الصف مش مع كل
        /// رسم للبطاقة — فك تشفير الصورة في كل مرة كان هيتقل القائمة.
        /// </summary>
        public System.Windows.Media.ImageSource? Image => _image ??= StoredImageHelper.ToImageSource(ImageData);
        private System.Windows.Media.ImageSource? _image;

        /// <summary>عنده صورة؟ (لو لأ بتظهر دايرة الحروف الأولى مكانها)</summary>
        public bool HasImage => ImageData is { Length: > 0 };

        public string StatusText => IsActive ? "نشط" : "موقوف";
        public int ActiveStagesCount => Stages.Count(s => s.IsActive);
        public string StagesCountText => $"{ActiveStagesCount} مرحلة";

        /// <summary>أول حرفين من اسم المنتج — للدايرة على البطاقة</summary>
        public string Initials
        {
            get
            {
                var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return "؟";
                return parts.Length == 1
                    ? parts[0][..Math.Min(2, parts[0].Length)]
                    : $"{parts[0][0]}{parts[1][0]}";
            }
        }

        // ------- التنبيهات -------

        /// <summary>منتج من غير أي مرحلة نشطة — مينفعش يتسجل عليه إنتاج خالص</summary>
        public bool HasNoStages => ActiveStagesCount == 0;

        /// <summary>مراحل نشطة مفيش حد مؤهل ليها — الخط بيقف عندها</summary>
        public int UncoveredStagesCount => Stages.Count(s => s.IsActive && s.HasNoQualifiedWorkers);

        public bool NeedsAttention => HasNoStages || UncoveredStagesCount > 0;

        public string AttentionText => HasNoStages
            ? "مفيش مراحل — مينفعش يتسجل عليه إنتاج"
            : UncoveredStagesCount > 0
                ? $"{UncoveredStagesCount} مرحلة مفيش حد مؤهل ليها"
                : "";
    }

    /// <summary>مرحلة واحدة في خط إنتاج المنتج المحدد</summary>
    public class StageRow
    {
        public int StageId { get; init; }
        public string StageName { get; init; } = "";
        public int PiecesPerWorkday { get; init; }
        public int SortOrder { get; init; }
        public bool IsActive { get; init; }

        /// <summary>ترتيبها المعروض في الخط (1، 2، 3...) — محسوب من موقعها مش من SortOrder</summary>
        public int DisplayOrder { get; init; }

        /// <summary>عدد العمال المربوط لهم المهارة دي</summary>
        public int QualifiedWorkersCount { get; init; }

        public string StatusText => IsActive ? "نشطة" : "موقوفة";
        public string QuotaText => $"{PiecesPerWorkday} قطعة / يومية";

        /// <summary>
        /// مرحلة نشطة مفيش ولا عامل مؤهل ليها. دي مشكلة حقيقية بتوقف
        /// الخط: شاشة رحلة الإنتاج بتعرض المؤهلين بس، فالمرحلة دي مش
        /// هيتوزع عليها حد ومش هينفع تتسجل.
        /// </summary>
        public bool HasNoQualifiedWorkers => IsActive && QualifiedWorkersCount == 0;

        public string WorkersText => QualifiedWorkersCount == 0
            ? "مفيش عمال مؤهلين"
            : $"{QualifiedWorkersCount} عامل مؤهل";
    }
}
