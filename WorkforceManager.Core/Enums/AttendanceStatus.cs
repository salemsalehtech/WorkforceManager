namespace WorkforceManager.Core.Enums
{
    /// <summary>حالة حضور العامل في يوم معين</summary>
    public enum AttendanceStatus
    {
        /// <summary>حاضر</summary>
        Present = 1,

        /// <summary>غائب بإذن (إجازة معتمدة، ظرف طارئ متفق عليه، ...إلخ)</summary>
        AbsentWithPermission = 2,

        /// <summary>غائب بدون إذن</summary>
        AbsentWithoutPermission = 3
    }
}
