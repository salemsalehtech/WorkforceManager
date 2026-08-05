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

`WorkforceManager.Tests` (xUnit, `net8.0`) covers the worker-assignment rule — run with `dotnet test`
from the `WorkforceManager/` folder. It spins up a real SQLite file DB per test (`TestDatabase`), not the
EF InMemory provider, because the concurrency tests need SQLite's actual write lock. `TestDatabase` mirrors
the DI registrations from `App.xaml.cs`, so a service added there but not here fails the tests on purpose.

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
  bar (active / hourly / inactive / best-of-week + a "needs attention" button that filters to problem
  workers), instant search, `FilterChip` quick filters (الكل / بالقطعة / بالساعة / موقوفين), and a sort
  dropdown. The whole worker list is loaded once via `IWorkerRepository.GetAllWithSkillsAsync()` and
  search/filter/sort run **in memory** (`ApplyFilters`) — that is why search is per-keystroke with no
  DB round-trip; `WorkerRow.SkillsSearchText` pre-joins skills + notes so skill search stays a single
  string match. Each card flags `HasNoWage` (worker would earn 0 EGP on the payroll) and `HasNoSkills`
  (piece-rate worker with no skill links never appears in a production flow) — both surfaced together as
  `NeedsAttention`. The profile panel adds bulk skill assignment: `IsAddingSkills` opens a searchable,
  multi-select stage list (`VisibleStageOptions`, already-owned stages filtered out) and
  `AddSelectedSkillsCommand` assigns them all at once. `RefreshRowsKeepingSelectionAsync` reloads the
  list without losing the open profile. Also add/edit/soft-delete. `DailyEntryView` is implemented: one shared date +
  3 tabs — production-flow entry, topped by a day summary bar (pieces / workdays / workers / products,
  derived from the records `LoadDayRecordsAsync` already loads — no extra query). Each stage card carries
  a colour bar and label driven by `FlowStageRow.State` (`FlowStageState`: Ready / NeedsWorkers /
  Mismatch / WorkersWithoutPieces / NotToday) so an 11-stage product reads at a glance instead of card by
  card, and the session header shows `ReadinessText` ("7 من 11 مرحلة جاهزة"). `RefreshState` /
  `RefreshReadiness` are driven from `RecomputeTotals`, so anything touching pieces or shares repaints
  both. Every stage's worker picker has its own search box (`WorkerSearch` → `VisibleWorkers`) since a
  stage can have ~20 qualified workers. "كرّر يوم فات" is `RepeatLastDayAsync` (see `GetLastFlowAsync`).
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
  `ReportsView` is implemented: daily evaluation tab (colored ratings vs team
  average) + weekly sheet tab (net-workdays ranking, week navigation, Excel export via
  `WeeklyReportExcelService`/ClosedXML in Business) + products chart tab (weekly COMPLETED pieces per
  product = pieces on each product's last stage only, via `ProductionChartService`; bars are native WPF
  elements, no chart library; time axis forced LTR) + "تقرير الإنتاج" general-report tab (department
  summary + by product/stage + by worker) + "تقرير عامل" per-worker tab (production detail by stage and
  by day + attendance + wage/penalties + advances/bonuses breakdown line + a "🖨 قسيمة أجر" printable
  payslip via `PayslipWindow`/`PayslipData` — a preview window that prints the slip to any printer or
  Microsoft Print to PDF, no external library). Both report tabs share the same period model: quick buttons
  (اليوم/الأسبوع/الشهر) + a free from/to custom range (any span works, e.g. day 1→20), all served by
  `ProductionReportService.GetGeneralReportAsync(from,to)`/`GetWorkerReportAsync(workerId,from,to)`
  (completed pieces = last-stage-per-product, same rule as the chart) with Excel export via
  `WeeklyReportExcelService.ExportGeneralReport`/`ExportWorkerReport`. `ProductsView` is implemented with the same card language as the workers/attendance screens: summary bar
  (active products / total stages / total + a "needs attention" button), instant search (product or stage
  name), `FilterChip` filters (النشط / الكل / موقوف), and product cards showing stage count only — a
  `TotalQuota` stat (sum of every active stage's `PiecesPerWorkday`) was removed on purpose: summing
  quotas across sequential stages measures nothing, since a piece passes through the stages in order
  rather than in parallel, so the number just grew with stage count. Don't reintroduce it.
  The right panel renders the product as a **production line** — one card per stage with its
  position number, quota, and **how many workers are qualified for it**, plus ▲▼ buttons that reorder the
  line. Reordering goes through `ProductManagementService.MoveStageAsync(stageId, moveUp)`, which swaps
  with the neighbour and then **renumbers the whole line from 1** (healing gaps/duplicates left by older
  edits); it returns false at the ends instead of throwing. Order is not cosmetic — production ranges
  ("from stage X to Y") are resolved by line position, so `StageOrderTests` covers both the swap itself
  and the fact that a range which was valid before a move becomes invalid after it. Warnings surface the
  two states that silently block production: a product with no active stages, and an active stage with
  **zero qualified workers** (the flow screen only offers qualified workers, so such a stage can never be
  filled). Stage names stay unique per product, and quota edits only affect future entries thanks to the
  snapshot.
  All four sidebar screens are implemented. Navigation uses `Checked` (not `Click`) on the sidebar
  radios — handlers guard against the initial `Checked` that fires during `InitializeComponent` before
  `MainContent` exists. `App.xaml` holds the design system: brand brushes (BrandBrush/AccentBrush/
  Success/Danger/Warn + bg variants) and keyed styles (`Card`, `ToolbarCard`, `PrimaryButton`,
  `SuccessButton`, `DangerButton`, `GhostButton`, `IconButton`, `ModernDataGrid` + header/cell/row
  styles, `NavItem`) — style new UI from these resources, never inline colors; local DataGrid RowStyles
  must use `BasedOn="{StaticResource ModernGridRow}"`. ViewModels take `IServiceScopeFactory` and create a scope per operation
  (keeps DbContext short-lived). Gotcha: WPF implicit styles don't apply to derived types, so the
  `TargetType="Window"` style in App.xaml does NOT hit `MainWindow` — set `FlowDirection="RightToLeft"`
  explicitly on each window.

### Domain model relationships

- `Product` 1—* `ProductionStage` (cascade delete): each stage carries its own `PiecesPerWorkday`
  ("اليومية" — the Arabic term shown in every UI surface; "كوتة" was retired) — the same stage name can
  repeat across products with an independent quota/price each.
  `Product.ImageData` (nullable BLOB) holds an optional product photo **inside the DB on purpose** — the
  backup only copies the `.db` file, so images kept as loose files would be lost on restore or when
  moving to another machine. Always write it through `ProductManagementService.SetProductImageAsync`
  (kept separate from `UpdateProductAsync` so renaming a product neither resends nor accidentally clears
  the photo), and always prepare the bytes with `ProductImageHelper.LoadForStorage` (UI layer), which
  downscales to 256px and re-encodes as JPEG using WPF's own imaging — no new package, and the stored
  blob stays tens of KB instead of megabytes multiplied across every daily backup. In the UI the photo
  occupies the **same 44×44 slot as the initials circle**, so products without one cost no extra space.
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
- `Worker.EmployeeCode` is **invisible plumbing — never show it in any screen, export, or payslip**. It was
  removed from every UI surface (workers grid + profile + add/edit dialog, both report grids, attendance
  cards, payslip, and all four Excel sheets) because it added nothing for the user; searching is by name
  only. The column and its seed values (`W001`–`W046`) survive on purpose: `DatabaseSeeder`
  `.ToDictionaryAsync(w => w.EmployeeCode!)` matches `WorkerSkillsSeed` **by code, not by name**, so
  dropping it would leave a fresh install with zero skill links and therefore nobody qualified for any
  stage. For the same reason `WorkerManagementService.UpdateWorkerAsync` deliberately does **not** touch
  `EmployeeCode` (an edit that nulled it would silently break re-seeding for that worker), and
  `CreateWorkerAsync` leaves it null for new workers. Removing the Excel "الكود" column shifted every
  later column index in `WeeklyReportExcelService` — check the whole sheet if you touch those layouts.
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
