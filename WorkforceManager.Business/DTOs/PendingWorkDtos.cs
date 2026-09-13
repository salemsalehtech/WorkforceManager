namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// مرحلة قدامها شغل واقف — قطع خلصت المرحلة اللي قبلها ولسه معدّتش
    /// المرحلة دي.
    /// </summary>
    public class PendingStageDto
    {
        public int StageId { get; init; }
        public string StageName { get; init; } = string.Empty;

        /// <summary>موقع المرحلة في الخط (1 = أول مرحلة)</summary>
        public int Position { get; init; }

        /// <summary>اسم المرحلة اللي قبلها — بيظهر في الرسالة</summary>
        public string PreviousStageName { get; init; } = string.Empty;

        /// <summary>القطع الواقفة قدام المرحلة دي</summary>
        public int PendingPieces { get; init; }

        /// <summary>
        /// المرحلة دي اتسجل عليها **أكتر** من اللي قبلها — مستحيل فيزيائيًا،
        /// القطعة لازم تعدّي المرحلة اللي قبلها الأول. غلط إدخال.
        /// </summary>
        public bool IsDataError { get; init; }

        /// <summary>بكام قطعة زيادة (لما يكون فيه غلط إدخال)</summary>
        public int ExcessPieces { get; init; }

        /// <summary>وصف المشكلة للمستخدم — بيسمّي المرحلتين والرقم</summary>
        public string ErrorText => IsDataError
            ? $"مرحلة \"{StageName}\" عليها {ExcessPieces:N0} قطعة أكتر من " +
              $"\"{PreviousStageName}\" اللي قبلها — راجع سجلات المرحلتين"
            : "";

        public string PendingText => $"{PendingPieces:N0} قطعة واقفة قدام \"{StageName}\"";
    }

    /// <summary>حالة الشغل الواقف على منتج واحد</summary>
    public class ProductPendingDto
    {
        public int ProductId { get; init; }
        public string ProductName { get; init; } = string.Empty;

        /// <summary>المراحل اللي قدامها شغل واقف أو فيها غلط إدخال</summary>
        public List<PendingStageDto> Stages { get; init; } = new();

        /// <summary>
        /// إجمالي القطع اللي دخلت الخط ولسه ماخرجتش منه =
        /// اللي خلص أول مرحلة ناقص اللي خلص آخر مرحلة.
        /// </summary>
        public int TotalPending { get; init; }

        /// <summary>آخر مرحلة نشطة في خط المنتج</summary>
        public int LastStageId { get; init; }

        public bool HasPending => Stages.Any(s => !s.IsDataError && s.PendingPieces > 0);

        public bool HasDataError => Stages.Any(s => s.IsDataError);

        /// <summary>المراحل اللي ينفع نكمّل منها (من غير اللي فيها غلط)</summary>
        public IEnumerable<PendingStageDto> Resumable =>
            Stages.Where(s => !s.IsDataError && s.PendingPieces > 0);
    }
}
