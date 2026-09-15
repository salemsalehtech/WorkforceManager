using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// تحليل النية بره أي قاعدة بيانات — <see cref="SearchIntentService.ParseIntent"/>
    /// منطق نقي (نفس مبدأ <see cref="SearchMatcher"/>)، فمفيش داعي TestDatabase هنا خالص.
    /// </summary>
    public class ParseIntentTests
    {
        [Theory]
        // القيمة المتوقعة الثالثة بعد التطبيع (أ→ا) — نفس تطبيع ArabicSearch
        // اللي كل مطابقة تانية في البرنامج قايمة عليه، شوف ParseIntent
        [InlineData("غياب أحمد", SearchIntentKind.Absence, "احمد")]
        [InlineData("أحمد غياب", SearchIntentKind.Absence, "احمد")] // ترتيب حر: الاسم قبل كلمة النية
        [InlineData("الغياب أحمد", SearchIntentKind.Absence, "احمد")]
        [InlineData("جزاء أحمد", SearchIntentKind.Penalties, "احمد")]
        [InlineData("جزاءات أحمد", SearchIntentKind.Penalties, "احمد")]
        [InlineData("حوافز أحمد", SearchIntentKind.Adjustments, "احمد")]
        [InlineData("سلف أحمد", SearchIntentKind.Adjustments, "احمد")]
        [InlineData("راتب أحمد", SearchIntentKind.Wage, "احمد")]
        [InlineData("أجر أحمد", SearchIntentKind.Wage, "احمد")]
        [InlineData("مهارات أحمد", SearchIntentKind.Skills, "احمد")]
        [InlineData("تقييم أحمد", SearchIntentKind.Standing, "احمد")]
        [InlineData("انتاج أحمد", SearchIntentKind.Production, "احمد")]
        [InlineData("إنتاج أحمد", SearchIntentKind.Production, "احمد")] // همزة — لازم تتوحّد زي أي مطابقة تانية
        public void Recognizes_each_intent_keyword_with_name_in_either_order(
            string query, SearchIntentKind expectedKind, string expectedName)
        {
            var parsed = SearchIntentService.ParseIntent(query);

            Assert.NotNull(parsed);
            Assert.Equal(expectedKind, parsed!.Kind);
            Assert.Equal(expectedName, parsed.CandidateName);
        }

        [Theory]
        [InlineData("انتاج أحمد اليوم", ReportPeriodKind.Today)]
        [InlineData("انتاج أحمد يوم", ReportPeriodKind.Today)]
        [InlineData("انتاج أحمد الاسبوع", ReportPeriodKind.ThisWeek)]
        [InlineData("انتاج أحمد أسبوع", ReportPeriodKind.ThisWeek)] // همزة "أسبوع" لازم تتطبّع زي أي كلمة تانية
        [InlineData("انتاج أحمد الشهر", ReportPeriodKind.ThisMonth)]
        [InlineData("انتاج أحمد شهر", ReportPeriodKind.ThisMonth)]
        public void Extracts_the_period_keyword_and_removes_it_from_the_name(string query, ReportPeriodKind expectedPeriod)
        {
            var parsed = SearchIntentService.ParseIntent(query);

            Assert.NotNull(parsed);
            Assert.Equal(expectedPeriod, parsed!.Period);
            Assert.Equal("احمد", parsed.CandidateName);
        }

        [Fact]
        public void No_period_keyword_leaves_period_null_defaulting_to_this_week_later()
        {
            var parsed = SearchIntentService.ParseIntent("انتاج أحمد");

            Assert.NotNull(parsed);
            Assert.Null(parsed!.Period);
        }

        [Theory]
        [InlineData("متوسط انتاج أحمد")]
        [InlineData("انتاج متوسط أحمد")]
        [InlineData("متوسط الانتاج أحمد")]
        public void Average_and_production_together_in_any_order_become_average_production_intent(string query)
        {
            var parsed = SearchIntentService.ParseIntent(query);

            Assert.NotNull(parsed);
            Assert.Equal(SearchIntentKind.AverageProduction, parsed!.Kind);
            Assert.Equal("احمد", parsed.CandidateName);
        }

        [Fact]
        public void Average_marker_alone_without_production_is_not_a_supported_intent()
        {
            Assert.Null(SearchIntentService.ParseIntent("متوسط أحمد"));
        }

        [Fact]
        public void Average_marker_with_a_different_intent_is_not_supported()
        {
            // "متوسط راتب" مش من النيات المتفق عليها — بس "متوسط انتاج"
            Assert.Null(SearchIntentService.ParseIntent("متوسط راتب أحمد"));
        }

        [Theory]
        [InlineData("مهارات أحمد الاسبوع")]
        [InlineData("تقييم أحمد الشهر")]
        public void Skills_and_standing_ignore_a_period_word_even_if_present(string query)
        {
            var parsed = SearchIntentService.ParseIntent(query);

            Assert.NotNull(parsed);
            Assert.Null(parsed!.Period);
            Assert.Equal("احمد", parsed.CandidateName);
        }

        [Fact]
        public void Intent_keyword_with_no_name_at_all_is_not_a_recognized_intent_for_kinds_without_a_no_name_form()
        {
            // "تقييم" لوحدها من غير اسم مش نية — عكس "غياب"/"انتاج" (بمرجع يوم)
            // اللي بقالهم شكل بلا اسم، "تقييم" لسه محتاجة اسم عامل دايمًا
            Assert.Null(SearchIntentService.ParseIntent("تقييم"));
        }

        [Fact]
        public void Absence_keyword_with_no_name_becomes_the_factory_wide_day_absence_intent()
        {
            var parsed = SearchIntentService.ParseIntent("غياب");

            Assert.NotNull(parsed);
            Assert.Equal(SearchIntentKind.DayAbsence, parsed!.Kind);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Empty_or_whitespace_query_has_no_intent(string? query)
        {
            Assert.Null(SearchIntentService.ParseIntent(query));
        }

        [Fact]
        public void Text_with_no_intent_keyword_at_all_has_no_intent()
        {
            Assert.Null(SearchIntentService.ParseIntent("دبلة تشكيل"));
        }

        private static readonly Dictionary<DayOfWeek, string> ArabicWeekday = new()
        {
            [DayOfWeek.Sunday] = "الاحد",
            [DayOfWeek.Monday] = "الاثنين",
            [DayOfWeek.Tuesday] = "الثلاثاء",
            [DayOfWeek.Wednesday] = "الاربعاء",
            [DayOfWeek.Thursday] = "الخميس",
            [DayOfWeek.Friday] = "الجمعه",
            [DayOfWeek.Saturday] = "السبت",
        };

        [Theory]
        [InlineData(DayOfWeek.Sunday, "الاحد")]
        [InlineData(DayOfWeek.Monday, "الاثنين")]
        [InlineData(DayOfWeek.Tuesday, "الثلاثاء")]
        [InlineData(DayOfWeek.Wednesday, "الاربعاء")]
        [InlineData(DayOfWeek.Thursday, "الخميس")]
        [InlineData(DayOfWeek.Friday, "الجمعه")]
        [InlineData(DayOfWeek.Saturday, "السبت")]
        public void Resolves_each_weekday_word_to_the_most_recent_past_occurrence(DayOfWeek targetDay, string arabicWord)
        {
            var today = new DateTime(2026, 6, 15);

            var parsed = SearchIntentService.ParseIntent($"انتاج يوم {arabicWord} اللي فات", today);

            Assert.NotNull(parsed);
            Assert.Equal(SearchIntentKind.DayProduction, parsed!.Kind);
            Assert.NotNull(parsed.SpecificDay);
            Assert.Equal(targetDay, parsed.SpecificDay!.Value.DayOfWeek);
            Assert.True(parsed.SpecificDay.Value < today); // فات فعلًا، مش النهارده ولا مستقبل
            Assert.True((today - parsed.SpecificDay.Value).Days <= 7); // أقرب تكرار فات، مش أسبوعين لورا
        }

        [Fact]
        public void When_today_is_the_referenced_weekday_it_resolves_to_last_week_not_today()
        {
            var today = new DateTime(2026, 6, 16);
            var wordForToday = ArabicWeekday[today.DayOfWeek];

            var parsed = SearchIntentService.ParseIntent($"انتاج يوم {wordForToday}", today);

            Assert.NotNull(parsed);
            Assert.Equal(today.AddDays(-7), parsed!.SpecificDay);
        }

        [Fact]
        public void Yesterday_and_today_words_resolve_to_the_correct_fixed_offsets()
        {
            var today = new DateTime(2026, 6, 15);

            var todayParsed = SearchIntentService.ParseIntent("انتاج انهارده", today);
            Assert.Equal(today, todayParsed!.SpecificDay);

            var yesterdayParsed = SearchIntentService.ParseIntent("انتاج امبارح", today);
            Assert.Equal(today.AddDays(-1), yesterdayParsed!.SpecificDay);
        }

        [Fact]
        public void Production_keyword_alone_without_a_day_reference_or_name_has_no_intent()
        {
            // "انتاج" لوحدها من غير اسم ومن غير مرجع يوم — غموض عالي، مش نية
            Assert.Null(SearchIntentService.ParseIntent("انتاج"));
        }

        [Theory]
        [InlineData("اعلى انتاج", SearchIntentKind.TopProduction)]
        [InlineData("انتاج اعلى", SearchIntentKind.TopProduction)] // ترتيب حر زي متوسط+انتاج بالظبط
        [InlineData("الاعلى انتاج", SearchIntentKind.TopProduction)]
        [InlineData("اقل انتاج", SearchIntentKind.BottomProduction)]
        [InlineData("انتاج اقل", SearchIntentKind.BottomProduction)]
        public void Top_and_bottom_production_markers_combine_with_production_in_any_order(string query, SearchIntentKind expectedKind)
        {
            var parsed = SearchIntentService.ParseIntent(query);

            Assert.NotNull(parsed);
            Assert.Equal(expectedKind, parsed!.Kind);
            Assert.Equal("", parsed.CandidateName);
        }

        [Fact]
        public void Top_marker_without_production_is_not_a_supported_intent()
        {
            // "اعلى راتب" مش من النيات المتفق عليها — بس "اعلى انتاج"
            Assert.Null(SearchIntentService.ParseIntent("اعلى راتب"));
        }

        [Fact]
        public void Top_production_with_a_leftover_name_is_not_supported_and_falls_back_to_plain_search()
        {
            // النية دي مبتاخدش اسم أصلًا — مزيج زي ده مش مدعوم، بدل تخمين المقصود
            Assert.Null(SearchIntentService.ParseIntent("اعلى انتاج دبلة"));
        }

        [Fact]
        public void Top_production_honors_an_explicit_period_word()
        {
            var parsed = SearchIntentService.ParseIntent("اعلى انتاج الشهر ده");

            Assert.NotNull(parsed);
            Assert.Equal(ReportPeriodKind.ThisMonth, parsed!.Period);
        }

        [Theory]
        [InlineData("احسن عامل", SearchIntentKind.TopWorker)]
        [InlineData("مين احسن عامل", SearchIntentKind.TopWorker)]
        [InlineData("افضل عامل", SearchIntentKind.TopWorker)]
        [InlineData("اسوا عامل", SearchIntentKind.BottomWorker)]
        public void Best_and_worst_worker_keywords_are_recognized_with_no_name_required(string query, SearchIntentKind expectedKind)
        {
            var parsed = SearchIntentService.ParseIntent(query);

            Assert.NotNull(parsed);
            Assert.Equal(expectedKind, parsed!.Kind);
            Assert.Equal("", parsed.CandidateName);
        }

        [Fact]
        public void Best_worker_with_a_leftover_name_is_not_supported()
        {
            Assert.Null(SearchIntentService.ParseIntent("احسن عامل أحمد"));
        }

        [Fact]
        public void Best_worker_without_a_past_marker_is_not_flagged_as_past_period()
        {
            var parsed = SearchIntentService.ParseIntent("احسن عامل الاسبوع ده");

            Assert.NotNull(parsed);
            Assert.False(parsed!.IsPastPeriod);
            Assert.Equal(ReportPeriodKind.ThisWeek, parsed.Period);
        }

        [Theory]
        [InlineData("احسن عامل الاسبوع اللي فات")]
        [InlineData("احسن عامل اللي فات الاسبوع")]
        public void Best_worker_with_a_past_marker_is_flagged_as_past_period(string query)
        {
            var parsed = SearchIntentService.ParseIntent(query);

            Assert.NotNull(parsed);
            Assert.True(parsed!.IsPastPeriod);
            Assert.Equal(ReportPeriodKind.ThisWeek, parsed.Period);
        }

        [Theory]
        [InlineData("شغال على دبلة")]
        [InlineData("مين شغال على دبلة")]
        [InlineData("شغالين دبلة")]
        public void Product_workers_keyword_is_recognized_with_the_name_kept(string query)
        {
            var parsed = SearchIntentService.ParseIntent(query);

            Assert.NotNull(parsed);
            Assert.Equal(SearchIntentKind.ProductWorkers, parsed!.Kind);
            Assert.Equal("دبله", parsed.CandidateName); // بعد التطبيع (ة→ه)
        }

        [Fact]
        public void Product_workers_keyword_alone_without_a_name_has_no_intent()
        {
            Assert.Null(SearchIntentService.ParseIntent("مين شغال"));
        }

        [Theory]
        [InlineData("ضيف مرحلة دبلة")]
        [InlineData("دبلة اضيف مرحلة")]
        public void Add_stage_keyword_requires_the_add_marker_and_keeps_the_product_name(string query)
        {
            var parsed = SearchIntentService.ParseIntent(query);

            Assert.NotNull(parsed);
            Assert.Equal(SearchIntentKind.AddStage, parsed!.Kind);
            Assert.Equal("دبله", parsed.CandidateName);
        }

        [Fact]
        public void Add_stage_keeps_the_attached_lam_preposition_but_still_fuzzy_matches_later()
        {
            // "لدبلة" مكتوبة كلمة واحدة (اللام ملزوقة، زي العربي العادي) —
            // ParseIntent مش بيشيل اللام صراحة، لكن حرف واحد زيادة لسه جوه
            // مدى SearchMatcher الفَزّي (مسافة تحرير 1)، فالمطابقة بعدين
            // بتنجح برضه — شوف Add_stage_intent_resolves_the_product_... تحت
            var parsed = SearchIntentService.ParseIntent("اضيف مرحلة لدبلة");

            Assert.NotNull(parsed);
            Assert.Equal(SearchIntentKind.AddStage, parsed!.Kind);
            Assert.Equal("لدبله", parsed.CandidateName);
        }

        [Fact]
        public void Stage_keyword_without_the_add_marker_is_not_a_supported_intent()
        {
            // "مرحلة دبلة" لوحدها من غير "أضيف"/"ضيف" — بحث نصي عادي، مش فعل
            Assert.Null(SearchIntentService.ParseIntent("مرحلة دبلة"));
        }

        [Fact]
        public void Assign_skill_keyword_requires_the_add_marker_and_keeps_the_worker_name()
        {
            var parsed = SearchIntentService.ParseIntent("ضيف مهارة أحمد");

            Assert.NotNull(parsed);
            Assert.Equal(SearchIntentKind.AssignSkill, parsed!.Kind);
            Assert.Equal("احمد", parsed.CandidateName);
        }

        [Fact]
        public void Skill_keyword_without_the_add_marker_is_not_a_supported_intent()
        {
            Assert.Null(SearchIntentService.ParseIntent("مهارة أحمد"));
        }
    }

    /// <summary>
    /// <see cref="SearchIntentService.AnswerAsync"/> بقاعدة بيانات حقيقية —
    /// بيتأكد إن الاسم بيتطابق (دقيق وبتسامح مع خطأ إملائي) وإن الإجابة
    /// بتنادي السيرفس الصح فعلًا وترجّع أرقامه، مش أرقام مخترعة.
    ///
    /// كل الاختبارات هنا بتستخدم DateTime.Today الحقيقي (مش TestDatabase.Today
    /// الثابت) — لأن SearchIntentService بيعيد استخدام ReportPeriod.Resolve
    /// الموجودة أصلًا، واللي بتحسب "الأسبوع ده" من DateTime.Today الحقيقي
    /// زي أي مكان تاني في البرنامج بيسأل عن "الفترة الحالية".
    /// </summary>
    public class SearchIntentServiceTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime ThisWeekStart => WeeklySummaryService.GetWorkWeekRange(DateTime.Today).WeekStart;

        private SearchIntentService Intent(IServiceScope scope) =>
            _db.GetService<SearchIntentService>(scope);

        [Fact]
        public async Task Absence_intent_with_no_absences_this_week_says_so_explicitly()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            db.Attendances.Add(new Attendance
            {
                WorkerId = TestDatabase.WorkerAhmedId, Date = ThisWeekStart, Status = AttendanceStatus.Present
            });
            await db.SaveChangesAsync();

            var answer = await Intent(scope).AnswerAsync("غياب أحمد");

            Assert.NotNull(answer);
            Assert.Equal(SearchIntentKind.Absence, answer!.Kind);
            Assert.Equal("لا يوجد غياب في الفترة دي", answer.EmptyNote);
        }

        [Fact]
        public async Task Absence_intent_reports_real_excused_and_unexcused_counts()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            db.Attendances.AddRange(
                new Attendance { WorkerId = TestDatabase.WorkerAhmedId, Date = ThisWeekStart, Status = AttendanceStatus.AbsentWithPermission },
                new Attendance { WorkerId = TestDatabase.WorkerAhmedId, Date = ThisWeekStart.AddDays(1), Status = AttendanceStatus.AbsentWithoutPermission });
            await db.SaveChangesAsync();

            var answer = await Intent(scope).AnswerAsync("غياب أحمد");

            Assert.NotNull(answer);
            Assert.Null(answer!.EmptyNote);
            Assert.Contains(answer.Lines, l => l.Label == "غياب بإذن" && l.Value.StartsWith('1'));
            Assert.Contains(answer.Lines, l => l.Label == "غياب من غير إذن" && l.Value.StartsWith('1'));
        }

        [Fact]
        public async Task Absence_intent_matches_the_name_even_with_a_typo()
        {
            using var scope = _db.CreateScope();

            // "اجمد" بدل "أحمد" — استبدال حرف واحد، تسامح فَزّي متوقّع
            var answer = await Intent(scope).AnswerAsync("غياب اجمد");

            Assert.NotNull(answer);
            Assert.Equal(TestDatabase.WorkerAhmedId, answer!.WorkerId);
        }

        [Fact]
        public async Task No_matching_name_returns_null_so_the_plain_search_takes_over()
        {
            using var scope = _db.CreateScope();

            var answer = await Intent(scope).AnswerAsync("غياب مصطفى_مش_موجود_خالص");

            Assert.Null(answer);
        }

        [Fact]
        public async Task Penalties_intent_with_none_this_week_says_so_explicitly()
        {
            using var scope = _db.CreateScope();

            var answer = await Intent(scope).AnswerAsync("جزاء أحمد");

            Assert.NotNull(answer);
            Assert.Equal("لا يوجد جزاءات في الفترة دي", answer!.EmptyNote);
        }

        [Fact]
        public async Task Penalties_intent_lists_the_real_seeded_penalty()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            db.Penalties.Add(new Penalty
            {
                WorkerId = TestDatabase.WorkerAhmedId, Date = ThisWeekStart,
                Reason = "تدخين أثناء الشغل", Deduction = PenaltyDeduction.HalfDay
            });
            await db.SaveChangesAsync();

            var answer = await Intent(scope).AnswerAsync("جزاءات أحمد");

            Assert.NotNull(answer);
            Assert.Null(answer!.EmptyNote);
            Assert.Contains(answer.Lines, l => l.Value.Contains("تدخين أثناء الشغل"));
        }

        [Fact]
        public async Task Adjustments_intent_with_none_this_week_says_so_explicitly()
        {
            using var scope = _db.CreateScope();

            var answer = await Intent(scope).AnswerAsync("سلف أحمد");

            Assert.NotNull(answer);
            Assert.Equal("لا يوجد سلف أو حوافز في الفترة دي", answer!.EmptyNote);
        }

        [Fact]
        public async Task Adjustments_intent_lists_the_real_seeded_advance()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            db.WageAdjustments.Add(new WageAdjustment
            {
                WorkerId = TestDatabase.WorkerAhmedId, Date = ThisWeekStart,
                Type = WageAdjustmentType.Advance, AmountEgp = 150m
            });
            await db.SaveChangesAsync();

            var answer = await Intent(scope).AnswerAsync("حوافز أحمد");

            Assert.NotNull(answer);
            Assert.Contains(answer!.Lines, l => l.Label == "إجمالي السلف" && l.Value.Contains("150"));
        }

        [Fact]
        public async Task Wage_intent_warns_when_the_worker_has_no_daily_wage_rate()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            db.Workers.Add(new Worker { Id = 900, FullName = "بلال بلا يومية", IsActive = true, DailyWageEgp = 0m });
            await db.SaveChangesAsync();

            var answer = await Intent(scope).AnswerAsync("راتب بلال بلا يومية");

            Assert.NotNull(answer);
            Assert.Equal(SearchIntentKind.Wage, answer!.Kind);
            Assert.Contains("مفيش سعر يومية", answer.EmptyNote);
        }

        [Fact]
        public async Task Skills_intent_with_no_skills_says_so_explicitly()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            // أحمد الأساسي في التخم مربوط بمهارات فعلًا (شوف TestDatabase.Seed) —
            // عامل جديد بدون أي WorkerSkill عشان نتأكد من حالة "مفيش مهارات" الحقيقية
            db.Workers.Add(new Worker { Id = 901, FullName = "سامي بلا مهارات", IsActive = true, DailyWageEgp = 200m });
            await db.SaveChangesAsync();

            var answer = await Intent(scope).AnswerAsync("مهارات سامي بلا مهارات");

            Assert.NotNull(answer);
            Assert.Equal(SearchIntentKind.Skills, answer!.Kind);
            Assert.Equal("العامل ده لسه من غير أي تقييم مهارة مسجّل", answer.EmptyNote);
        }

        [Fact]
        public async Task Skills_intent_lists_the_seeded_skills_when_present()
        {
            using var scope = _db.CreateScope();

            // أحمد الأساسي مربوط بمهارات على كل مراحل "دبلة" (شوف TestDatabase.Seed)
            var answer = await Intent(scope).AnswerAsync("مهارات أحمد");

            Assert.NotNull(answer);
            Assert.Equal(SearchIntentKind.Skills, answer!.Kind);
            Assert.Null(answer.EmptyNote);
            Assert.NotEmpty(answer.Lines);
        }

        [Fact]
        public async Task Standing_intent_says_so_explicitly_when_worker_is_not_in_this_weeks_comparison()
        {
            using var scope = _db.CreateScope();

            // أحمد مالوش إنتاج مسجّل الأسبوع ده في السيناريو الافتراضي —
            // يعني مش داخل مقارنة الترتيب، ولازم يتقال بوضوح مش سكوت
            var answer = await Intent(scope).AnswerAsync("تقييم أحمد");

            Assert.NotNull(answer);
            Assert.Equal(SearchIntentKind.Standing, answer!.Kind);
            Assert.NotNull(answer.EmptyNote);
        }

        [Fact]
        public async Task Production_intent_resolves_to_the_product_when_the_name_matches_a_product_not_a_worker()
        {
            using var scope = _db.CreateScope();

            var answer = await Intent(scope).AnswerAsync("انتاج دبلة");

            Assert.NotNull(answer);
            Assert.Equal(SearchIntentKind.Production, answer!.Kind);
            Assert.Equal(TestDatabase.ProductRingId, answer.ProductId);
            Assert.Null(answer.WorkerId);
        }

        [Fact]
        public async Task Production_intent_resolves_to_the_worker_when_the_name_matches_a_worker_not_a_product()
        {
            using var scope = _db.CreateScope();

            var answer = await Intent(scope).AnswerAsync("انتاج أحمد");

            Assert.NotNull(answer);
            Assert.Equal(SearchIntentKind.Production, answer!.Kind);
            Assert.Equal(TestDatabase.WorkerAhmedId, answer.WorkerId);
            Assert.Null(answer.ProductId);
        }

        [Fact]
        public async Task Production_intent_with_no_data_in_range_says_so_explicitly_not_silently()
        {
            using var scope = _db.CreateScope();

            var answer = await Intent(scope).AnswerAsync("انتاج دبلة");

            Assert.NotNull(answer);
            Assert.Equal("لا يوجد إنتاج مسجّل للمنتج ده في الفترة دي", answer!.EmptyNote);
        }

        [Fact]
        public async Task Average_production_intent_divides_the_same_totals_it_would_otherwise_show_raw()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            db.Attendances.Add(new Attendance
            {
                WorkerId = TestDatabase.WorkerAhmedId, Date = ThisWeekStart, Status = AttendanceStatus.Present
            });
            db.DailyProductions.Add(new DailyProduction
            {
                WorkerId = TestDatabase.WorkerAhmedId, ProductionStageId = TestDatabase.RingStage1Id,
                Date = ThisWeekStart, PieceCount = 100, PiecesPerWorkdayAtEntry = 100
            });
            await db.SaveChangesAsync();

            var answer = await Intent(scope).AnswerAsync("متوسط انتاج أحمد");

            Assert.NotNull(answer);
            Assert.Equal(SearchIntentKind.AverageProduction, answer!.Kind);
            Assert.Null(answer.EmptyNote);
            Assert.Contains(answer.Lines, l => l.Label == "متوسط الإنتاج لكل يومية");
        }

        [Fact]
        public async Task Day_production_intent_with_no_data_says_so_explicitly()
        {
            using var scope = _db.CreateScope();

            var answer = await Intent(scope).AnswerAsync("انتاج امبارح");

            Assert.NotNull(answer);
            Assert.Equal(SearchIntentKind.DayProduction, answer!.Kind);
            Assert.NotNull(answer.EmptyNote);
        }

        [Fact]
        public async Task Day_production_intent_reports_real_output_for_a_specific_past_day()
        {
            using var scope = _db.CreateScope();
            var flow = _db.GetService<ProductionFlowService>(scope);
            var yesterday = DateTime.Today.AddDays(-1);

            var range = new FlowRangeDto
            {
                FromStageId = TestDatabase.BagStage1Id, ToStageId = TestDatabase.BagStage3Id, PieceCount = 40
            };
            var shares = new List<FlowShareDto>
            {
                new() { ProductionStageId = TestDatabase.BagStage1Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 40 },
                new() { ProductionStageId = TestDatabase.BagStage2Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 40 },
                new() { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 40 },
            };
            await flow.RecordFlowAsync(TestDatabase.ProductBagId, yesterday, new[] { range }, shares, confirmOverride: true);

            var answer = await Intent(scope).AnswerAsync("انتاج امبارح");

            Assert.NotNull(answer);
            Assert.Equal(SearchIntentKind.DayProduction, answer!.Kind);
            Assert.Null(answer.EmptyNote);
            Assert.Contains(answer.Lines, l => l.Label == "تم إنتاجه (تام)" && l.Value.Contains("40"));
        }

        [Fact]
        public async Task Day_absence_intent_with_nothing_absent_says_so_explicitly()
        {
            using var scope = _db.CreateScope();

            var answer = await Intent(scope).AnswerAsync("غياب");

            Assert.NotNull(answer);
            Assert.Equal(SearchIntentKind.DayAbsence, answer!.Kind);
            Assert.Equal("لا يوجد غياب في الفترة دي", answer.EmptyNote);
        }

        [Fact]
        public async Task Day_absence_intent_lists_real_absent_workers_factory_wide()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            db.Attendances.Add(new Attendance
            {
                WorkerId = TestDatabase.WorkerAhmedId, Date = DateTime.Today, Status = AttendanceStatus.AbsentWithoutPermission
            });
            await db.SaveChangesAsync();

            var answer = await Intent(scope).AnswerAsync("غياب");

            Assert.NotNull(answer);
            Assert.Null(answer!.EmptyNote);
            Assert.Contains(answer.Lines, l => l.Label == "أحمد" && l.Value.Contains("من غير إذن"));
        }

        [Fact]
        public async Task Day_absence_intent_honors_an_explicit_period_word()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            // خارج مدى "النهارده" الافتراضي، بس داخل الأسبوع ده
            db.Attendances.Add(new Attendance
            {
                WorkerId = TestDatabase.WorkerAhmedId, Date = ThisWeekStart, Status = AttendanceStatus.AbsentWithPermission
            });
            await db.SaveChangesAsync();

            var thisWeek = await Intent(scope).AnswerAsync("غياب الاسبوع ده");

            Assert.NotNull(thisWeek);
            Assert.Null(thisWeek!.EmptyNote);
            Assert.Contains(thisWeek.Lines, l => l.Label == "أحمد");
        }

        [Fact]
        public async Task Top_bottom_production_intent_with_no_data_says_so_explicitly()
        {
            using var scope = _db.CreateScope();

            var answer = await Intent(scope).AnswerAsync("اعلى انتاج");

            Assert.NotNull(answer);
            Assert.Equal(SearchIntentKind.TopProduction, answer!.Kind);
            Assert.NotNull(answer.EmptyNote);
        }

        /// <summary>دبلة (أحمد) بـ50 قطعة مقابل شنطة (سعيد) بـ20 — أعلى/أقل واضحين، مفيش تعادل</summary>
        private async Task SeedTwoProductsWithDifferentOutputAsync(IServiceScope scope)
        {
            var flow = _db.GetService<ProductionFlowService>(scope);

            await flow.RecordFlowAsync(TestDatabase.ProductRingId, ThisWeekStart,
                new[] { new FlowRangeDto { FromStageId = TestDatabase.RingStage1Id, ToStageId = TestDatabase.RingStage2Id, PieceCount = 50 } },
                new List<FlowShareDto>
                {
                    new() { ProductionStageId = TestDatabase.RingStage1Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 50 },
                    new() { ProductionStageId = TestDatabase.RingStage2Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 50 },
                }, confirmOverride: true);

            await flow.RecordFlowAsync(TestDatabase.ProductBagId, ThisWeekStart,
                new[] { new FlowRangeDto { FromStageId = TestDatabase.BagStage1Id, ToStageId = TestDatabase.BagStage3Id, PieceCount = 20 } },
                new List<FlowShareDto>
                {
                    new() { ProductionStageId = TestDatabase.BagStage1Id, WorkerId = TestDatabase.WorkerSaidId, PieceCount = 20 },
                    new() { ProductionStageId = TestDatabase.BagStage2Id, WorkerId = TestDatabase.WorkerSaidId, PieceCount = 20 },
                    new() { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerSaidId, PieceCount = 20 },
                }, confirmOverride: true);
        }

        [Fact]
        public async Task Top_production_intent_identifies_the_highest_producing_product_and_worker()
        {
            using var scope = _db.CreateScope();
            await SeedTwoProductsWithDifferentOutputAsync(scope);

            var answer = await Intent(scope).AnswerAsync("اعلى انتاج الاسبوع ده");

            Assert.NotNull(answer);
            Assert.Null(answer!.EmptyNote);
            Assert.Contains(answer.Lines, l => l.Label == "أعلى منتج" && l.Value.Contains("دبلة"));
            Assert.Contains(answer.Lines, l => l.Label == "أعلى عامل" && l.Value.Contains("أحمد"));
            Assert.Equal(TestDatabase.ProductRingId, answer.ProductId);
        }

        [Fact]
        public async Task Bottom_production_intent_identifies_the_lowest_producing_product_and_worker()
        {
            using var scope = _db.CreateScope();
            await SeedTwoProductsWithDifferentOutputAsync(scope);

            var answer = await Intent(scope).AnswerAsync("اقل انتاج الاسبوع ده");

            Assert.NotNull(answer);
            Assert.Contains(answer!.Lines, l => l.Label == "أقل منتج" && l.Value.Contains("شنطة"));
            Assert.Contains(answer.Lines, l => l.Label == "أقل عامل" && l.Value.Contains("سعيد"));
        }

        [Fact]
        public async Task Best_worker_intent_with_no_eligible_workers_says_so_explicitly()
        {
            using var scope = _db.CreateScope();

            var answer = await Intent(scope).AnswerAsync("احسن عامل");

            Assert.NotNull(answer);
            Assert.Equal(SearchIntentKind.TopWorker, answer!.Kind);
            Assert.NotNull(answer.EmptyNote);
        }

        [Fact]
        public async Task Best_worker_intent_with_past_marker_uses_the_past_period_label_in_the_title()
        {
            using var scope = _db.CreateScope();

            var answer = await Intent(scope).AnswerAsync("احسن عامل الاسبوع اللي فات");

            Assert.NotNull(answer);
            Assert.Contains("اللي فات", answer!.Title);
        }

        [Fact]
        public async Task Top_and_bottom_worker_intents_return_different_workers_when_more_than_one_is_eligible()
        {
            using var scope = _db.CreateScope();
            // نفس عملاء بذرة "أعلى/أقل إنتاج" — أحمد وسعيد الاتنين شغّالين
            // فعليًا الأسبوع ده، يعني الاتنين مؤهلين للترتيب
            await SeedTwoProductsWithDifferentOutputAsync(scope);

            var top = await Intent(scope).AnswerAsync("احسن عامل");
            var bottom = await Intent(scope).AnswerAsync("اسوا عامل");

            Assert.NotNull(top);
            Assert.NotNull(bottom);
            Assert.NotNull(top!.WorkerId);
            Assert.NotNull(bottom!.WorkerId);
            Assert.NotEqual(top.WorkerId, bottom.WorkerId);
            Assert.Contains(top.Lines, l => l.Label == "الترتيب" && l.Value.StartsWith("1 "));
        }

        [Fact]
        public async Task Product_workers_intent_lists_qualified_workers_for_a_product()
        {
            using var scope = _db.CreateScope();

            // أحمد وسعيد الاتنين مربوطين بمهارات على كل مراحل "دبلة" أصلًا (شوف TestDatabase.Seed)
            var answer = await Intent(scope).AnswerAsync("شغال على دبلة");

            Assert.NotNull(answer);
            Assert.Equal(SearchIntentKind.ProductWorkers, answer!.Kind);
            Assert.Null(answer.EmptyNote);
            Assert.Contains(answer.Lines, l => l.Label == "أحمد");
            Assert.Contains(answer.Lines, l => l.Label == "سعيد");
            Assert.Equal(TestDatabase.ProductRingId, answer.ProductId);
        }

        [Fact]
        public async Task Product_workers_intent_resolves_to_a_stage_when_the_name_matches_a_stage_not_a_product()
        {
            using var scope = _db.CreateScope();

            var answer = await Intent(scope).AnswerAsync("شغال على تشكيل");

            Assert.NotNull(answer);
            Assert.Equal(SearchIntentKind.ProductWorkers, answer!.Kind);
            Assert.Equal(TestDatabase.ProductRingId, answer.ProductId); // تشكيل مرحلة في دبلة
            Assert.Equal("تشكيل", answer.Name);
        }

        [Fact]
        public async Task Product_workers_intent_with_no_matching_product_or_stage_returns_null()
        {
            using var scope = _db.CreateScope();

            var answer = await Intent(scope).AnswerAsync("شغال على حاجة مش موجودة خالص");

            Assert.Null(answer);
        }

        [Fact]
        public async Task Add_stage_intent_resolves_the_product_without_writing_anything()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            var stagesBefore = await db.ProductionStages.CountAsync();

            var answer = await Intent(scope).AnswerAsync("اضيف مرحلة لدبلة");

            Assert.NotNull(answer);
            Assert.Equal(SearchIntentKind.AddStage, answer!.Kind);
            Assert.Equal(TestDatabase.ProductRingId, answer.ProductId);
            Assert.Equal(stagesBefore, await db.ProductionStages.CountAsync()); // مفيش كتابة فعلية من AnswerAsync
        }

        [Fact]
        public async Task Add_stage_intent_with_no_matching_product_returns_null()
        {
            using var scope = _db.CreateScope();

            var answer = await Intent(scope).AnswerAsync("اضيف مرحلة لمنتج مش موجود خالص");

            Assert.Null(answer);
        }

        [Fact]
        public async Task Assign_skill_intent_resolves_the_worker_without_writing_anything()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            var skillsBefore = await db.WorkerSkills.CountAsync();

            var answer = await Intent(scope).AnswerAsync("اضيف مهارة لأحمد");

            Assert.NotNull(answer);
            Assert.Equal(SearchIntentKind.AssignSkill, answer!.Kind);
            Assert.Equal(TestDatabase.WorkerAhmedId, answer.WorkerId);
            Assert.Equal(skillsBefore, await db.WorkerSkills.CountAsync()); // مفيش كتابة فعلية من AnswerAsync
        }

        [Fact]
        public async Task Assign_skill_intent_with_no_matching_worker_returns_null()
        {
            using var scope = _db.CreateScope();

            var answer = await Intent(scope).AnswerAsync("اضيف مهارة لمصطفى_مش_موجود_خالص");

            Assert.Null(answer);
        }
    }
}
