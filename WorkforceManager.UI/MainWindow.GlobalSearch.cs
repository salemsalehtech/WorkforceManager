using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Helpers;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using WorkforceManager.UI.Views;

namespace WorkforceManager.UI
{
    // ======================= البحث الشامل (GlobalSearchDialog) =======================
    // فصل عن MainWindow.xaml.cs الأساسي — تجميع النتائج من كل الفئات
    // العشرة ومطابقة الفئتين المحليّتين (إعدادات/دليل)، والهبوط على
    // النتيجة المختارة. شوف CLAUDE.md لتفاصيل تصميم البحث الشامل.
    public partial class MainWindow
    {
        /// <summary>
        /// وقت استقرار الشاشة بعد التنقّل قبل ما كود الهبوط يلمس عنصر
        /// جواها — بعض الشاشات بتحمّل بياناتها async على Loaded (نفس
        /// السبب اللي RunTourAsync مستخدم تأخير مشابه له، 150-400ms، لنفس
        /// المشكلة بالظبط). مش كل فئة محتاجاه: أي حاجة بتتبنى على خاصية
        /// SearchText بسيطة (العمال/المنتجات) بتتظبط **قبل** التحميل
        /// الطبيعي فبتتلقط منه تلقائيًا من غير تأخير — التأخير هنا للحاجات
        /// اللي محتاجة صف حقيقي من قايمة لسه بتتحمّل (تحديد عامل بعينه،
        /// تسليط مرحلة، فتح خطة ذاكرة، فتح تبويب رصيد أولي، فتح بروفايل
        /// حساب إداري).
        /// </summary>
        private const int SearchLandingSettleDelayMs = 300;

        /// <summary>
        /// بحث سريع شامل — بيغطي كل الفئات العشرة الموثّقة في CLAUDE.md.
        /// الديالوج نفسه بينادي <see cref="SearchAllCategoriesAsync"/> لكل
        /// بحث (مش قايمة محمّلة مرة واحدة زي قبل كده)، لأن سجل العمليات
        /// وحده محتاج استعلام حي؛ الهبوط على النتيجة بعد الاختيار في
        /// <see cref="LandOnSearchResultAsync"/>.
        /// </summary>
        private async void GlobalSearch_Click(object sender, RoutedEventArgs e)
        {
            var chosen = GlobalSearchDialog.Ask(this, SearchAllCategoriesAsync);
            if (chosen is null) return;

            await LandOnSearchResultAsync(chosen);
        }

        /// <summary>
        /// بيجمع نتايج GlobalSearchService (الفئات الثمانية المرتبطة
        /// بقاعدة البيانات) مع فئتي الإعدادات والدليل الثابتين في قايمة
        /// واحدة مرتبة — الفئتين دول محتوى واجهة بحت (مفيش استعلام
        /// يرجّعهم)، فمطابقتهم بتحصل هنا مباشرة بنفس محرك المطابقة
        /// وأوزان الحقول اللي GlobalSearchService نفسها بتستخدمها
        /// (GlobalSearchService.BestMatch) — عشان الترجيح يفضل قاعدة
        /// واحدة في كل مكان.
        /// </summary>
        private async Task<IReadOnlyList<GlobalSearchResult>> SearchAllCategoriesAsync(string query)
        {
            var results = (await _session.GetRequiredService<GlobalSearchService>().SearchAsync(query)).ToList();

            results.AddRange(MatchSettings(query));
            results.AddRange(MatchHelpContent(query));
            results.AddRange(MatchScreens(query));

            // إجابة النية (لو العبارة اتفهمت كنية) بتتحط دايمًا على الرأس —
            // شوف SearchIntentService.AnswerResultScore. مفيش خصم اسكور هنا:
            // النية مقصودة صراحة، مش تخمين نصي زي باقي النتايج
            var intentAnswer = await _session.GetRequiredService<SearchIntentService>().AnswerAsync(query);
            if (intentAnswer is not null)
            {
                results.Add(new GlobalSearchResult
                {
                    Category = SearchCategory.IntentAnswer,
                    // Name فاضية للنيات اللي بلا اسم (DayProduction/DayAbsence) —
                    // العنوان الكامل هو أقرب نص معروض متاح في الحالة دي
                    PrimaryText = intentAnswer.Name ?? intentAnswer.Title,
                    Score = SearchIntentService.AnswerResultScore,
                    WorkerId = intentAnswer.WorkerId,
                    ProductId = intentAnswer.ProductId,
                    IntentAnswer = intentAnswer
                });
            }

            ApplyUsageRanking(query, results);

            return results.OrderByDescending(r => r.Score).ToList();
        }

