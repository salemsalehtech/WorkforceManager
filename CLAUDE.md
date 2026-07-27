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

There is no test project in the solution yet.

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
  implemented: workers grid with current-week stats, unified name/skill search, profile panel with skills
  management and weekly history, add/edit/soft-delete. `DailyEntryView` is implemented: one shared date +
  3 tabs — production-flow entry (one or MORE products per day: each product gets its own
  `FlowSessionViewModel` card — stages as ordered cards, qualified-only workers per stage with equal
  auto-split + manual override, stage ranges "from stage X to Y: N pieces", live per-worker workdays
  preview, independent save; "add product" button appends sessions; row-level commands live on the row
  view-models via callbacks, not RelativeSource), a "سجلات اليوم" correction tab (edit/delete saved
  production records), an "العمال بالساعة" tab (hourly workers pick an end-hour → live workdays preview
  via the ladder → save + auto-attendance), attendance grid (upsert per worker/date),
  penalties (add with reason/deduction, list + delete for the day), and an "السلف والحوافز" tab
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
  `WeeklyReportExcelService.ExportGeneralReport`/`ExportWorkerReport`. `ProductsView` is implemented: products list with
  search/inactive filter, stages panel per product with quota management (`ProductManagementService` —
  stage names unique per product, quota edits only affect future entries thanks to the snapshot).
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

- `Product` 1—* `ProductionStage` (cascade delete): each stage carries its own `PiecesPerWorkday` ("كوتة
  اليومية") — the same stage name can repeat across products with an independent quota/price each.
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

- `DailyProduction` rows are created only by `WorkdayCalculationService` (single/batch) or
  `ProductionFlowService.RecordFlowAsync` — both snapshot the stage quota automatically.
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
- `AttendanceService.RecordAttendanceAsync` is an upsert (one record per worker/date). Recording an
  absence for a worker who has production that day is REJECTED (single and batch — batch is
  all-or-nothing, names the conflicting workers). Delete the production first if truly absent.
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
