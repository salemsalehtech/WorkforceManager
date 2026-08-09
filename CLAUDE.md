# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

WorkforceManager (نظام إدارة إنتاجية وأجور العمال) is a WPF desktop app for managing factory workers, their
skills, products and their manufacturing stages, daily piece-production entry (with automatic "workday"
calculation), attendance, and performance evaluation vs. team average. All in-code comments and docs are
written in Arabic — follow that convention when editing existing files.

The solution root is `WorkforceManager/` (contains `WorkforceManager.sln`), one level below the repo root.

## Commands

Run from inside the `WorkforceManager/` folder (where the `.sln` lives):

```bash
dotnet restore

# one-time global tool needed for migrations
dotnet tool install --global dotnet-ef

# create/update a migration after changing entities in WorkforceManager.Core/Models or AppDbContext
dotnet ef migrations add <MigrationName> --project WorkforceManager.Data --startup-project WorkforceManager.UI

# run the app (auto-creates + seeds the SQLite DB on first run)
dotnet run --project WorkforceManager.UI

# build / restore only
dotnet build
```

From the **repo root** (one level above the `.sln`):

```powershell
# build the distributable — wipes dist/ first, so only ONE copy ever exists
.\publish.ps1                      # self-contained folder + zip (~172 MB / ~70 MB)
.\publish.ps1 -Mode SingleFile     # one compressed .exe (~70 MB)
.\publish.ps1 -Mode Light          # framework-dependent (~15 MB, needs .NET 8 Desktop Runtime installed)

# nuke all build artifacts (bin/obj/dist) — everything here is regenerable
.\clean.ps1
.\clean.ps1 -KeepDist
```

Both scripts must stay **UTF-8 with BOM** — Windows PowerShell 5.1 reads `.ps1` as ANSI without a BOM
and the Arabic strings break the parser.

`publish-assets/` holds the files copied into every release (`اقرأني.txt`, `portable.marker`) — they live
in the repo, not only inside a zip. Releases ship **without** a `Data/` folder; the app creates and seeds
its own on first run.

Size discipline (the repo was once 741 MB, 99.8% of it regenerable build output):
- `Directory.Build.props` (next to the `.sln`) holds everything shared by all 4 projects —
  `ImplicitUsings`, `Nullable`, version identity, `SatelliteResourceLanguages` (drops 13 unused
  translation folders, ~16 MB/release), and `DebugType=embedded` for Release (no `.pdb` files, but crash
  stack traces keep line numbers).