        /// <summary>
        /// ترقية "الترتيب بالاستخدام": نتيجة اتختارت قبل كده لنفس الاستعلام
        /// (بعد التطبيع) بتاخد ترقية درجة صغيرة — شوف SearchRankingScorer
        /// لصيغة الحساب والتبرير الكامل. إجابة النية مش داخلة هنا أصلًا
        /// (درجتها الثابتة فوق أي ترقية ممكنة بالتصميم).
        /// </summary>
        private static void ApplyUsageRanking(string query, List<GlobalSearchResult> results)
        {
            var normalizedQuery = ArabicSearch.Normalize(query);
            var picks = SearchRankingStore.Load().PicksByQuery.GetValueOrDefault(normalizedQuery);
            if (picks is null || picks.Count == 0) return;

            var now = DateTime.Now;
            foreach (var result in results)
            {
                if (result.Category == SearchCategory.IntentAnswer) continue;

                var key = GlobalSearchService.RankingKey(result);
                var pick = key is null ? null : picks.FirstOrDefault(p => p.ResultKey == key);
                if (pick is null) continue;

                result.Score += SearchRankingScorer.ComputeBoost(pick.PickCount, pick.LastPickedAt, now);
            }
        }

        private static IEnumerable<GlobalSearchResult> MatchSettings(string query)
        {
            foreach (var setting in Tour.SearchableSettings.Entries)
            {
                var match = GlobalSearchService.BestMatch(
                    query,
                    (setting.Title, GlobalSearchService.PrimaryFieldWeight),
                    (setting.Description, GlobalSearchService.SecondaryFieldWeight));
                if (match is null) continue;

                yield return new GlobalSearchResult
                {
                    Category = SearchCategory.Setting,
                    PrimaryText = setting.Title,
                    SecondaryText = setting.Description,
                    Score = match.Value.Score,
                    SettingTargetElementName = setting.TargetElementName
                };
            }
        }

        /// <summary>
        /// فئة "تنقّل بالشاشة" — دوسة عليها تودّي **للشاشة كلها**، مش عنصر
        /// معيّن جواها زي Setting. Score بيتقيّم على العنوان بس (مفيش وصف
        /// ثانوي هنا، عكس Setting).
        /// </summary>
        private static IEnumerable<GlobalSearchResult> MatchScreens(string query)
        {
            foreach (var screen in Tour.NavigableScreens.Entries)
            {
                var match = GlobalSearchService.BestMatch(query, (screen.Title, GlobalSearchService.PrimaryFieldWeight));
                if (match is null) continue;

                yield return new GlobalSearchResult
                {
                    Category = SearchCategory.Screen,
                    PrimaryText = screen.Title,
                    Score = match.Value.Score,
                    NavItemName = screen.NavItemName
                };
            }
        }

        /// <summary>كل موضوع دليل مع أب المستوى الأول بتاعه (نفسه لو هو نفسه مستوى أول) — لازمة للهبوط لاحقًا على SubTopic مش بس للمطابقة</summary>
        private static IEnumerable<(Tour.HelpTopic TopLevel, Tour.HelpTopic Topic)> AllHelpTopics()
        {
            foreach (var top in Tour.HelpTopics.Topics)
            {
                yield return (top, top);
                foreach (var sub in top.SubTopics)
                    yield return (top, sub);
            }
        }

