using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// اختبارات القاعدة من طرف لطرف على قاعدة بيانات SQLite حقيقية:
    /// مش بتختبر القرار بس، بتختبر إن اللي المفروض ما يتكتبش فعلاً
    /// ما اتكتبش (ده بيت القصيد كله في R4).
    /// </summary>
    public class ProductionFlowAssignmentTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        // ---------------- أدوات مساعدة ----------------

        private async Task<FlowSaveResultDto> RecordAsync(
            int productId, int stageId, int workerId, int pieces = 10, bool confirmOverride = false)
        {
            return await _db.InScopeAsync<ProductionFlowService, FlowSaveResultDto>(service =>
                service.RecordFlowAsync(
                    productId,
                    TestDatabase.Today,
                    new[]
                    {
                        new FlowRangeDto
                        {
                            FromStageId = stageId, ToStageId = stageId, PieceCount = pieces
                        }
                    },
                    new[] { new FlowShareDto { ProductionStageId = stageId, WorkerId = workerId, PieceCount = pieces } },
                    confirmOverride: confirmOverride));
        }

        /// <summary>تسجيل أولي ناجح: أحمد على "دبلة / تشكيل" — نقطة البداية لأغلب الاختبارات</summary>
        private Task<FlowSaveResultDto> RecordAhmedOnRingShapingAsync() =>
            RecordAsync(TestDatabase.ProductRingId, TestDatabase.RingStage1Id, TestDatabase.WorkerAhmedId);

        // ---------------- R1: مفيش تكليف سابق → بيتسجل على طول ----------------

        [Fact]
        public async Task NoExistingAssignment_SavesWithoutConfirmation()
        {
            var result = await RecordAhmedOnRingShapingAsync();

            Assert.Equal(1, result.RecordsCount);

            var saved = await _db.GetProductionAsync();
            Assert.Single(saved);
            Assert.Equal(TestDatabase.WorkerAhmedId, saved[0].WorkerId);
        }

        // ---------------- R2 + R4: نفس المنتج، مرحلة مختلفة ----------------

        [Fact]
        public async Task SameProductDifferentStage_WithoutConfirm_RequestsConfirmationAndPersistsNothing()
        {
            await RecordAhmedOnRingShapingAsync();

            var ex = await Assert.ThrowsAsync<AssignmentConfirmationRequiredException>(() =>
                RecordAsync(TestDatabase.ProductRingId, TestDatabase.RingStage2Id, TestDatabase.WorkerAhmedId));

            var conflict = Assert.Single(ex.Conflicts);
            Assert.Equal("تشكيل", conflict.Existing.StageName);
            Assert.Equal("تلميع", conflict.Attempted.StageName);

            // R4: الإلغاء ما غيّرش حاجة — السجل الأصلي بس هو اللي موجود
            var saved = await _db.GetProductionAsync();
            Assert.Single(saved);
            Assert.Equal(TestDatabase.RingStage1Id, saved[0].ProductionStageId);
        }

        [Fact]
        public async Task SameProductDifferentStage_WithConfirm_Persists()
        {
            await RecordAhmedOnRingShapingAsync();

            await RecordAsync(TestDatabase.ProductRingId, TestDatabase.RingStage2Id,
                TestDatabase.WorkerAhmedId, confirmOverride: true);

            // R3: العامل بقى عنده تكليفين — والقديم ما اتشالش
            var saved = await _db.GetProductionAsync();
            Assert.Equal(2, saved.Count);
            Assert.Contains(saved, r => r.ProductionStageId == TestDatabase.RingStage1Id);
            Assert.Contains(saved, r => r.ProductionStageId == TestDatabase.RingStage2Id);
        }

        // ---------------- R2 + R4: منتج مختلف ----------------

        [Fact]
        public async Task DifferentProduct_WithoutConfirm_RequestsConfirmationAndPersistsNothing()
        {
            await RecordAhmedOnRingShapingAsync();

            var ex = await Assert.ThrowsAsync<AssignmentConfirmationRequiredException>(() =>
                RecordAsync(TestDatabase.ProductChainId, TestDatabase.ChainStage1Id, TestDatabase.WorkerAhmedId));

            var conflict = Assert.Single(ex.Conflicts);
            Assert.Equal("دبلة", conflict.Existing.ProductName);
            Assert.Equal("سلسلة", conflict.Attempted.ProductName);

            var saved = await _db.GetProductionAsync();
            Assert.Single(saved);
        }

        [Fact]
        public async Task DifferentProduct_WithConfirm_Persists()
        {
            await RecordAhmedOnRingShapingAsync();

            await RecordAsync(TestDatabase.ProductChainId, TestDatabase.ChainStage1Id,
                TestDatabase.WorkerAhmedId, confirmOverride: true);

            var saved = await _db.GetProductionAsync();
            Assert.Equal(2, saved.Count);
        }

        // ---------------- R5: تكرار حرفي ----------------

        [Fact]
        public async Task ExactDuplicate_IsBlockedAsAlreadyAssigned_NotAnOverride()
        {
            await RecordAhmedOnRingShapingAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RecordAsync(TestDatabase.ProductRingId, TestDatabase.RingStage1Id, TestDatabase.WorkerAhmedId));

            // مش استثناء التأكيد — ده رفض نهائي برسالة مختلفة
            Assert.IsNotType<AssignmentConfirmationRequiredException>(ex);
            Assert.Contains("مسجلة بالفعل", ex.Message);

            var saved = await _db.GetProductionAsync();
            Assert.Single(saved);
        }

        [Fact]
        public async Task ExactDuplicate_StaysBlockedEvenWithConfirmOverride()
        {
            await RecordAhmedOnRingShapingAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RecordAsync(TestDatabase.ProductRingId, TestDatabase.RingStage1Id,
                    TestDatabase.WorkerAhmedId, confirmOverride: true));

            Assert.IsNotType<AssignmentConfirmationRequiredException>(ex);

            var saved = await _db.GetProductionAsync();
            Assert.Single(saved); // ولا سجل زيادة اتكتب
        }

        // ---------------- التخطي بيسجل سجل واحد بالظبط ----------------

        [Fact]
        public async Task ConfirmedOverride_InsertsExactlyOneRecord_NoDoubleInsert()
        {
            await RecordAhmedOnRingShapingAsync();
            var beforeCount = (await _db.GetProductionAsync()).Count;

            await RecordAsync(TestDatabase.ProductChainId, TestDatabase.ChainStage1Id,
                TestDatabase.WorkerAhmedId, confirmOverride: true);

            var afterCount = (await _db.GetProductionAsync()).Count;

            // المحاولة الفاشلة قبلها + التأكيد بعدها = سجل واحد جديد بس
            Assert.Equal(beforeCount + 1, afterCount);
        }

        [Fact]
        public async Task FailedAttemptThenConfirm_LeavesOnlyOneNewRecord()
        {
            await RecordAhmedOnRingShapingAsync();

            // المسار الحقيقي بالظبط: محاولة → رفض → تأكيد → حفظ
            await Assert.ThrowsAsync<AssignmentConfirmationRequiredException>(() =>
                RecordAsync(TestDatabase.ProductChainId, TestDatabase.ChainStage1Id, TestDatabase.WorkerAhmedId));

            await RecordAsync(TestDatabase.ProductChainId, TestDatabase.ChainStage1Id,
                TestDatabase.WorkerAhmedId, confirmOverride: true);

            var saved = await _db.GetProductionAsync();
            Assert.Equal(2, saved.Count);
            Assert.Single(saved, r => r.ProductionStageId == TestDatabase.ChainStage1Id);
        }

        // ---------------- التخطي بيخص الطلب بتاعه بس ----------------

        [Fact]
        public async Task ConfirmOverride_DoesNotLeakIntoTheNextRequest()
        {
            await RecordAhmedOnRingShapingAsync();

            // طلب اتأكد عليه
            await RecordAsync(TestDatabase.ProductChainId, TestDatabase.ChainStage1Id,
                TestDatabase.WorkerAhmedId, confirmOverride: true);

            // طلب تالت من غير تأكيد → لازم يقف تاني (الموافقة ما اتخزنتش في أي حتة)
            await Assert.ThrowsAsync<AssignmentConfirmationRequiredException>(() =>
                RecordAsync(TestDatabase.ProductRingId, TestDatabase.RingStage2Id, TestDatabase.WorkerAhmedId));

            var saved = await _db.GetProductionAsync();
            Assert.Equal(2, saved.Count);
        }

        // ---------------- عدم كسر السلوك القائم ----------------

        [Fact]
        public async Task OtherWorkersAreUnaffectedByAnotherWorkersAssignment()
        {
            await RecordAhmedOnRingShapingAsync();

            // سعيد مالوش أي تكليف — لازم يعدّي عادي من غير أي تأكيد
            await RecordAsync(TestDatabase.ProductChainId, TestDatabase.ChainStage1Id, TestDatabase.WorkerSaidId);

            var saved = await _db.GetProductionAsync();
            Assert.Equal(2, saved.Count);
        }

        [Fact]
        public async Task SuccessfulSave_StillMarksAttendanceAutomatically()
        {
            // السلوك القديم لازم يفضل زي ما هو بعد ما الحفظ بقى جوه معاملة
            var result = await RecordAhmedOnRingShapingAsync();

            Assert.Equal(1, result.AttendanceMarkedCount);
            Assert.Equal(1, result.StagesCovered);
            Assert.Single(result.WorkerTotals);
            Assert.Equal("أحمد", result.WorkerTotals[0].WorkerName);
        }

        [Fact]
        public async Task RejectedSave_DoesNotMarkAttendance()
        {
            // العامل الوحيد في الرحلة المرفوضة هو أحمد وهو أصلاً له حضور،
            // فبنستخدم سعيد عشان نتأكد إن الحضور نفسه اترجع مع التراجع
            await RecordAhmedOnRingShapingAsync();

            await Assert.ThrowsAsync<AssignmentConfirmationRequiredException>(() =>
                RecordAsync(TestDatabase.ProductChainId, TestDatabase.ChainStage1Id, TestDatabase.WorkerAhmedId));

            using var scope = _db.CreateScope();
            var db = _db.GetService<Data.AppDbContext>(scope);
            // أحمد بس (من الحفظة الناجحة) — الرحلة المرفوضة ما سابتش أي أثر
            Assert.Single(db.Attendances);
        }

        // ---------------- الحالة الواقعية: عامل واحد على كل مراحل المنتج ----------------

        /// <summary>
        /// نفس اللي المستخدم بيعمله على الشاشة: نطاق واحد من أول مرحلة
        /// لآخر مرحلة، ونفس العامل متحطّ على كل المراحل — الحفظ لازم
        /// يقف ويسأل، مش يعدّي ويسجل اليوميات على طول.
        /// </summary>
        private Task<FlowSaveResultDto> RecordAhmedOnEveryRingStageAsync(bool confirmOverride) =>
            _db.InScopeAsync<ProductionFlowService, FlowSaveResultDto>(service =>
                service.RecordFlowAsync(
                    TestDatabase.ProductRingId,
                    TestDatabase.Today,
                    // نطاق واحد بيغطي المرحلتين (زي "من مرحلة 1 إلى مرحلة 11")
                    new[]
                    {
                        new FlowRangeDto
                        {
                            FromStageId = TestDatabase.RingStage1Id,
                            ToStageId = TestDatabase.RingStage2Id,
                            PieceCount = 10
                        }
                    },
                    // نفس العامل على كل مرحلة
                    new[]
                    {
                        new FlowShareDto
                        {
                            ProductionStageId = TestDatabase.RingStage1Id,
                            WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 10
                        },
                        new FlowShareDto
                        {
                            ProductionStageId = TestDatabase.RingStage2Id,
                            WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 10
                        }
                    },
                    confirmOverride: confirmOverride));

        [Fact]
        public async Task OneWorkerOnEveryStageOfOneProduct_InASingleFlow_RequiresConfirmation()
        {
            // مفيش أي حاجة محفوظة قبل كده — التعارض جوه نفس الرحلة نفسها
            var ex = await Assert.ThrowsAsync<AssignmentConfirmationRequiredException>(
                () => RecordAhmedOnEveryRingStageAsync(confirmOverride: false));

            var conflict = Assert.Single(ex.Conflicts);
            Assert.Equal("تشكيل", conflict.Existing.StageName);
            Assert.Equal("تلميع", conflict.Attempted.StageName);

            // والأهم: ولا يومية اتسجلت
            Assert.Empty(await _db.GetProductionAsync());
        }

        [Fact]
        public async Task OneWorkerOnEveryStageOfOneProduct_AfterConfirm_SavesAllStages()
        {
            await RecordAhmedOnEveryRingStageAsync(confirmOverride: true);

            var saved = await _db.GetProductionAsync();
            Assert.Equal(2, saved.Count);
            Assert.All(saved, r => Assert.Equal(TestDatabase.WorkerAhmedId, r.WorkerId));
        }

        // ---------------- التزامن ----------------

        [Fact]
        public async Task ConcurrentAssignmentsForSameWorker_OnlyOneSucceeds()
        {
            // نسختين بيسجلوا نفس العامل على منتجين مختلفين في نفس اللحظة،
            // والاتنين شايفين قاعدة بيانات فاضية. من غير قفل الكتابة الاتنين
            // هيعدّوا التحقق ويكتبوا → العامل يبقى على منتجين من غير ما حد وافق.
            var ring = Task.Run(() => RecordAsync(
                TestDatabase.ProductRingId, TestDatabase.RingStage1Id, TestDatabase.WorkerAhmedId));
            var chain = Task.Run(() => RecordAsync(
                TestDatabase.ProductChainId, TestDatabase.ChainStage1Id, TestDatabase.WorkerAhmedId));

            var outcomes = await Task.WhenAll(Settle(ring), Settle(chain));

            Assert.Equal(1, outcomes.Count(succeeded => succeeded));

            // والأهم: الحالة النهائية سليمة — تكليف واحد بس للعامل
            var saved = await _db.GetProductionAsync();
            var ahmedStages = saved
                .Where(r => r.WorkerId == TestDatabase.WorkerAhmedId)
                .Select(r => r.ProductionStageId)
                .Distinct()
                .ToList();

            Assert.Single(ahmedStages);
        }

        [Fact]
        public async Task ConcurrentIdenticalAssignments_DoNotBothInsert()
        {
            // نفس العامل ونفس المرحلة بالظبط من الاتنين: واحد بس ينجح
            // والتاني يترفض كتكرار (مش كتعارض)
            var first = Task.Run(() => RecordAhmedOnRingShapingAsync());
            var second = Task.Run(() => RecordAhmedOnRingShapingAsync());

            var outcomes = await Task.WhenAll(Settle(first), Settle(second));

            Assert.Equal(1, outcomes.Count(succeeded => succeeded));

            var saved = await _db.GetProductionAsync();
            Assert.Single(saved);
        }

        /// <summary>بيحوّل نتيجة المهمة لـ "نجحت؟" من غير ما الاستثناء يوقّع الاختبار</summary>
        private static async Task<bool> Settle(Task task)
        {
            try
            {
                await task;
                return true;
            }
            catch (InvalidOperationException)
            {
                // تعارض أو تكرار — الاتنين رفض متوقع
                return false;
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                // القفل اتاخد من التاني — رفض متوقع كمان، المهم ما يكتبش
                return false;
            }
        }
    }
}