- `WorkforceManager.UI` pins `RuntimeIdentifier=win-x64` — without it the SQLite package copies native
  libs for 20 platforms (linux-mips64, wasm, maccatalyst…), ~24 MB per build. Note this puts build output
  under `bin\<Config>\net8.0-windows\win-x64\`.
- `Microsoft.EntityFrameworkCore.Design` is referenced `Condition="'$(Configuration)' == 'Debug'"` in both
  UI and Data — it drags in Roslyn (~13 MB). `dotnet ef` builds Debug by default so migrations still work.

`WorkforceManager.Tests` (xUnit, `net8.0`, 291 tests) covers the worker-assignment rule, daily output,
the skill-rating system, worker filtering, product activity, pending work, the worker report,
activity-log retention, database integrity, deletion scope, the report builder, fresh-install seeding, and the removed-field guards — run with `dotnet test`
from the `WorkforceManager/` folder. It spins up a real SQLite file DB per test (`TestDatabase`), not the
EF InMemory provider, because the concurrency tests need SQLite's actual write lock. `TestDatabase` mirrors
the DI registrations from `App.xaml.cs`, so a service added there but not here fails the tests on purpose.

`WorkforceManager.UiTests` (xUnit, `net8.0-windows`, `UseWPF`) is a **separate** project for one test:
`XamlLoadTests` loads **every** compiled XAML file for real. It exists because a whole class of XAML errors
is invisible to both the compiler and every other test, and only shows up when the screen opens on the
user's machine — a bad `PackIconKind` name, a missing `StaticResource` key, a duplicate `x:Name`, a
`TargetName` outside its namescope, or **`BasedOn="{DynamicResource ...}"`** (`BasedOn` is a plain CLR
property, not a DependencyProperty, so `DynamicResource` on it throws at load). That last one shipped once
and made the app refuse to open at all, because `MainWindow`'s constructor builds `WorkersView` — a load
error in the default screen kills the whole window. The test enumerates the assembly's **BAML resource
table**, not file paths, so a new `.xaml` is covered without anyone remembering to add it; screens are
constructed with `null` for their DI arguments (every view calls `InitializeComponent()` first, so the XAML
still loads) and it runs on a manually created STA thread rather than pulling in an extra xUnit package.
Two failure shapes are deliberately ignored: anything that is **not** a `XamlParseException` (the XAML
loaded; the constructor just wanted a real ViewModel) and "Cannot locate resource" (`Application.ResourceAssembly`
is pinned to the test host, so window icons by relative URI can't resolve there).

The SQLite DB lives outside the repo at `%LocalAppData%\WorkforceManager\workforce.db` (or in `Data\` next
to the exe when a `portable.marker` file is present — see `AppPaths`). `App.OnStartup` creates/updates it
with `Database.MigrateAsync()` + `DatabaseSeeder.SeedIfEmptyAsync`, so migrations DO run at startup and
schema changes reach an existing customer DB without wiping data.

## Architecture

Simplified Clean Architecture across 4 projects, each with its own `.csproj`, referenced in one direction only:

```
Core  <-  Data  <-  Business  <-  UI
Core  <-------------Business
Core  <----------------------- UI
```

- **WorkforceManager.Core** — POCO models (`Models/`), enums (`Enums/`), and repository interfaces
  (`Interfaces/`). Zero dependency on EF Core or WPF — this is what would let SQLite be swapped for
  SQL Server later without touching models or business logic.
- **WorkforceManager.Data** — EF Core + SQLite. `AppDbContext` is the single point of contact with the
  database (all relationships/cascade rules configured in `OnModelCreating`); `Repositories/` implement
  the Core interfaces; nothing outside this project talks to `AppDbContext` directly.
- **WorkforceManager.Business** — all business rules live here, nowhere else (especially not in UI code):
  `WorkdayCalculationService`, `PerformanceEvaluationService`, `AttendanceService`, `ProductionFlowService`,
  `WeeklySummaryService`, `PenaltyService`, `WorkerManagementService`, `ProductManagementService`,
  `ProductionReportService`, `WageAdjustmentService`, `AuthService`, `WeeklyReportExcelService`, plus
  their DTOs in `DTOs/`.
- **WorkforceManager.UI** — WPF, MVVM (CommunityToolkit.Mvvm) + MaterialDesignThemes. `App.xaml.cs` wires
  up DI via `Microsoft.Extensions.Hosting`'s `Host` (`AppHost`) — this is the single place new
  repositories/services/views get registered. `WorkersView` (+ `WorkersViewModel`, `WorkerEditDialog`) is
  implemented as a **card list** (same `WorkerCard` style as the attendance screen), not a grid: summary
  bar (active / hourly / inactive + a "needs attention" button that filters to problem
  workers), instant search, `FilterChip` quick filters (الكل / بالإنتاج / بالساعة / موقوفين), and a sort
  dropdown. **Best-of-week is its own highlighted card** in grid column 0 of the summary row — the screen
  is RTL, so column 0 renders on the visual right; it shows photo + name + strongest product
  (`WorkerRow.TopSkillProduct`) and the whole card is a button running `OpenBestWorkerCommand`, which
  clears any active filter first so the selection is actually visible. Who counts as best worker is
  still decided in `WeeklySummaryService` — the card only renders it.
  Behind a single **"فلاتر وترتيب"** button (`ToolbarToggle` + popup, `IsFilterMenuOpen`, with
  `ActiveFilterCount` as a badge) sit four **composable** dropdowns (stage / product / min-stars /
  today's attendance) plus the sort — the same pattern and the same shared styles as the products
  screen, replacing a permanent second toolbar row.
  All of them AND together with the chip in `WorkerFilterRules` (Business) — the chip is
  the mutually-exclusive scope (`WorkerPayScope`), the dropdowns narrow it further. `null` on a
  criterion means "filter off", never "match empty"; `AverageStars <= 0` means "no skills" and is
  excluded from any stars filter rather than treated as zero stars. The rule is pure and lives in
  Business precisely so `WorkerFilterTests` can cover it without a ViewModel.
  The whole worker list is loaded once via `IWorkerRepository.GetAllWithSkillsAsync()` and
  search/filter/sort run **in memory** (`ApplyFilters`) — that is why search is per-keystroke with no
  DB round-trip; `WorkerRow.SkillsSearchText` pre-joins skills + notes so skill search stays a single
  string match. Each card flags `HasNoWage` (worker would earn 0 EGP on the payroll) and `HasNoSkills`
  (piece-rate worker with no skill links never appears in a production flow) — both surfaced together as
  `NeedsAttention`. Each product card in the profile carries a **rating badge on its own header** —
  stars + a one-word level ("ممتاز" / "عادي" …) colour-coded by level, from
  `SkillProductGroup.AverageStars` (which delegates to `SkillRatingService.ProductStars`, so the
  "unknown stages don't count as zero" rule stays in one place) plus `StarsLabel`. This **replaced the
  free-text "ملاحظات المهارات" field**, which is gone from the add/edit form (see Domain model notes for
  why the column survives). It deliberately lives on the card rather than in a separate list section:
  the same information next to the product it describes, at no extra vertical space. `AverageStars` is
  memoised and invalidated by `RefreshCounters` → `RefreshRating`, because six bound properties read it
  on every render. Skill assignment happens inside the profile: `IsAddingSkills` widens the cards to
  every product, and the star row shows on not-yet-assigned stages too (`SkillStageItem.ShowStars`) —
  clicking a star there **assigns the skill at that rating in one gesture**
  (`SetSkillStarsCommand` → `AssignSkillAsync` then `SetStarsAsync`). **The panel never closes by
  itself, and two separate mechanisms are needed to keep that true** — both were added after it broke:
  1. `_reloadingRows` — reloading the list calls `Workers.Clear()`, and WPF drops the `Selector`'s
     selection the instant the row is removed, so `SelectedWorker` goes null for a moment. Without the
     guard, `OnSelectedWorkerChanged(null)` set `Detail = null`, which destroyed the very snapshot
     `RestorePanelState` reads — so `previous` came back null and the panel rebuilt collapsed with
     add-mode off. The guard ignores **only** a null that arrives mid-reload; a real deselection still
     closes the panel. This also fixes removing a skill outside add-mode, which closed the open card.
  2. `_skillRowsStale` — while `IsAddingSkills` is on, `RefreshRowsKeepingSelectionAsync` doesn't reload
     at all; it just marks the rows stale. The skill itself is written to the DB immediately — only the
     list card's skills counter waits. `FlushPendingRowRefreshAsync` runs it once when add-mode ends,
     whether by the "خلصت" button (`ToggleAddSkillsAsync`) or by leaving the worker (`LoadDetailAsync`
     turns add-mode off and flushes, then returns and lets the flush's own re-selection load the new
     profile — one load, not two). The flag is cleared **before** the refresh so re-selection can't
     recurse.

  There is deliberately **no banner** inside add-mode: it held a hint line, a duplicate "خلصت إضافة"
  button (the header's "إضافة مهارات"/"خلصت" toggle already does it), and a `RecentlyAdded` chip
  counter — all removed as clutter competing with the cards themselves. Also add/edit/soft-delete, plus an optional
  profile photo. `DailyEntryView` is implemented: one shared date +
  3 tabs — production-flow entry, topped by a day summary bar (pieces / workdays / workers / products,
  derived from the records `LoadDayRecordsAsync` already loads — no extra query). Each stage card carries
  a colour bar and label driven by `FlowStageRow.State` (`FlowStageState`: Ready / NeedsWorkers /
  Mismatch / WorkersWithoutPieces / NotToday) so an 11-stage product reads at a glance instead of card by
  card, and the session header shows `ReadinessText` ("7 من 11 مرحلة جاهزة"). `RefreshState` /
  `RefreshReadiness` are driven from `RecomputeTotals`, so anything touching pieces or shares repaints
  both. Every stage's worker picker has its own search box (`WorkerSearch` → `VisibleWorkers`) since a
  stage can have ~20 qualified workers. Three rules make placing workers down an 11-stage line bearable,
  all driven from `AddWorkerToStageAsync`:
  (1) a worker **already assigned somewhere else today sinks to the bottom** of every other stage's list
  and carries a "مكلّف على {product} / {stage}" tag. This is not cosmetic — `WorkerAssignmentGuard` allows
  one assignment per worker per day, so picking them raises a confirmation dialog; the tag explains it
  before it happens. `DescribeAssignedElsewhere` is deliberately **synchronous** (it runs per keystroke),
  reading a cached `_savedDayAssignments` plus every open session's live chips — the same two sources the
  guard itself measures against. `RefreshAllWorkerPickers()` re-sorts every list in every session after
  each add/remove, since the moment the ordering matters is the moment the next stage's list is opened,
  not when it's typed into. `OrderBy` is stable, so the rating order survives inside each group.
  (2) the suggestions list **closes on every add** (`IsPickerOpen`, reset last inside `ResetWorkerPicker`).
  Focus alone couldn't close it — focus stays in the box, so the list re-opened over the next stage. The
  user re-opens it by clicking the box or typing; `WorkerSearch_Clicked` is bound to mouse-down and **not**
  to `GotKeyboardFocus`, because the click-to-add path restores focus to the box and that would re-open it.
  (3) the view then **moves the caret to the next stage's search box and scrolls the just-filled stage to
  the top** (`WorkerAdded` event → `FocusStageSearch` + `ScrollStageToTop`, dispatched at `Loaded` priority
  so the new chip and the closed list are already laid out). Focus first, scroll second — focusing makes
  WPF bring the box into view on its own, so our scroll has to be the last word. The next stage skips any
  with no qualified workers (their picker is collapsed, so the caret would land nowhere). Top, not past it:
  a stage may need a second worker, and scrolling past would force a scroll back. Focus is decided **only**
  here, never in `Suggestions_Click`, so mouse-add and Enter-add behave identically.
  "كرّر يوم فات" is `RepeatLastDayAsync` (see `GetLastFlowAsync`).
  One or MORE products per day: each product gets its own
  `FlowSessionViewModel` card — stages as ordered cards, qualified-only workers per stage with equal
  auto-split + manual override, stage ranges "from stage X to Y: N pieces", live per-worker workdays
  preview, independent save. A range has no "where did these pieces come from?" picker: that question
  died with the batch entity (see Business logic notes), so a range is just from-stage/to-stage/pieces.
  "add product" button appends sessions; row-level commands live on the row
  view-models via callbacks, not RelativeSource), a "سجلات اليوم" correction tab (edit/delete saved
  production records), a **unified** attendance grid (upsert per worker/date) that replaced the
  separate "العمال بالساعة" tab — piece-rate and hourly workers in one list, status picked via inline
  single-select `ToggleButton` chips (`StatusChip`/`ShiftChip` styles in App.xaml) instead of a
  dropdown, options served by `AttendanceStatusCatalog.ForWorker(isHourly)` (reads the
  `AttendanceStatus` enum, never a hardcoded list — both types currently share all three statuses).
  Hourly rows add three shift chips from `HourlyWorkdayService.ShiftPresets` (شيفت عادي / لحد 8م /
  لحد 12 = 1 / 1.5 / 2 workdays), the only three distinct outcomes the ladder can produce; the old
  13-entry end-hour dropdown is gone from the UI though `RecordHourlyWorkAsync` still accepts any hour.
  Mutual exclusion lives in `AttendanceRow.OnChoiceToggled`; picking a shift also marks the worker
  Present. Then penalties (add with reason/deduction, list + delete for the day), and an "السلف والحوافز" tab
  (advances/bonuses in EGP: pick worker + type + amount + note, list with delete; سلفة red, حافز green).
  **The reports and the evaluation are two separate screens on purpose**, because they do two different
  jobs. `ReportsView` (nav: "التقييم والمتابعة") is looked at and acted on: a **"محتاج تصرّف" list above
  everything else** (`NeedsAttentionService`), then the day's evaluation vs team average, the day's output,
  and the products chart. The attention list is the point of the screen — a table of averages states a fact
  but doesn't say what to *do*, so the manager reads it and closes it. `NeedsAttentionService` answers the
  other question: which stage has **zero qualified workers** (severity 0 — the flow screen only offers
  qualified workers, so that stage is impossible to record and the user finds out while standing at it),
  who was absent unexcused, **who dropped against their own average** (not the team's — a slow-but-steady
  worker is fine, a declining one is not, even while still above team average; 25% below their own
  4-week-per-worked-day mean), who has no skills or no wage rate, and whose ratings are older than
  `StaleRatingDays`. It computes nothing new: evaluation, week range and ratings all come from their
  existing services.
  `ReportBuilderView` (nav: "التقارير") is the document factory — see the report engine below.
  `ProductsView` is implemented with the same card language as the workers/attendance screens: summary bar,
  instant search (product or stage name), `FilterChip` filters, and product cards showing stage count
  only — a `TotalQuota` stat (sum of every active stage's `PiecesPerWorkday`) was removed on purpose:
  summing quotas across sequential stages measures nothing, since a piece passes through the stages in
  order rather than in parallel, so the number just grew with stage count. Don't reintroduce it.
  **The screen is driven by a period**, defaulting to the current work week and served by
  `ProductActivityService` (which delegates to `WeeklySummaryService.GetWorkWeekRange` — do not define a
  second "this week" anywhere). The period controls the filter and the stats together, so the number on
  screen and the filter applied always cover the same span:
  - The first chip is **"شغّالين"** (`ProductFilter.WorkedThisPeriod`). It means *actual logged
    production in the period* (`ProductActivityDto.WorkedInPeriod`), **not** the `Product.IsActive` flag
    the old "نشط" chip read — a product untouched for months stayed "active" forever, so the count said
    nothing. `IsActive` still backs the "موقوف" chip, which is a different question. The label used to
    carry the period too ("شغّالين الأسبوع ده على") and no longer does: the period button sits right
    next to it, so that was a fourth copy of the same fact.
  - Summary stats are **"أكتر منتج إنتاجًا" / "أقل منتج إنتاجًا"**; the old "الإجمالي" and
    "إجمالي المراحل" are gone (near-constant numbers nobody acted on). "Least active" ranks only
    products that actually worked — including the zeros would just surface the first product
    alphabetically.
  - Three more filters AND together with the chip: stage **by name** (the same stage name repeats across
    products, and the user asks "which products have لمعة?"), worker (who worked on it in the period),
    and a volume sort.

  **All of the chrome lives on one row** — search, chips, result count, a "فلاتر" button, a period
  button. It used to be three stacked rows that stated the period **four** times (two DatePickers, two
  quick buttons, a descriptive line, and the chip's own label). Now `PeriodLabel` names the period on
  its button ("الأسبوع ده" / "الشهر ده" / "آخر 30 يوم" / a date range) and everything else — the quick
  choices, the custom from/to pickers, and the full `PeriodText` description — lives inside its popup.
  The three dropdowns moved into the "فلاتر" popup with `ActiveFilterCount` shown as a badge, because a
  filter you can't see is a list you can't explain. Both popups are driven by `IsPeriodMenuOpen` /
  `IsFilterMenuOpen` on the ViewModel rather than code-behind, so picking a period closes its own menu.
  `ToolbarToggle` + `ToolbarPopupCard` (App.xaml) are the shared styles for this pattern — use them
  rather than growing another toolbar row.
  The right panel renders the product as a **production line** — one card per stage with its
  position number, quota, **how many workers are qualified for it**, and a 👤🔍 button opening
  `QualifiedWorkersDialog` (who can do this stage, best-rated first). That dialog calls
  `SkillRatingService.GetRankedForStageAsync` — **the same method the daily-entry screen uses**, so the
  order the manager sees here is the order they get while recording. Plus ▲▼ buttons that reorder the
  line. Reordering goes through `ProductManagementService.MoveStageAsync(stageId, moveUp)`, which swaps
  with the neighbour and then **renumbers the whole line from 1** (healing gaps/duplicates left by older
  edits); it returns false at the ends instead of throwing. Order is not cosmetic — production ranges
  ("from stage X to Y") are resolved by line position, so `StageOrderTests` covers both the swap itself
  and the fact that a range which was valid before a move becomes invalid after it. Warnings surface the
  two states that silently block production: a product with no active stages, and an active stage with
  **zero qualified workers** (the flow screen only offers qualified workers, so such a stage can never be
  filled). Stage names stay unique per product, and quota edits only affect future entries thanks to the
  snapshot.
  **The design system lives in `Themes/`, not in `App.xaml`.** `Palette.Light.xaml` / `Palette.Dark.xaml`
  hold the identity; `Core.xaml` holds the sizes, the font, and the component styles. Each palette also
  carries the **old brush names** (`BrandBrush`, `CardBgBrush`, `TextPrimaryBrush`…) mapped onto the new
  tokens, so screens that haven't been redesigned yet still match; that block gets deleted with the last
  migrated screen. It deliberately does **not** live in a separate `Compat.xaml` any more — see the
  Freezable trap below. **Gold is an accent, never body text on white**: `#C2A14D` on white is 2.2:1
  contrast where 4.5 is the readable minimum, so gold goes on the logo, the active nav item, hero numbers,
  focus rings and the primary button, and `GoldDeepBrush` (5.9:1) is the only gold allowed as text on a
  light surface. That scarcity is also why it reads as expensive rather than loud.
  **Never declare a brush directly in `Application.Resources`.** WPF resolves direct resources *before*
  merged dictionaries, so a key defined there silently beats the same key in a theme file — which is
  exactly what happened: 24 old brushes sat directly in `App.xaml`, the compat bridge was dead from the day
  it was written, and every screen except the redesigned one was still painting itself in the old
  blue/orange while the theme files looked correct. `AppFont` had the same collision (Tajawal direct vs
  Segoe UI in Core). The only things allowed to stay direct are the `PrimaryHue*`/`SecondaryHue*`
  overrides, because beating MaterialDesign's own merged dictionary is the entire point of them — and they
  now take their colours from the palette via `DynamicResource` rather than literals. `PrimaryHue*` is
  MaterialDesign's accent (checkbox tick, radio dot, selected calendar day, tab indicator), so it must be
  **gold**; it was briefly wired to ink, which in the black theme made every checkbox a white square.
  **A `SolidColorBrush` declared in a `ResourceDictionary` gets frozen, so a `DynamicResource` inside it
  resolves exactly once and never updates.** This is why the old-name bridge had to move *into* the palette
  files instead of sitting in its own `Compat.xaml`: those aliases captured the light palette at startup and
  stayed light forever, so half of dark mode was simply the light theme wearing a dark page. A palette file
  is swapped wholesale by `ApplyTheme`, so aliases defined inside it are rebuilt against the new colours and
  the freeze can't bite. Verify with `app.TryFindResource("CardBgBrush")` after a switch, not by eye.
  **There is no green, no blue, and no primary red in the palette.** They were the most saturated things
  on screen, so the least important numbers pulled the eye hardest and the identity broke. The status slots
  survive but all live in the gold family: `Good` = gold ("yes" in this identity), `Warn` = deeper bronze,
  `Info` = neutral, and `Danger` = a warm brick. Danger is the one deliberate exception — a delete button
  that looks like a save button is a safety problem, not an aesthetic choice — and it is warm enough to
  belong beside gold rather than read as a traffic light.
  **There is no navy anywhere in the identity, in either theme.** The first pass paired gold with navy ink
  (`#1B2E4A`) in the light theme, and it fought the gold for attention — a second strong hue in a screen
  that is supposed to read as one colour family. Light ink is now a warm charcoal/brown-grey (`#342E28`),
  computed to the **same WCAG relative luminance** as the navy it replaced (so contrast ratios didn't
  regress — 12:1 on the warm ground, 13.4:1 on white), just with the blue channel pulled out. The sidebar,
  `InkOnAccentBrush`, `InfoBrush`, and the hero-card gradient (`InkGradientBrush`) all moved the same way.
  Dark was built without navy from the start — neutral greys with zero blue cast, because navy surfaces
  there turned the whole screen into a dim blue smear and chilled the gold. Its six-step ladder (`#000000`
  sidebar → `#0B0B0C` ground → `#141416` card → `#1D1D20` raised → `#2A2A2E` line → `#3A3A40` strong line)
  is spaced so layers separate without needing a border; an earlier version put ground and card 8 points
  apart, which is invisible on a real monitor. Ink is `#EDEDED`, never `#FFFFFF` — pure white on black
  haloes and hurts after an hour. The warm parchment ground (`#F6F2E9`) is what makes gold read as the
  identity rather than a foreign accent; a cold blue-grey ground made the same gold look like a mistake.
  **If a colour needs replacing and it must keep its current contrast ratio, don't eyeball a substitute** —
  solve for it: convert the old colour to WCAG relative luminance, hold that luminance constant, and only
  change the hue. A grey with `R=G=B` at that luminance is the neutral anchor; nudging R up and B down a
  few points from there gives a warm tilt without moving the contrast number.
  **Theme switching is live** (`App.ApplyTheme` swaps the palette dictionary *in place*): every new style
  references colours through `DynamicResource`, so the binding stays alive. `StaticResource` is why it used
  to need a restart — a screen written with it resolves colours once at load. New XAML must use
  `DynamicResource` for anything from the palette. **The one exception is `BasedOn`**, which must stay
  `StaticResource`: it is a plain CLR property on `Style`, not a DependencyProperty, so `DynamicResource`
  on it throws at load — and it costs nothing, since the colours *inside* the base style are still
  `DynamicResource` and stay live. A blanket StaticResource→DynamicResource sweep will hit `BasedOn`;
  `XamlLoadTests` is what catches it.
  **A ViewModel never returns a colour — it returns a palette key.** `AttendanceVisuals.ColorFor`,
  `WorkerRow.NetColor`, `FlowStageRow.StateColor`, `SkillProductGroup.RatingColor`, `DailyReportRow.RatingColor`,
  `ReportsViewModel.ChartPalette` and friends all hand back a resource name (`"GoodBrush"`, `"Series3Brush"`),
  and the XAML binds it through `ThemeBrush.ForegroundKey` / `BackgroundKey` / `BorderKey` (`ThemeBrush.cs`).
  They used to return literal hex (`"#0B6E4F"`, `"#B00020"`), which meant dark mode rendered the *same* green
  and red as light mode no matter what the theme files said — the second reason colours looked inconsistent.
  `ThemeBrush` calls `SetResourceReference`, the programmatic equivalent of `DynamicResource`, so the binding
  stays live and a theme switch reaches these elements too; a plain `IValueConverter` would return a dead
  brush and silently break live switching. Chart series are `Series1Brush`…`Series8Brush` + `SeriesOtherBrush`,
  defined per theme and **alternating gold/warm-charcoal** so adjacent stack segments stay distinguishable;
  the light theme's `Series2Brush`/`Series8Brush` were also fixed here — they were near-duplicate navy
  shades (five points apart in one channel) even before the navy removal, so two different products could
  land on visually identical bars.
  **`ApplyTheme` also flips MaterialDesign's own `BaseTheme`** via `PaletteHelper`. The `BundledTheme` in
  `App.xaml` is pinned to `Light`, and without that call the 22 ComboBoxes, 39 DataGrids and 10 DatePickers
  kept drawing themselves from the library's light theme on top of a black page — no palette change could
  ever reach them, because they never read our brushes at all.
  **A control with no template gets Windows' default chrome, which ignores every palette.** The ComboBoxes
  carried a style that set only padding and font ("without rebuilding the inner template, to avoid
  unnecessary risk"), so they kept Aero's white gradient box; `DatePickerTextBox` was worse, because
  declaring *any* implicit style for it replaces MaterialDesign's entirely and drops it to the bare WPF
  template. `ComboBox` and `DatePicker` now inherit `MaterialDesignOutlinedComboBox` /
  `MaterialDesignDatePicker` and only re-set colours on top. **Do not hand-roll a ComboBox template here:**
  the first attempt did, and its selection `ContentPresenter` silently ignored `DisplayMemberPath` — which
  every ComboBox in this app uses — so each one rendered its record's `ToString()`
  (`StageFilterOption { StageId = 1, Display = GRS }`) instead of the stage name. Selection-box template
  resolution is subtler than one `ContentPresenter`, and the library's template already handles it.
  `DatePickerTextBox` does keep a full hand-written template, because there is no library style left to
  inherit once an implicit style for it exists.
  **Every filled button takes its foreground from `InkOnAccentBrush`, never `White`.** `BaseActionButton`
  hardcoded white, and `PrimaryButton`'s background was `BrandBrush` — the *ink* colour — so in the black
  theme the main action became a near-white rectangle with white text on it. Primary and Success are both
  the gold gradient now (one identity, one "yes"). `DangerButton` is deliberately **tinted** rather than
  solid: the brick tone inverts between themes, so a solid fill would need light text in one and dark in
  the other, while `DangerTintBrush` background + `DangerBrush` text is contrast-safe in both by
  construction.
  **An implicit `TextBlock` style sets `Foreground`**, because WPF's default is black and dozens of
  TextBlocks in this app never set one — invisible on a black page, and perfectly fine-looking in the light
  theme, which is why it went unnoticed for so long.
  All four sidebar screens are implemented. Navigation uses `Checked` (not `Click`) on the sidebar
  radios — handlers guard against the initial `Checked` that fires during `InitializeComponent` before
  `MainContent` exists. `App.xaml` holds the design system: brand brushes (BrandBrush/AccentBrush/
  Success/Danger/Warn + bg variants) and keyed styles (`Card`, `ToolbarCard`, `PrimaryButton`,
  `SuccessButton`, `DangerButton`, `GhostButton`, `IconButton`, `ModernDataGrid` + header/cell/row
  styles, `NavItem`) — style new UI from these resources, never inline colors; local DataGrid RowStyles
  must use `BasedOn="{StaticResource ModernGridRow}"`. ViewModels take `IServiceScopeFactory` and create a scope per operation
  (keeps DbContext short-lived). Gotcha: WPF implicit styles don't apply to derived types, so the
  `TargetType="Window"` style in App.xaml does NOT hit `MainWindow` — set `FlowDirection="RightToLeft"`
  explicitly on each window. **The same derived-type rule bites text inputs**: the implicit
  `TargetType="TextBox"` style does not reach `DatePickerTextBox`, which needs its own style.
- **Text selection colours are set once, in `App.xaml`'s implicit `TextBox`/`PasswordBox`/
  `DatePickerTextBox` styles** — never per screen. They use `TextSelectionBrush` (#2C7BE5) with an
  explicit `SelectionOpacity`, deliberately **separate** from `SelectionBgBrush` (#E3EDFB). The latter
  is the selected-row background for cards and is pale on purpose so black text stays readable on it;
  when it was also wired to `SelectionBrush`, selection rendered at 0.4 opacity over a white field and
  was effectively invisible — users were selecting text and seeing nothing happen. No `TextBox` in the
  app carries an explicit style, so fixing the implicit one covers every screen.
- **Shared worker rendering**: `Views/WorkerAvatar` (photo, else initials) is the only place a worker's
  avatar is drawn — worker cards, the best-worker card, and the qualified-workers dialog all use it.
  Its `PhotoData` DP is typed `object`, not `byte[]`, because XAML rejects array-typed properties inside
  a `DataTemplate` ("Tags of type 'PropertyArrayStart' are not supported in template sections") and the
  control lives inside list templates.
- **Stored images** (product photos and worker photos) all go through `StoredImageHelper` — downscale to
  256px, re-encode as JPEG, return null for unreadable data so callers fall back to initials.

### The report engine

- **Every report in the app is one `ReportSpec`**: subject × period × grouping × filters. The four reports
  that used to be hand-written tabs (weekly sheet, period payroll, general production, worker report) turned
  out to be *the same report with four settings*, so they were generalised rather than joined by a fifth.
  They now ship as built-in templates in `ReportTemplateStore` — nothing was taken away from the user, and
  everything became editable.
- **`ReportBuilderService` adds no arithmetic.** Wages come from `PayrollService`, workdays from
  `WorkdayMath`, absence deduction from `AbsenceDeductionRule`, the week from `WeeklySummaryService`. It
  groups and shapes only. `ReportBuilderTests` asserts its totals equal those services' own output —
  because the real danger isn't a report that crashes, it's a report that quietly prints a *different*
  number than the screen showing the same thing.
- **The "القطع" column means two different things, and `CountsCompletedOutput` is the only place that
  decides which.** Grouping by **worker or stage** makes the group a *unit of work*: summing its rows is
  its own effort, every row a real hand-worked record earning its own workday, so all stages count.
  Grouping by **product, day, or week** makes the group a *bucket of output* answering "how much left the
  line?" — and since one piece passes through every stage, summing them all counts it once per stage.
  This shipped wrong: an 11-stage product reported **11× its real output** (110,000 instead of 10,000 on
  real data) while the chart and the daily report — both already on `ProductionLine.LastStageIdByProduct`
  — showed the truth. Any new grouping must pick a side here, and the whole app must keep answering with
  one number.
- **Every subject returns the same `ReportTable`**, so there is **one** Excel exporter
  (`ReportTableExcelService`) and **one** preview grid for all six subjects and their groupings, instead of
  six of each. The preview grid's columns are built in code-behind from `PreviewHeaders`, since XAML can't
  generate columns from a list — that's the only reason that code-behind exists.
- **Exactly one preview may be in flight, and none before the screen is ready.** Every selection setter
  calls `RequestPreview`, so opening the screen used to fire four overlapping fire-and-forget builds — the
  constructor's `RefreshGroupings`, then both filter defaults, then `InitializeAsync`'s own — each clearing
  and refilling the same collections, which is what made the table visibly flash on entry. `_ready` gates
  everything until `InitializeAsync` finishes (in a `finally`, or a failed filter load would freeze the
  screen for good), `_suppressPreview` brackets multi-setter changes, and `_previewGeneration` drops
  results that arrive after a newer request — without it a slow wide-range query can land *after* the
  narrow one that replaced it and repaint stale numbers. Column rebuilds coalesce for the same reason:
  one per report, not one per header added.
- **Not every combination is offered.** `ReportSpec.AllowedGroupings` encodes which cuts have meaning
  ("attendance by product" has no answer), so the screen never lets the user reach an empty report and
  mistake it for a bug. `UsesPeriod` hides the date controls for Skills, which is a state, not a movement.
- **Templates store a period *kind*, not two dates** — a template called "أجور الشهر" must mean the current
  month every time, not the month it was saved in.

### Database rules (audited — don't undo these)

- **Never call `.Date` on a date column inside a query.** `dp.Date.Date >= from.Date` translates to
  `date(...)` in SQL, which SQLite cannot answer from an index — so it scans the whole table and every
  `Date` index becomes pure write cost for zero read benefit. Every one of the 20 date predicates in
  `Repositories/` used to do exactly this. Compare the column directly (`dp.Date >= from.Date`); it is
  correct because **every write path normalises with `date.Date`**, so every `Date` column holds
  midnight. `DatabaseIntegrityTests` covers both halves: the range boundaries are inclusive, and every
  stored `Date` equals its own `.Date`. The one exception is `ActivityEvent.OccurredAt`, which stores a
  real time (`DateTime.Now`) — it uses a half-open range (`>= from.Date && < to.Date.AddDays(1)`).
- **Five CHECK constraints** guard the table itself: stars 1–5, stage quota > 0, production
  `PieceCount >= 0 AND PiecesPerWorkdayAtEntry > 0` (that column is the divisor behind every wage),
  adjustment amount > 0, daily wage >= 0. The services already enforce all of these; the constraints
  exist so a future code path, a bad migration, or an external tool can't put the data in a state the
  reports would silently mis-total. They were verified against the live DB (0 violations) before being
  added.
- **Every `decimal` needs an explicit `HasColumnType`.** EF's SQLite provider maps `decimal` to TEXT by
  default, and TEXT compares lexicographically — `"10.5" < "9.0"` is true. `WorkerSkill.MeasuredRatio`
  was stored that way (the other three decimals were configured); it is now `decimal(5,2)`. A test
  asserts the column type so a new decimal can't quietly land as text.
- **Deleted as dead, don't reintroduce**: `Attendance.CheckInTime` / `CheckOutTime` (the only write path
  set them to `null` explicitly; hourly work is tracked in `HourlyWorkLog`, which has a real end hour),
  and the `Notes` column on `Attendance` / `DailyProduction` / `Penalty` / `HourlyWorkLog` /
  `ProductionDayClosure` — no caller ever passed a value, so the optional `notes` service parameters went
  with them. Also `IX_ActivityEvents_EventType`: no query filters on it, and 11 distinct values would
  make it useless if one did. `Worker.EmployeeCode` went too — see below for what had to change first.
  Contrast `Worker.SkillsNotes`, which looks equally dead and is load-bearing for the seeder.
- **`Worker.EmployeeCode` is gone from the database.** It survived earlier rounds only because
  `DatabaseSeeder.SeedWorkerSkillLinksAsync` joined `WorkerSkillsSeed` (keyed `"W001"`…) to workers by
  code — drop the column and a fresh install comes up with products, workers and stages but **zero skill
  links**, so nobody is qualified for any stage and daily entry is silently unusable. The codes are now
  seed-internal identifiers only: `RealDataSeed.BuildRoster()` pairs each code with its worker in one
  list, `NameByCode()` derives the translation from that same list (so the two can't drift), and the
  seeder joins **by name**. Names are safe as the key because all 46 seeded names are unique — a test
  asserts it — and a name that appears twice in the DB is **skipped rather than guessed**, since a skill
  attached to the wrong person is worse than a missing one the user can add from the screen.
  `FreshInstallSeedTests` builds a real database from scratch and asserts links exist and land on the
  right worker; that is the test that would catch this whole class of breakage.

- **Deleting removes the row; soft delete is the exception, not the rule.** `DeletionScopeService` answers
  one question — is any wage history pointing at this row? — and `SoftDeleteService.DeleteAsync` takes a
  `removePermanently` callback from the caller that knows. A worker/product/stage with **no** production,
  hourly log, penalty or adjustment is deleted outright (dependent skills/attendance cascade); one with
  history is only flagged, because payroll sheets and old reports read its name and erasing it turns them
  into numbers with no owners — and the `Restrict` FK on production would refuse the delete anyway with a
  database error no user could act on. **`DailyProduction` is always removed**: nothing has a foreign key
  to it, so a flagged row was pure accumulation that every query had to filter past. The audit trail is
  the activity-log event (who/when/why/how many pieces) — that is the trace worth keeping, and it lives
  in its own table under a retention policy instead of as dead rows in working tables.
  Two traps this design has to respect, both covered by `DeletionScopeTests`:
  the existence checks **must** use `IgnoreQueryFilters()`, since a flagged production row still holds its
  foreign key and still blocks its worker; and `SoftDeleteResult.WasPermanent` tells the screen which of
  the two actually happened, so `IsActive = false` is only applied to a row that still exists.
- `DeletedRowsCleaner` (Data) applies that same rule once per startup to rows flagged before the rule
  existed, right after the backup and next to the activity-log purge. It computes what is busy **before**
  deleting anything, so there is no chain reaction — purging a flagged production row does not make its
  worker look free in the same pass.

### Domain model relationships

- `Product` 1—* `ProductionStage` (cascade delete): each stage carries its own `PiecesPerWorkday`
  ("اليومية" — the Arabic term shown in every UI surface; "كوتة" was retired) — the same stage name can
  repeat across products with an independent quota/price each.
  `Product.ImageData` (nullable BLOB) holds an optional product photo **inside the DB on purpose** — the
  backup only copies the `.db` file, so images kept as loose files would be lost on restore or when
  moving to another machine. Always write it through `ProductManagementService.SetProductImageAsync`
  (kept separate from `UpdateProductAsync` so renaming a product neither resends nor accidentally clears
  the photo), and always prepare the bytes with `StoredImageHelper.LoadForStorage` (UI layer), which
  downscales to 256px and re-encodes as JPEG using WPF's own imaging — no new package, and the stored
  blob stays tens of KB instead of megabytes multiplied across every daily backup. In the UI the photo
  occupies the **same 44×44 slot as the initials circle**, so products without one cost no extra space.
  `Product.ProductCode` **was deleted outright** (column and all) in `AddWorkerPhotoDropProductCode`:
  nothing read it — no report, no export, no calculation; it went form → service → displayed as "—".
  `Worker.EmployeeCode` followed it in `DropEmployeeCode` once the seeder stopped needing it.
  `Worker.PhotoData` mirrors `Product.ImageData` exactly (same reason, same helper) and is written only
  through `WorkerManagementService.SetWorkerPhotoAsync`, kept out of `UpdateWorkerAsync` for the same
  reason the product photo is kept out of `UpdateProductAsync`.
- `Worker.SkillsNotes` is **write-nobody, read-somebody**. Its input was removed from the add/edit form
  (replaced by the per-stage star ratings, surfaced as a rating badge on each product card), so
  `CreateWorkerAsync`/`UpdateWorkerAsync` no longer take or touch it: leaving the parameter in place while
  the form stopped supplying it would have made the first edit of any worker silently null the column.
  The column stays because
  `DatabaseSeeder.SeedHourlyRolesAsync` parses it every startup to classify رص/جودة/تدريب workers, and
  `WorkerRow.SkillsSearchText` still searches it. `RemovedFieldsTests` guards both halves.
- `Worker` *—* `ProductionStage` via `WorkerSkill` (join entity, unique per worker+stage): which stages a
  worker is qualified to perform.
- `DailyProduction`: one entry = pieces produced by one worker on one stage on one date. Snapshots
  `PiecesPerWorkdayAtEntry` from the stage at insert time (not read live) so historical records stay
  correct even if a stage's quota changes later. `WorkdaysCompleted` is a `[NotMapped]` computed property
  (`PieceCount / PiecesPerWorkdayAtEntry`). Delete of `Worker`/`ProductionStage` is `Restrict` here to
  protect historical records.
- `Attendance`: one row per worker per date (unique index), independent of `DailyProduction` — a worker can
  be present with no production logged, but absence implies no production. Cascade-deletes with `Worker`.
- `Penalty`: a disciplinary penalty on a worker on a date (reason + `PenaltyDeduction` enum: HalfDay=0.5,
  OneDay=1, ThreeDays=3, OneWeek=6 workdays — a work week is 6 days since Friday is off). Independent of
  attendance (can be issued while present). Cascade-deletes with `Worker`. Deleting a wrongly-entered
  penalty is a hard delete (no soft-delete value).
- `WageAdjustment`: a money movement in EGP on a worker on a date — `WageAdjustmentType` enum: Advance
  (سلفة, deducted) vs Bonus (حافز, added). `AmountEgp` is always positive; the type sets direction
  (`SignedAmountEgp` computed). Unlike penalties (which deduct workdays), these are direct EGP amounts on
  the wage. Independent of production/attendance/penalties. Cascade-deletes with `Worker`; hard delete for
  corrections. Date-leading index like the other by-date tables.
- Soft-delete convention: `Worker.IsActive` / `Product.IsActive` / `ProductionStage.IsActive` flags are used
  instead of hard deletes, to preserve historical production/attendance records.
- `Worker.EmployeeCode` **no longer exists** (`DropEmployeeCode`). It had already been removed from every
  UI surface — workers grid + profile + add/edit dialog, both report grids, attendance cards, payslip and
  all four Excel sheets — because it added nothing for the user; searching is by name only. The column
  itself lasted longer only because the seeder joined on it; see the Database rules section for how the
  join moved to names. Note that removing the Excel "الكود" column back then shifted every later column
  index in `WeeklyReportExcelService` — check the whole sheet if you touch those layouts.
- Two worker pay types: piece-rate (default) vs hourly. `Worker.HourlyRole` (nullable `HourlyRole` enum:
  Training/Racking/Quality/Other) — non-null means the worker is paid by hours, not pieces. Hourly workers
  have no `WorkerSkill` links, don't appear in production flow, and log via `HourlyWorkLog` instead.
- `HourlyWorkLog`: one row per hourly worker per date (unique index). Stores `EndHour24` (24h clock, shift
  starts fixed 8am) and a snapshot `WorkdaysCredited`. Cascade-deletes with `Worker`.
- `Worker.DailyWageEgp` (decimal, default 0): pay per workday in EGP. Wage = NetWorkdays × DailyWageEgp.
  NOT a snapshot — the current price applies to all periods (changing it re-computes all past wages).
  Applies to both piece-rate and hourly workers.
- `AppUser`: login accounts (unique username + PBKDF2-SHA256 hash/salt, never plaintext — all hashing in
  `AuthService`). Startup flow in `App.OnStartup`: migrate/seed → `EnsureDefaultUserAsync` (admin/admin on
  first run) → `LoginWindow.ShowDialog()` (with `ShutdownMode` juggling) → MainWindow only on success.
- Seeding (`DatabaseSeeder`): first-run seeds products/workers (`RealDataSeed`) + skill links
  (`WorkerSkillsSeed`, idempotent). `SeedHourlyRolesAsync` runs every startup (idempotent) — sets
  `HourlyRole` on descriptive workers (رص/جودة/تدريب) that have notes but no skills and no role yet.

### Business logic notes

- **Daily output is DERIVED, never stored** (`DailyProductionReportService` — the ONLY place these
  numbers are computed). There is no entity tracking pieces as they walk the line. Each report reads
  **one day's** production rows (`GetStageTotalsOnAsync`, a `GROUP BY` in SQLite) and asks two
  questions per product, both against `ActiveLine(product)` — active stages ordered by
  `SortOrder` then `Id`:
  - **Completed today** = production recorded on the **last** stage of the line that day.
  - **Started today** = production recorded on the **first** stage of the line that day.
  - A product with neither is dropped from the report (`HasActivity`), so idle products don't pad it.
  - **This replaced a `ProductionBatch` entity** (batch/split/carry-over/opening-balance, removed in
    `RemoveProductionBatches`). That design asked the user "where did these pieces come from?" on every
    mid-line range, and answering it wrong (choosing "opening balance" while a lot was parked at that
    exact stage) minted pieces from nothing — 2000 stayed parked when only 1000 truly remained.
    **Do not reintroduce piece-level lot tracking** to answer "how many are done"; it falls out of the
    records already being entered. The deliberate trade-off: no per-lot traceability (when a specific
    lot started, how it moved).
  - **There is deliberately no WIP / "الواقف" / "مستني" number anywhere**, and no cumulative-history
    query behind one (`GetStageTotalsUpToAsync` was deleted with it). A cross-stage subtraction
    (`cumulative(i-1) − cumulative(i)`) did exist and was correct arithmetic, but the user removed it:
    it was shown on the entry screen, the reports screen and the closure dialog, and nobody ever took
    a decision from it. Reintroducing it means reintroducing per-stage queue badges, over-count
    warnings, and a third summary number the screens have to carry. Don't.
- **Ranges no longer carry any lot identity** (`FlowRangeDto`): from-stage, to-stage, piece count.
  Ranges still may not overlap (a stage in two ranges is double-entry) and each covered stage still
  needs worker shares summing exactly to its pieces. A range may start anywhere in the line — starting
  mid-line needs no justification, because the pieces it consumes are implied by the arithmetic.
- **Day closure** (`DayClosureService`): `PreviewAsync` shows completed + started per product,
  `CloseAsync` writes a `ProductionDayClosure` row and `RecordFlowAsync` then refuses that date.
  Nothing is "carried forward" — every day is read from its own rows, so work that wasn't finished
  is simply recorded on the day it does get done. The stored `CompletedPieces`/`StartedPieces` are a
  **snapshot the user approved**, not a cache to recompute. `ReopenAsync` undoes it (data-entry
  mistakes are normal).
- `WorkdayCalculationService.Update/DeleteProductionAsync` edit rows freely. They used to refuse rows
  belonging to a batch because quantity and line position could desync; with numbers derived from the
  rows themselves, correcting a row corrects every report that depends on it.
- `DailyProduction` rows are created only by `WorkdayCalculationService.RecordProductionAsync` or
  `ProductionFlowService.RecordFlowAsync` — both snapshot the stage quota automatically. Every row
  counts for both wages and the daily output report; there is no second class of row.
- **Product activity** (`ProductActivityService` — the ONLY place "is this product working?" is
  decided): a product counts as working in a period iff it has logged production in it, never because
  `Product.IsActive` is set. Its `CurrentWeek` delegates to `WeeklySummaryService.GetWorkWeekRange`, so
  the products screen and the payroll sheet can never disagree about which week "this week" is. It
  returns a row for **every** product including zero-production ones, so the screen can filter and rank;
  callers that need "worked" must check `WorkedInPeriod`.
- **Worker filtering** (`WorkerFilterRules` in Business — pure, no DB, no UI): the composable filters on
  the workers screen. Scope (production / hourly / inactive) is mutually exclusive because those sets
  are disjoint; every other criterion ANDs on top. `null` criterion = filter off. A worker with no
  skills (`AverageStars <= 0`) is excluded from any stars filter rather than counted as zero stars, and
  a worker with no attendance record matches no attendance status — "unrecorded" is its own state.
- **Worker-assignment rule** (`WorkerAssignmentGuard` in Business — the ONLY place this rule exists;
  never re-implement it in a controller/ViewModel). An "assignment" is a `DailyProduction` row, so
  "assigned" = has a row for that worker/stage/date. By default a worker holds one assignment per
  production day (there is no shift concept, so the day IS the scope). `Evaluate(existing, requested)`
  is pure and testable; it processes `requested` in order and each item is compared against the saved
  rows **plus the earlier items in the same request**, so a worker put on two stages of one flow is
  caught too. Outcomes: exact duplicate (same worker+stage+date) is **always blocked** with an "already
  assigned" message and is NOT an override case; a different stage/product on the same day needs
  explicit confirmation; anything else passes. `EnsureAllowed(result, confirmOverride)` is the single
  place that turns a result into "continue or stop" and throws `AssignmentConfirmationRequiredException`
  (carries structured conflicts, derives from `InvalidOperationException` so existing catch blocks
  still work). Both creation services take `confirmOverride = false` as an optional last parameter —
  the flag applies only to the call that carries it and never overrides the duplicate block.
  The check + the insert run inside one `IUnitOfWork.BeginWriteTransactionAsync()`, which opens
  `BEGIN IMMEDIATE` (`EfUnitOfWork`) so the read the decision rests on is under the same write lock as
  the insert — two instances can't both pass the check and write. There is deliberately **no** unique
  index on `(WorkerId, ProductionStageId, Date)`: existing customer DBs may hold legitimate duplicates
  from two separate saves, so the migration would fail and the rule would change behaviour retroactively.
  UI side (`FlowSessionViewModel`): adding a worker validates **before** the chip is rendered (nothing
  to roll back on Cancel), comparing against saved rows + every open flow session's unsaved chips; the
  save path is two-phase (attempt → `AssignmentConfirmationRequiredException` → dialog → re-send with
  `confirmOverride: true`). `_confirmedAssignments` only prevents asking twice about the same pair the
  user just confirmed — it is cleared on reload/date change and is not a "remember my choice".
- `ProductionFlowService.GetLastFlowAsync(productId, before, lookbackDays = 60)` powers the "كرّر يوم فات"
  button: it finds the most recent day **before** the given date that had production on that product and
  returns the (stage, worker) pairs. It deliberately returns **no piece counts** — piece counts change
  daily and copying yesterday's numbers risks saving a stale figure unnoticed; the user re-types them.
  The UI re-checks each returned worker is still qualified and still active before placing them, reports
  how many were skipped, and confirms before wiping the current on-screen distribution. Assignments
  placed this way still go through `WorkerAssignmentGuard` at save time — the button bypasses only the
  fast add-time check, never the authoritative one.
- `ProductionFlowService.RecordFlowAsync` is the main production-entry path: takes stage ranges
  ("from stage X to Y produced N pieces" — every stage in a range gets N) + per-stage worker shares.
  Validates everything (ranges in line order, no overlaps, share sums == stage pieces, workers must be
  qualified via `WorkerSkill`), writes all records in one SaveChanges (all-or-nothing), and auto-creates
  a Present attendance record for participating workers who have none that day (never overwrites).
- `PerformanceEvaluationService.EvaluateDayAsync` ranks workers for a given date against the average
  workdays of only the workers who actually produced that day (absentees aren't averaged in at zero).
  Unexcused absence (`AbsentWithoutPermission`) always ranks worst regardless of any production; excused
  absence is neutral (`Average`). Thresholds (`TopPerformerThreshold`, `AboveAverageThreshold`,
  `BelowAverageThreshold`) are relative percentages vs. team average, defined as constants in that service.
- `PayrollService.GetPeriodPayrollAsync(from, to)`: custom-period (e.g. monthly) wage sheet. Aggregates
  ALL days in the range directly (not whole weeks): produced + hourly workdays − absence/penalty
  deductions, × current wage = workdays-wage, then **+ bonuses − advances (EGP)** = net wage. Surfaced in
  ReportsView's "كشف الأجور" tab (date range + Excel export via `WeeklyReportExcelService.ExportPeriodPayroll`,
  with حافز/سلفة columns). Weekly wage also shows in the weekly sheet (`NetWageEgp` column + totals row in
  Excel) and per-week in the worker profile. (NOTE: weekly sheet wage does NOT include EGP adjustments —
  advances/bonuses only flow through the period payroll + worker report + payslip.)
- `WageAdjustmentService.RecordAdjustmentAsync/RemoveAdjustmentAsync`: add/hard-delete an advance (سلفة) or
  bonus (حافز) in EGP for a worker on a date. Surfaced in DailyEntryView's "السلف والحوافز" tab (same
  attendance-row worker picker as penalties; سلفة shown red, حافز green). Both `PayrollService` and
  `ProductionReportService.GetWorkerReportAsync` fold these into net wage; the worker report shows the full
  breakdown line (أجر اليوميات + حوافز − سلف = الأجر النهائي) and a printable payslip (see ReportsView).
- `HourlyWorkdayService`: hourly wage ladder. Shift 8am→4pm. `ComputeWorkdays(endHour24)` (pure/static):
  finished by 4pm → pro-rata `(endHour-8)/8` (max 1.0); finished 4pm–8pm → 1.5; finished 8pm–midnight →
  2.0. NON-cumulative (last period reached wins). `RecordHourlyWorkAsync` upserts + snapshots + auto-marks
  Present. `WeeklySummaryService` sums `HourlyWorkLog.WorkdaysCredited` into `ProducedWorkdays` so hourly
  days flow into net workdays / weekly sheet / pay exactly like piece production.
- `AttendanceService.RecordAttendanceBatchAsync` is the **only** attendance write path — an upsert
  (one record per worker/date) for the whole grid in a single save. Recording an absence for a worker
  who has **work logged** that day is REJECTED, and the batch is all-or-nothing: it names every
  conflicting worker and writes nothing. Delete the work first if truly absent. "Has work logged"
  comes from `AttendanceAutomationService.GetWorkersWithLoggedWorkAsync` — production rows OR hourly
  logs, so hourly workers are covered too (they have no stage production by design).
- **Attendance automation** (`AttendanceAutomationService` — the only place these rules exist):
  - *Auto-Present*: on load, a worker with logged work is pre-selected as Present and the row shows why
    (`WorkNote`). A saved status always wins over the auto value.
  - *Auto absence penalty*: `AbsentWithoutPermission` ⇒ exactly one `HalfDay` penalty tagged
    `PenaltySource.AutoAbsence`. Changing the status away removes it. Applies to piece-rate **and**
    hourly workers. `ReconcileAbsencePenaltiesAsync` is idempotent and only touches penalties it
    created — `PenaltySource.Manual` (value 0, so every pre-existing row) is never modified or deleted,
    and `PenaltyService.RemovePenaltyAsync` refuses to hand-delete an auto penalty.
  - Attendance + penalty reconcile run in one `IUnitOfWork` transaction (`BEGIN IMMEDIATE`), so two
    instances saving the same day can't produce two penalties for one absence.
- **No double deduction** (`AbsenceDeductionRule` — shared by `WeeklySummaryService` and
  `PayrollService`): an unexcused absence day costs **0.5 workday, once**. Before this feature the 0.5
  was a hidden subtraction; now it is a visible auto penalty. `ComputeUnpenalizedAbsenceDeduction`
  counts only absence days that have **no** auto penalty, so new days are charged through the penalty
  and legacy days keep their built-in deduction — same total either way, and no data migration was
  needed to backfill historical rows.
- Daily evaluation: a sole producer gets `TopPerformer` iff `TotalWorkdays >= 1.0` (objective bar —
  percent-vs-average is meaningless with no peers), else `Average`.
- UI hygiene: never use `_ = SomeAsync()` — use `SafeAsync.Run(...)` (ViewModels) so failures surface
  instead of vanishing (Dispatcher handler doesn't see unobserved task exceptions). App enforces a
  single instance via a named Mutex in `App.OnStartup`. Date-leading indexes exist on
  DailyProductions/Attendances/Penalties for all by-date/by-week queries.
- Corrections: `WorkdayCalculationService.UpdateProductionAsync/DeleteProductionAsync` fix wrongly-saved
  records (update keeps the quota snapshot; delete is hard, like penalties) — surfaced in DailyEntryView's
  "سجلات اليوم" tab.
- Backups: `DatabaseBackupService` — daily-on-startup (local `Backups/` + optional external folder from
  `AppSettingsStore`/settings.json, external failures never block startup), `BackupNow` (manual, errors
  loudly), `RestoreBackup` (safety-copies current db first, then overwrite + app restart). Cleanup is
  filename-date based; `AppPaths` centralizes all file locations. UI in `SettingsView` (5th nav item).
- **Activity-log retention** (`ActivityLogService.PurgeExpiredAsync`, run once per startup from
  `App.OnStartup` **after** the backup, so anything it deletes is still in today's backup; its failure is
  swallowed — a cleanup is not a startup prerequisite). **Two windows, not one**, because this log has no
  routine noise: every one of its 11 event types is either a deletion or a money movement.
  `ActivityEventRetention` (Core) lists only the **short-lived** types — the five administrative deletions
  (day / record / worker / product / stage) — and everything else gets the long window **by default**, so
  a new event type added later can't silently inherit a 90-day life just because someone forgot to list
  it. `ActivityLogRetentionTests` asserts exactly that inversion. Defaults: 90 days for deletions, 365 for
  money + `OperationsPasswordChanged` (it's the gate protecting the money operations, so "who changed it"
  belongs to the same question). Both are editable in Settings; **0 means off, never "delete everything"**,
  and anything else is raised to `MinRetentionDays` (30). Deleting is a bulk `ExecuteDeleteAsync` on the
  indexed `OccurredAt` — the rows never load into memory. `ActivityLogViewModel.RetentionNote` prints the
  live policy on the log screen: a log that shrinks on its own must say so, or the first person who can't
  find a six-month-old event reports it as a bug.
- `WeeklySummaryService` is the heart of weekly math. The work week runs **Thursday → Wednesday**
  (`GetWorkWeekRange`). Weekly counters are computed on the fly from `DailyProduction`/`Attendance`/
  `Penalty` records — nothing weekly is stored, so "a new week starts fresh" while all history stays
  queryable. Net workdays = produced − unexcused-absence deduction (**0.5 workday per
  `AbsentWithoutPermission` day**; excused absence costs nothing) − penalty deductions. Best worker of
  the week = highest net, only if they produced and net > 0.

## Environment note

.NET 8 SDK was installed via winget but may not be in PATH for fresh shells; if `dotnet` isn't found in
PowerShell, prepend `$env:PATH = [System.Environment]::GetEnvironmentVariable("PATH","Machine") + ";" +
[System.Environment]::GetEnvironmentVariable("PATH","User")`.