        private static IEnumerable<GlobalSearchResult> MatchHelpContent(string query)
        {
            foreach (var (_, topic) in AllHelpTopics())
            {
                var match = GlobalSearchService.BestMatch(
                    query,
                    (topic.Title, GlobalSearchService.PrimaryFieldWeight),
                    (topic.Description, GlobalSearchService.SecondaryFieldWeight));
                if (match is null) continue;

                yield return new GlobalSearchResult
                {
                    Category = SearchCategory.HelpTopic,
                    PrimaryText = topic.Title,
                    SecondaryText = topic.Description,
                    Score = match.Value.Score
                };
            }

            foreach (var faq in Tour.HelpFaq.Entries)
            {
                var match = GlobalSearchService.BestMatch(
                    query,
                    (faq.Question, GlobalSearchService.PrimaryFieldWeight),
                    (faq.Answer, GlobalSearchService.SecondaryFieldWeight));
                if (match is null) continue;

                yield return new GlobalSearchResult
                {
                    Category = SearchCategory.HelpTopic,
                    PrimaryText = faq.Question,
                    SecondaryText = faq.Answer,
                    Score = match.Value.Score,
                    IsFaqEntry = true
                };
            }
        }

        /// <summary>
        /// بيوصّل المستخدم للعنصر المختار بالظبط قدر الإمكان — نفس منطق
        /// "روّح على الشاشة الصح ثم اظبط الشاشة على العنصر ده" اللي
        /// GlobalSearchDialog القديمة كانت بتعمله للعمال/المنتجات بس،
        /// موسّع للعشر فئات كلهم.
        /// </summary>
        private async Task LandOnSearchResultAsync(GlobalSearchResult chosen)
        {
            switch (chosen.Category)
            {
                case SearchCategory.Worker:
                    await LandOnWorkerAsync(chosen.PrimaryText, chosen.WorkerId);
                    break;

                case SearchCategory.Product:
                    LandOnProduct(chosen.PrimaryText);
                    break;

                case SearchCategory.ProductionStage:
                    NavProductsItem.IsChecked = true;
                    if (MainContent?.Content is ProductsView stageView &&
                        stageView.DataContext is ViewModels.ProductsViewModel stageVm)
                    {
                        // اسم المنتج الأب (SecondaryText) بيوصّل لصفحة المنتج الصح —
                        // اسم المرحلة نفسه ممكن يتكرر عبر منتجات تانية
                        stageVm.SearchText = chosen.SecondaryText ?? chosen.PrimaryText;
                        await Task.Delay(SearchLandingSettleDelayMs);

                        var stageRow = stageVm.Stages.FirstOrDefault(s => s.StageId == chosen.ProductionStageId);
                        if (stageRow is not null &&
                            stageView.FindName("ProductStagesList") is ItemsControl stagesList &&
                            stagesList.ItemContainerGenerator.ContainerFromItem(stageRow) is FrameworkElement stageContainer)
                        {
                            stageContainer.BringIntoView();
                        }
                    }
                    break;

                case SearchCategory.InitialBalance:
                    NavDailyEntryItem.IsChecked = true;
                    await Task.Delay(SearchLandingSettleDelayMs);
                    if (chosen.ProductId is { } balanceProductId)
                    {
                        _session.GetRequiredService<ViewModels.DailyEntryViewModel>()
                            .OpenInitialBalanceTabCommand.Execute(new ViewModels.ProductOption { ProductId = balanceProductId });
                    }
                    break;

                case SearchCategory.MemoryPlan:
                    NavMemoryItem.IsChecked = true;
                    await Task.Delay(SearchLandingSettleDelayMs);
                    if (MainContent?.Content is MemoryView { DataContext: ViewModels.MemoryViewModel memoryVm } &&
                        chosen.MemoryPlanId is { } planId)
                    {
                        var plan = await _session.GetRequiredService<ProductionMemoryService>().GetAsync(planId);
                        if (plan is not null) memoryVm.EditCommand.Execute(plan);
                    }
                    break;

                case SearchCategory.ActivityLogEntry:
                    NavActivityLogItem.IsChecked = true;
                    if (MainContent?.Content is ActivityLogView { DataContext: ViewModels.ActivityLogViewModel logVm })
                    {
                        // زي بحث العمال/المنتجات: بتتظبط قبل التحميل الطبيعي
                        // (Loaded → LoadAsync) بدل ما تتنادى تاني وتتسابق معاه
                        var day = (chosen.ActivityEventOccurredAt ?? DateTime.Today).Date;
                        logVm.FromDate = day;
                        logVm.ToDate = day;
                        logVm.SearchText = chosen.PrimaryText;
                    }
                    break;

                case SearchCategory.ReportTemplate:
                    NavReportsItem.IsChecked = true;
                    // القوالب بتتحمّل تزامنيًا في الـ Constructor (ReportTemplateStore.Load
                    // ملف JSON بسيط)، فمفيش تأخير محتاج هنا عكس باقي الفئات
                    if (MainContent?.Content is ReportBuilderView { DataContext: ViewModels.ReportBuilderViewModel reportVm })
                        reportVm.SelectedTemplate = reportVm.Templates.FirstOrDefault(t => t.Name == chosen.ReportTemplateName);
                    break;

                case SearchCategory.DepartmentAccount:
                    NavDepartmentAccountsItem.IsChecked = true;
                    await Task.Delay(SearchLandingSettleDelayMs);
                    if (MainContent?.Content is DepartmentAccountsView { DataContext: ViewModels.DepartmentAccountsViewModel deptVm })
                    {
                        var account = deptVm.Accounts.FirstOrDefault(a => a.WorkerId == chosen.WorkerId);
                        if (account is not null) deptVm.OpenProfileCommand.Execute(account);
                    }
                    break;

                case SearchCategory.Setting:
                    NavSettingsItem.IsChecked = true;
                    if (MainContent?.Content is FrameworkElement settingsView && chosen.SettingTargetElementName is not null &&
                        settingsView.FindName(chosen.SettingTargetElementName) is FrameworkElement settingElement)
                    {
                        settingElement.BringIntoView();
                    }
                    break;

                case SearchCategory.Screen:
                    // معالجة عامة واحدة للشاشات العشرة كلهم — نفس آلية IsChecked
                    // الموحّدة الموجودة أصلًا، بس بالاسم من NavigableScreens مش
                    // سويتش مكرّر لكل شاشة. FindName على النافذة نفسها (مش
                    // MainContent) لأن أزرار التنقل عايشة في MainWindow.xaml
                    if (chosen.NavItemName is not null && FindName(chosen.NavItemName) is RadioButton navItem)
                        navItem.IsChecked = true;
                    break;

                case SearchCategory.HelpTopic:
                    NavHelpItem.IsChecked = true;
                    if (MainContent?.Content is HelpView { DataContext: ViewModels.HelpViewModel helpVm })
                    {
                        if (chosen.IsFaqEntry)
                        {
                            var faq = Tour.HelpFaq.Entries.FirstOrDefault(f => f.Question == chosen.PrimaryText);
                            if (faq is not null) helpVm.ToggleFaqCommand.Execute(faq);
                        }
                        else
                        {
                            var match = AllHelpTopics().FirstOrDefault(pair => pair.Topic.Title == chosen.PrimaryText);
                            if (match.Topic is not null)
                            {
                                helpVm.SelectTopicCommand.Execute(match.TopLevel);
                                if (!ReferenceEquals(match.Topic, match.TopLevel))
                                    helpVm.ToggleSubTopicCommand.Execute(match.Topic);
                            }
                        }
                    }
                    break;

                case SearchCategory.IntentAnswer:
                    // فعل ("أضيف مرحلة/مهارة") لازم هبوط خاص بيفتح نفس
                    // الديالوج/اللوحة اللي المستخدم كان هيفتحها بإيده —
                    // مش مجرد تنقّل زي باقي إجابات النية
                    if (chosen.IntentAnswer?.Kind == SearchIntentKind.AddStage)
                        await LandOnAddStageAsync(chosen);
                    else if (chosen.IntentAnswer?.Kind == SearchIntentKind.AssignSkill)
                        await LandOnAssignSkillAsync(chosen);
                    // باقي إجابات النية: نفس هبوط فئتي Worker/Product بالظبط،
                    // بس بالاسم الصافي (IntentAnswer.Name)، مش عنوان البطاقة
                    // الكامل (PrimaryText هنا = نفس الاسم الصافي أصلًا، شوف
                    // SearchAllCategoriesAsync)
                    else if (chosen.WorkerId is not null) await LandOnWorkerAsync(chosen.PrimaryText, chosen.WorkerId);
                    else if (chosen.ProductId is not null) LandOnProduct(chosen.PrimaryText);
                    break;
            }
        }

