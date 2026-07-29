using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// اختبارات القاعدة نفسها من غير قاعدة بيانات — دي الدالة الخالصة
    /// اللي كل مسارات الكتابة والواجهة بتنادي عليها، فلو اتكسرت هنا
    /// يبقى كل حتة في البرنامج اتكسرت.
    /// </summary>
    public class WorkerAssignmentGuardTests
    {
        private const int Ahmed = 1;
        private const int Said = 2;

        private const int RingProduct = 10;
        private const int ChainProduct = 20;

        private const int RingShaping = 100;
        private const int RingPolishing = 101;
        private const int ChainWelding = 200;

        private static WorkerAssignmentDto Assignment(
            int workerId, int productId, int stageId, string workerName = "أحمد") =>
            new()
            {
                WorkerId = workerId,
                WorkerName = workerName,
                ProductId = productId,
                ProductName = productId == RingProduct ? "دبلة" : "سلسلة",
                ProductionStageId = stageId,
                StageName = stageId switch
                {
                    RingShaping => "تشكيل",
                    RingPolishing => "تلميع",
                    _ => "لحام"
                }
            };

        // ---------------- R1: مفيش تكليف تاني → يعدّي على طول ----------------

        [Fact]
        public void Evaluate_NoExistingAssignment_IsClear()
        {
            var result = WorkerAssignmentGuard.Evaluate(
                existing: Array.Empty<WorkerAssignmentDto>(),
                requested: new[] { Assignment(Ahmed, RingProduct, RingShaping) });

            Assert.True(result.IsClear);
            Assert.False(result.RequiresConfirmation);
            Assert.False(result.HasDuplicates);
        }

        [Fact]
        public void Evaluate_DifferentWorkerOnSameStage_IsClear()
        {
            // عامل تاني على نفس المرحلة مالوش أي علاقة بالقاعدة —
            // المرحلة ممكن يشتغل عليها أكتر من عامل عادي
            var result = WorkerAssignmentGuard.Evaluate(
                existing: new[] { Assignment(Ahmed, RingProduct, RingShaping) },
                requested: new[] { Assignment(Said, RingProduct, RingShaping, "سعيد") });

            Assert.True(result.IsClear);
        }

        // ---------------- R2: نفس المنتج/مرحلة مختلفة → تأكيد ----------------

        [Fact]
        public void Evaluate_SameProductDifferentStage_RequiresConfirmation()
        {
            var result = WorkerAssignmentGuard.Evaluate(
                existing: new[] { Assignment(Ahmed, RingProduct, RingShaping) },
                requested: new[] { Assignment(Ahmed, RingProduct, RingPolishing) });

            Assert.True(result.RequiresConfirmation);
            Assert.False(result.HasDuplicates);

            var conflict = Assert.Single(result.Conflicts);
            Assert.Equal(RingShaping, conflict.Existing.ProductionStageId);
            Assert.Equal(RingPolishing, conflict.Attempted.ProductionStageId);
        }

        // ---------------- R2: منتج مختلف → تأكيد ----------------

        [Fact]
        public void Evaluate_DifferentProduct_RequiresConfirmation()
        {
            var result = WorkerAssignmentGuard.Evaluate(
                existing: new[] { Assignment(Ahmed, RingProduct, RingShaping) },
                requested: new[] { Assignment(Ahmed, ChainProduct, ChainWelding) });

            Assert.True(result.RequiresConfirmation);

            var conflict = Assert.Single(result.Conflicts);
            Assert.Equal(RingProduct, conflict.Existing.ProductId);
            Assert.Equal(ChainProduct, conflict.Attempted.ProductId);
        }

        [Fact]
        public void ConfirmationQuestion_NamesBothAssignmentsExplicitly()
        {
            var result = WorkerAssignmentGuard.Evaluate(
                existing: new[] { Assignment(Ahmed, RingProduct, RingShaping) },
                requested: new[] { Assignment(Ahmed, ChainProduct, ChainWelding) });

            var question = Assert.Single(result.Conflicts).ConfirmationQuestion;

            // الرسالة لازم تسمّي العامل والتكليف القديم والجديد بالاسم
            Assert.Contains("أحمد", question);
            Assert.Contains("دبلة – تشكيل", question);
            Assert.Contains("سلسلة – لحام", question);
        }

        // ---------------- R5: تكرار حرفي → ممنوع، مش تأكيد ----------------

        [Fact]
        public void Evaluate_ExactDuplicate_IsDuplicateNotConflict()
        {
            var result = WorkerAssignmentGuard.Evaluate(
                existing: new[] { Assignment(Ahmed, RingProduct, RingShaping) },
                requested: new[] { Assignment(Ahmed, RingProduct, RingShaping) });

            Assert.True(result.HasDuplicates);
            Assert.False(result.RequiresConfirmation); // مش حالة تخطي خالص
            Assert.Single(result.Duplicates);
        }

        [Fact]
        public void EnsureAllowed_ExactDuplicate_BlockedEvenWithConfirmOverride()
        {
            var result = WorkerAssignmentGuard.Evaluate(
                existing: new[] { Assignment(Ahmed, RingProduct, RingShaping) },
                requested: new[] { Assignment(Ahmed, RingProduct, RingShaping) });

            // التأكيد بيتخطى التعارض بس — التكرار الحرفي ممنوع مهما حصل
            var ex = Assert.Throws<InvalidOperationException>(
                () => WorkerAssignmentGuard.EnsureAllowed(result, confirmOverride: true));

            Assert.IsNotType<AssignmentConfirmationRequiredException>(ex);
            Assert.Contains("مسجلة بالفعل", ex.Message);
        }

        // ---------------- EnsureAllowed: قرار "نكمّل ولا نقف" ----------------

        [Fact]
        public void EnsureAllowed_ConflictWithoutOverride_ThrowsConfirmationRequired()
        {
            var result = WorkerAssignmentGuard.Evaluate(
                existing: new[] { Assignment(Ahmed, RingProduct, RingShaping) },
                requested: new[] { Assignment(Ahmed, ChainProduct, ChainWelding) });

            var ex = Assert.Throws<AssignmentConfirmationRequiredException>(
                () => WorkerAssignmentGuard.EnsureAllowed(result, confirmOverride: false));

            Assert.Single(ex.Conflicts);
        }

        [Fact]
        public void EnsureAllowed_ConflictWithOverride_DoesNotThrow()
        {
            var result = WorkerAssignmentGuard.Evaluate(
                existing: new[] { Assignment(Ahmed, RingProduct, RingShaping) },
                requested: new[] { Assignment(Ahmed, ChainProduct, ChainWelding) });

            WorkerAssignmentGuard.EnsureAllowed(result, confirmOverride: true); // R3
        }

        [Fact]
        public void EnsureAllowed_Clear_DoesNotThrow()
        {
            var result = WorkerAssignmentGuard.Evaluate(
                Array.Empty<WorkerAssignmentDto>(),
                new[] { Assignment(Ahmed, RingProduct, RingShaping) });

            WorkerAssignmentGuard.EnsureAllowed(result, confirmOverride: false); // R1
        }

        // ---------------- التكليفات جوه نفس الطلب بتتحسب على بعضها ----------------

        [Fact]
        public void Evaluate_SameWorkerTwiceWithinOneRequest_SecondIsConflict()
        {
            // نفس العامل على مرحلتين في طلب واحد: التانية بتشوف الأولى
            // كـ"مكلّف بالفعل" — من غير كده كان هيعدّي من غير تحذير
            var result = WorkerAssignmentGuard.Evaluate(
                existing: Array.Empty<WorkerAssignmentDto>(),
                requested: new[]
                {
                    Assignment(Ahmed, RingProduct, RingShaping),
                    Assignment(Ahmed, RingProduct, RingPolishing)
                });

            var conflict = Assert.Single(result.Conflicts);
            Assert.Equal(RingShaping, conflict.Existing.ProductionStageId);
            Assert.Equal(RingPolishing, conflict.Attempted.ProductionStageId);
        }

        [Fact]
        public void Evaluate_MultipleWorkersOnDistinctStages_IsClear()
        {
            // الحالة الطبيعية: كل عامل على مرحلته — ميصحش يظهر أي تحذير
            var result = WorkerAssignmentGuard.Evaluate(
                existing: Array.Empty<WorkerAssignmentDto>(),
                requested: new[]
                {
                    Assignment(Ahmed, RingProduct, RingShaping),
                    Assignment(Said, RingProduct, RingPolishing, "سعيد")
                });

            Assert.True(result.IsClear);
        }
    }
}
