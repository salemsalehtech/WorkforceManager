using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// مرحلة واحدة في ترتيب <see cref="ProductionMemory"/>، بموقعها.
    ///
    /// **الترتيب ممكن يشيل مراحل** (قرار مؤكد مع المستخدم): الخطة ممكن
    /// تتخطى مراحل عن قصد، مش لازم تغطي كل مراحل المنتج النشطة. النتيجة
    /// إن المرحلة المتخطّاة بتفضل بصفر إنتاج، وحساب فجوات الخط بيولّد
    /// عندها رصيد أولي تلقائي — وده الصح: القطع فعلاً عدّت من غيرها.
    ///
    /// الموقع صريح ومخزّن مش مستنتج من الـ Id: الـ Id ترتيب إدخال،
    /// والمستخدم بيعيد الترتيب بعد الإدخال.
    /// </summary>
    // موقع واحد لكل خطة — بيمنع ترتيب متكرر أو ناقص على مستوى القاعدة
    [Index(nameof(ProductionMemoryId), nameof(Position), IsUnique = true)]
    public class ProductionMemoryStage
    {
        [Key]
        public int Id { get; set; }

        public int ProductionMemoryId { get; set; }
        public ProductionMemory ProductionMemory { get; set; } = null!;

        public int ProductionStageId { get; set; }
        public ProductionStage ProductionStage { get; set; } = null!;

        /// <summary>موقع المرحلة في الخطة، من صفر</summary>
        public int Position { get; set; }
    }
}