        /// <summary>
        /// "أضيف مرحلة" — بيهبط على المنتج بنفس منطق فئة Product العادي،
        /// وبعدين بينادي نفس AddStageCommand اللي زرار "إضافة مرحلة" في
        /// الشاشة نفسها بينادّيه — StageEditDialog بيفتح فاضي، والمستخدم
        /// بيملاه ويحفظ زي العادة تمامًا (مفيش كتابة تلقائية من هنا).
        /// </summary>
        private async Task LandOnAddStageAsync(GlobalSearchResult chosen)
        {
            NavProductsItem.IsChecked = true;
            if (MainContent?.Content is not ProductsView { DataContext: ViewModels.ProductsViewModel productsVm }) return;

            productsVm.SearchText = chosen.PrimaryText;
            await Task.Delay(SearchLandingSettleDelayMs);

            var row = productsVm.Products.FirstOrDefault(p => p.ProductId == chosen.ProductId);
            if (row is null) return;

            productsVm.SelectProductCommand.Execute(row);
            productsVm.AddStageCommand.Execute(null);
        }

        /// <summary>
        /// "أضيف مهارة" — بيهبط على العامل بنفس منطق فئة Worker العادي،
        /// وبعدين بيفتح "وضع الإضافة" على كارته — تأخير تاني بعد تحديد
        /// العامل لازم هنا (عكس هبوط Worker العادي): SelectedWorker بيحمّل
        /// Detail بشكل غير متزامن (WorkersViewModel.OnSelectedWorkerChanged
        /// → LoadDetailAsync)، وToggleAddSkillsCommand محتاج Detail جاهز.
        /// </summary>
        private async Task LandOnAssignSkillAsync(GlobalSearchResult chosen)
        {
            NavWorkersItem.IsChecked = true;
            if (MainContent?.Content is not WorkersView { DataContext: ViewModels.WorkersViewModel workersVm }) return;

            workersVm.SearchText = chosen.PrimaryText;
            await Task.Delay(SearchLandingSettleDelayMs);

            workersVm.SelectedWorker = workersVm.Workers.FirstOrDefault(w => w.WorkerId == chosen.WorkerId);
            if (workersVm.SelectedWorker is null) return;

            await Task.Delay(SearchLandingSettleDelayMs);
            if (workersVm.Detail is not null && !workersVm.Detail.IsAddingSkills)
                workersVm.ToggleAddSkillsCommand.Execute(null);
        }

        /// <summary>هبوط على عامل بعينه — مشترك بين فئة Worker العادية وإجابة نية عن عامل</summary>
        private async Task LandOnWorkerAsync(string searchName, int? workerId)
        {
            NavWorkersItem.IsChecked = true;
            if (MainContent?.Content is WorkersView { DataContext: ViewModels.WorkersViewModel workersVm })
            {
                // بتتظبط قبل التحميل الطبيعي فبيلقطها لوحده أول ما يخلص
                workersVm.SearchText = searchName;
                await Task.Delay(SearchLandingSettleDelayMs);
                workersVm.SelectedWorker = workersVm.Workers.FirstOrDefault(w => w.WorkerId == workerId);
            }
        }

        /// <summary>هبوط على منتج بعينه — مشترك بين فئة Product العادية وإجابة نية عن منتج</summary>
        private void LandOnProduct(string searchName)
        {
            NavProductsItem.IsChecked = true;
            if (MainContent?.Content is ProductsView { DataContext: ViewModels.ProductsViewModel productsVm })
                productsVm.SearchText = searchName;
        }
    }
}
