namespace WorkforceManager.Core.Enums
{
    /// <summary>حالة دفعة الإنتاج داخل خط المنتج</summary>
    public enum BatchStatus
    {
        /// <summary>لسه في الخط — عدّت مراحل وواقفة عند واحدة، وبتترحّل لليوم اللي بعده</summary>
        Open = 0,

        /// <summary>عدّت آخر مرحلة نشطة في الخط — بقت إنتاج تام</summary>
        Completed = 1,

        /// <summary>اتلغت (هالك/خردة) — بتخرج من الواقف من غير ما تتحسب إنتاج</summary>
        Cancelled = 2
    }
}
