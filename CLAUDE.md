# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

WorkforceManager (نظام إدارة إنتاجية وأجور العمال) is a WPF desktop app for managing factory workers, their
skills, products and their manufacturing stages, daily piece-production entry (with automatic "workday"
calculation), attendance, and performance evaluation vs. team average. All in-code comments and docs are
written in Arabic — follow that convention when editing existing files.

**`WorkforceManager.sln` and the six projects sit at the repo root**, next to `publish.ps1`, `clean.ps1`,
`installer/` and `publish-assets/`. They used to live one level down in a `WorkforceManager/` folder; that
extra level bought nothing (the repo holds exactly one solution) and hid everything behind a click on
GitHub — including the README, which GitHub only renders on the landing page when it is at the root.
`publish.ps1`/`clean.ps1` keep a `$sln` variable that is now just `$PSScriptRoot`, so the rest of both
scripts stayed untouched.

## Commands

Run from the repo root (where the `.sln` lives):

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

Also from the repo root (same folder — the packaging scripts live next to the `.sln`):

```powershell
# build the distributable — wipes dist/ first, so only ONE copy ever exists
.\publish.ps1                      # self-contained folder + zip (~172 MB / ~70 MB)
.\publish.ps1 -Mode SingleFile     # one compressed .exe (~70 MB)
.\publish.ps1 -Mode Light          # framework-dependent (~15 MB, needs .NET 8 Desktop Runtime installed)

# the customer deliverable — one Setup .exe, Next-Next, prerequisites bundled
.\publish.ps1 -Mode Installer -SeedDatabase "<path to a backup .db>"

# nuke all build artifacts (bin/obj/dist) — everything here is regenerable
.\clean.ps1
.\clean.ps1 -KeepDist
```

Both scripts must stay **UTF-8 with BOM** — Windows PowerShell 5.1 reads `.ps1` as ANSI without a BOM
and the Arabic strings break the parser. **`installer/WorkforceManager.iss` needs a BOM for the same
reason** — Inno Setup reads a `.iss` as ANSI unless one is present, and every Arabic wizard string turns
to mojibake.

`publish-assets/` holds the files copied into every release (`اقرأني.txt`, `portable.marker`) — they live
in the repo, not only inside a zip. Releases ship **without** a `Data/` folder; the app creates and seeds
its own on first run.

**`Installer` mode is the one that omits `portable.marker`, and that omission is the whole point.** With
the marker present `AppPaths` puts the database inside the program folder — which under an installer is
`C:\Program Files\…`: read-only for a standard user, and wiped and rewritten by every upgrade. Without it
the data lives in `%ProgramData%\WorkforceManager` and survives upgrades untouched. Anything that puts the
marker back into an installer build silently destroys customer data on the first update.

The `.iss` encodes three rules that exist only to protect that data, all called out in its header comment:
a permanently fixed `AppId` (this is what makes the next Setup an *upgrade* rather than a second parallel
install), the seed database installed to `{commonappdata}` rather than `{app}`, and
`onlyifdoesntexist uninsneveruninstall` on that file — the first stops an upgrade overwriting the
customer's work, the second stops uninstall deleting it, since Inno otherwise removes every file it
installed. `CloseApplications` + `AppMutex` handle upgrading while the app is open; without them the
upgrade fails halfway and leaves a half-written program folder.

`-SeedDatabase` must be given a file produced by the app's own **"نسخة احتياطية الآن"** button, not a
hand-copied `.db`. That button goes through `VACUUM INTO`, so it captures the WAL; copying the `.db` while
the app is open yields a backup with no tables in it (see the backup rules below — this was a real defect).

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

`WorkforceManager.Tests` (xUnit, `net8.0`, 670+ tests) covers the worker-assignment rule, daily output,
the skill-rating system, worker filtering, product activity, pending work, the worker report,
activity-log retention + **which operations write to it**, database integrity, deletion scope, the report
builder, the production chart (day/week/month + scrap), payslip strips, **backup integrity and calendar
safety**, fresh-install seeding, **the installed-mode data path and the one-time legacy migration**,
**daily operations sign-off** (the unsigned-past-dates gap calculation, the automatic cutover seed, and
which `SensitiveAction`s still gate immediately), and the removed-field guards — run with `dotnet test`
from the repo root. It spins up a real SQLite file DB per test (`TestDatabase`), not the
EF InMemory provider, because the concurrency tests need SQLite's actual write lock. `TestDatabase` mirrors
the DI registrations from `App.xaml.cs`, so a service added there but not here fails the tests on purpose.

`WorkforceManager.UiTests` (xUnit, `net8.0-windows`, `UseWPF`) is a **separate** project for tests that need
real WPF: `XamlLoadTests` loads **every** compiled XAML file for real. It exists because a whole class of XAML errors
is invisible to both the compiler and every other test, and only shows up when the screen opens on the
user's machine — a bad `PackIconKind` name, a missing `StaticResource` key, a duplicate `x:Name`, a
`TargetName` outside its namescope, or **`BasedOn="{DynamicResource ...}"`** (`BasedOn` is a plain CLR
property, not a DependencyProperty, so `DynamicResource` on it throws at load). That last one shipped once
and made the app refuse to open at all, because `MainWindow`'s constructor builds `WorkersView` — a load
error in the default screen kills the whole window. The test enumerates the assembly's **BAML resource
table**, not file paths, so a new `.xaml` is covered without anyone remembering to add it; screens are
constructed with `null` for their DI arguments (every view calls `InitializeComponent()` first, so the XAML
still loads). **Every WPF test runs on the one STA thread owned by `WpfThread`**, which builds the single
`Application` and keeps a `Dispatcher` running on it. Both halves matter: WPF allows only one `Application`
per process, and resources that aren't frozen (any brush holding a `DynamicResource`) belong to the thread
that created them — a second STA thread building a window throws "The calling thread cannot access this
object". The live `Dispatcher` is what lets `ShowDialog` run its nested message loop, so a test can show a
dialog, click a button and read the result. Test classes touching WPF share the `"WPF"` xUnit collection so
they never overlap. Note that a **programmatic click must go through `ButtonAutomationPeer`**, not
`RaiseEvent(ClickEvent)`: `IsCancel`/`IsDefault` are handled inside `Button.OnClick`, which `RaiseEvent`
skips, so a cancel button tested that way silently never sets `DialogResult`.
Two failure shapes are deliberately ignored: anything that is **not** a `XamlParseException` (the XAML
loaded; the constructor just wanted a real ViewModel) and "Cannot locate resource" (`Application.ResourceAssembly`
is pinned to the test host, so window icons by relative URI can't resolve there).

The SQLite DB lives outside the repo, outside the program folder, at
`%ProgramData%\WorkforceManager\workforce.db` (or in `Data\` next to the exe when a `portable.marker`
file is present — see `AppPaths`). It used to live in `%LocalAppData%` before the installer existed;
`AppPaths.MigrateLegacyData` copies (not moves — the old copy stays as a fallback) a database found there
into the new shared folder exactly once, the first time `DataFolder` is resolved on a machine that has
never had one there. `App.OnStartup` creates/updates the DB with `Database.MigrateAsync()` +
`DatabaseSeeder.SeedIfEmptyAsync`, so migrations DO run at startup and schema changes reach an existing
customer DB without wiping data. Before any of that, `App.EnsureDataFolderWritable()` writes and deletes a
probe file in the resolved folder and shuts down with an Arabic message naming the folder if it can't —
the alternative is SQLite's "unable to open database file", which explains nothing to the customer.

## Architecture

Simplified Clean Architecture across 4 projects, each with its own `.csproj`, referenced in one direction only:

```text
Core  <-  Data  <-  Business  <-  UI
Core  <-------------Business
Core  <----------------------- UI
```

- **WorkforceManager.Core** — POCO models (`Models/`), enums (`Enums/`), and repository interfaces
  (`Interfaces/`). Zero dependency on EF Core or WPF — this is what would let SQLite be swapped for
  SQL Server later without touching models or business logic. **This claim briefly stopped being true and
  nothing caught it for a while**: 13 models had grown a `[Index(...)]` data-annotation directly on the
  class (a real, working index — just declared a different way than the older ones), which needs the
  `Microsoft.EntityFrameworkCore.IndexAttribute` type, so `Core.csproj` had picked up a `PackageReference`
  to the *full* `Microsoft.EntityFrameworkCore` package "just for the attribute." Two problems compounded:
  the zero-dependency claim above was flatly false, and index declaration now lived in two disagreeing
  places (fluent `HasIndex` in `AppDbContext.OnModelCreating` for the older tables, `[Index]` attributes
  on the model for the newer ones) — the same "one rule, two places" shape this file warns against
  elsewhere. Fixed by moving all 19 attribute-based indexes into `OnModelCreating` fluent calls next to
  the rest, and dropping the package reference entirely. **Verified with zero behavioural risk**:
  generated a migration right after the move and confirmed it came back with empty `Up`/`Down` methods —
  proof the fluent declarations produce byte-identical schema to what the attributes produced, not just
  "looks equivalent." If a model ever needs an index again, add it in `AppDbContext`, not on the class.
- **WorkforceManager.Data** — EF Core + SQLite. `AppDbContext` is the single point of contact with the
  database (all relationships/cascade rules configured in `OnModelCreating`); `Repositories/` implement
  the Core interfaces; nothing outside this project talks to `AppDbContext` directly.
- **WorkforceManager.Business** — all business rules live here, nowhere else (especially not in UI code):
  `WorkdayCalculationService`, `AttendanceService`, `ProductionFlowService`,
  `WeeklySummaryService`, `PenaltyService`, `WorkerManagementService`, `ProductManagementService`,
  `ProductionReportService`, `WageAdjustmentService`, `AuthService`, `PayslipStripExcelService`, plus
  their DTOs in `DTOs/`.
- **WorkforceManager.UI** — WPF, MVVM (CommunityToolkit.Mvvm) + MaterialDesignThemes. `App.xaml.cs` wires
  up DI via `Microsoft.Extensions.Hosting`'s `Host` (`AppHost`) — this is the single place new
  repositories/services/views get registered.
  **Every `[RelayCommand]` on an async method that actually writes to the database
  (Save/Delete/Add/Edit/Toggle/Withdraw/Set/Reactivate...) takes `(AllowConcurrentExecutions = false)`.**
  No screen anywhere used to give any feedback or protection while such a command was running — a fast
  double-click could fire the same save/delete twice. CommunityToolkit.Mvvm's generated
  `IAsyncRelayCommand` already tracks `IsRunning` and raises `CanExecuteChanged` when this flag is set,
  so a `Button` bound via plain `Command="{Binding XCommand}"` disables itself automatically while running
  — **zero XAML changes needed**, and the existing `IsEnabled="False"` triggers in `Core.xaml`'s button
  styles already grey it out, which doubles as the "this is busy" cue. Commands that only open a dialog,
  export a file, or navigate (`Show*`, `Open*`, `Export*`, `Prev/NextPeriod`) are deliberately left alone
  — no duplicate-write risk, and a modal dialog already blocks re-clicking anyway.
  **`DailyEntryViewModel.EntryDate` has quick prev/next/today commands** (`PreviousDay`/`NextDay`/
  `GoToToday`, next to the `DatePicker` in `DailyEntryView.xaml`) — trivial wrappers that just reassign
  `EntryDate`, reusing the existing `OnEntryDateChanged` → `ReloadForDateAsync` pipeline; no new loading
  logic.
  **`GlobalSearchDialog`** (`Views/GlobalSearchDialog.xaml`, opened from a "بحث سريع" button always
  visible at the top of the sidebar in `MainWindow.xaml`) is a lightweight quick-search over active
  workers + active products, same non-MVVM dialog pattern as `MemoryPostponeDialog`. It does not carry
  its own database logic: `MainWindow.GlobalSearch_Click` loads the two lists up front with the exact
  same repository calls `WorkersViewModel`/`MemoryViewModel.LoadAsync` already use
  (`IWorkerRepository.GetActiveWithSkillsAsync`, `IProductRepository.GetActiveWithStagesAsync`), and on a
  pick, **checks the corresponding nav `RadioButton` first** (`NavWorkersItem`/`NavProductsItem`, letting
  the existing `NavWorkers_Checked`/`NavProducts_Checked` handlers resolve the (Transient) view+ViewModel
  the normal way) **before** reading `MainContent.Content`'s `DataContext` and setting its `SearchText` —
  resolving the view a second time here directly would create a throwaway duplicate instance instead of
  the one actually on screen. `WorkersView`/`ProductsView` reuse the DTO the search dialog created and end
  up filtered to the picked name on arrival for free, off their own existing `SearchText` filtering.
  **"إيه الجديد؟" spotlight tour** (`Tour/AppTourStep.cs`, `Tour/AppTourContent.cs`,
  `MainWindow.RunTourAsync`/`PositionTourStep`): a real coach-mark tour, not a changelog dialog — each
  step navigates to the right screen (reusing the same `NavXItem.IsChecked = true` pattern as the global
  search above) and darkens everything except a rounded-rect cutout around the target element
  (`CombinedGeometry` with `GeometryCombineMode.Exclude`, computed fresh per step from
  `target.TransformToVisual(TourOverlay)`). **`TourOverlay` is `FlowDirection="LeftToRight"`, overriding
  the app's inherited RTL** — the positions are computed in physical coordinates via `TransformToVisual`,
  and RTL would silently mirror `Margin`/`HorizontalAlignment="Left"` to the wrong side; the callout
  bubble re-applies `FlowDirection="RightToLeft"` locally so its Arabic text still reads correctly. Only
  targets **static chrome elements with one stable `x:Name`** (a button, a search box, a panel) — never an
  `ItemsControl.ItemTemplate`-generated element (e.g. a specific memory-plan card's button), because there
  is no single always-present instance to point at and the list could be empty when the tour runs; a step
  whose named target isn't found is skipped, not treated as a tour-ending error. Offered once per
  `AppTourContent.Version` (`App.OfferAppTourIfNewAsync`, right after the memory reminders in the same
  startup dispatcher chain — so it never competes with them for attention), tracked in
  `AppSettings.LastSeenTourVersion`; the "seen" flag is written whether the user accepts or declines, same
  as every other one-time prompt in this app. **Maintenance**: any future feature worth teaching needs a
  new step added to `AppTourContent.Steps` **and** `Version` bumped — otherwise a user who already saw an
  older version never sees the new step, since the check is a single version-equality comparison.
  **"الدليل" (`Views/HelpView.xaml`, `ViewModels/HelpViewModel.cs`, last nav item)** is a standing
  reference, unlike the one-time tour above — one `HelpTopic` (`Tour/HelpTopic.cs`) per sidebar screen (9
  total, `Tour/HelpTopics.cs`), each a plain-language description plus a "جرّبها معايا" button that runs
  a short (usually one-step) spotlight tour through the exact same `MainWindow.RunTourAsync` engine, not a
  separate mechanism. `HelpViewModel.TryTourAsync` reaches `MainWindow` via `Application.Current.MainWindow`
  (the same pattern `DailyEntryViewModel` already uses everywhere as a dialog `Owner`), not DI — the topic
  list is static content, no repository needed.
  **Depth (revised after real use)**: the first pass gave every topic exactly one shallow step (e.g. just
  "here's the filter button"); the user tried it and asked for the features actually worth knowing, 3-5
  per screen, reusing an existing `x:Name` where one already fit (`FilterToggle`, `PreviewGrid`,
  `ProductToggle`, `ActivityLogList`, `BackupCard`, `AccountsListCard`...) and adding one where the
  distinctive action had none (`AddWorkerButton`, `SetPasswordButton`, `ExportButton`, and ~25 more —
  always on static chrome, never inside an `ItemsControl`/`DataGrid` `DataTemplate`, since that repeats
  per row and has no single instance to point at).
  **"تسجيل الإنتاج اليومي" is 7 `HelpTopic`s, not one** — it has 7 internal tabs (`DailyEntryView.xaml`'s
  `TabControl`, bound to `DailyEntryViewModel.SelectedTabIndex`) each with a real, separate workflow, too
  much to fold into a single topic without becoming shallow again. `AppTourStep.TabIndex` (`int?`) carries
  the tab to select; `RunTourAsync` sets `DailyEntryViewModel.SelectedTabIndex` after navigating, the exact
  same mechanism `OpenInitialBalanceTabCommand` already used to jump to tab 1 — not a new pattern.
  **Conditionally-visible targets are allowed now**, not avoided — `AddAccountButton` (Department
  Accounts, `Visibility`-collapsed for non-manager accounts) is a deliberate step despite that, because a
  general engine fix in `RunTourAsync` covers it for every topic at once: after `FindTourTarget` finds an
  element, it also checks `Visibility == Visible` and `ActualWidth/Height > 0` before showing the step,
  skipping to the next one otherwise — the same graceful handling already used for a target that isn't
  found at all. Excluding every conditional element by hand would have thrown away real, important content
  (the whole point of this revision) for a problem one shared check already solves.
  **Maintenance**: a new sidebar screen needs both a new `TourScreen` enum value + `NavigateToTourScreen`
  case **and** a new `HelpTopic` here (or, if it has its own internal tabs like Daily Entry, one per tab)
  — nothing enforces this automatically, same caveat as the tour above.
  **Tour/Guide engine, round 2** (after real use showed the round-1 back/skip-only flow too rigid):
  `RunTourAsync` took a `TourAction { Next, Previous, Skip }` result instead of a plain bool, and the
  `for` loop became a `while` loop over a mutable index with a `delta` (±1) — a step whose target turns
  out hidden/missing is skipped **in the direction already being travelled** (advancing normally skips
  forward, "السابق" skips backward), otherwise pressing "السابق" straight into a skipped step could
  silently push you forward again, defeating the button. `TourBackButton.IsEnabled` is set to `i > 0` each
  step so it disables itself on the first step instead of doing nothing. The overlay also closes on
  **Escape** (`Window.PreviewKeyDown`, checked only while `TourOverlay` is visible so it can't intercept
  Escape used elsewhere) and on **clicking the dimmed area** (`TourDimPath.MouseLeftButtonDown`) — both
  just resolve the same `TourAction.Skip` the button does, no separate code path.
  `HelpTopics` is two lists now, `MainTopics` and `DailyEntryTopics`, not one flat `All` — `HelpView.xaml`
  renders them as two `ItemsControl`s sharing one `DataTemplate` (`UserControl.Resources`, referenced by
  both via `StaticResource`) with a section heading between them, so the seven Daily Entry tab-topics read
  as parts of one screen instead of seven unrelated cards. Every step's `Description` also got a concrete
  worked example (real values: "اختار أحمد، اكتب نص يوم، السبب...") after the first pass turned out to be
  correct but too abstract to actually teach the click-by-click "how" — found from real use, not a
  design guess up front.
  **"الدليل" cards are feature accordions now, not one forced tour per card** — a screenshot of the
  Workers screen's "أحسن 3 عمال" card made it clear one flat run-all-steps tour per topic still hid most
  of a screen's real features behind whichever few steps existed. `HelpTopic` (`Tour/HelpTopic.cs`)
  became an `ObservableObject` with `[ObservableProperty] IsExpanded`; its header `Button` now calls
  `HelpViewModel.ToggleTopicCommand` (single-open accordion across **both** `MainTopics` and
  `DailyEntryTopics` together, closing all others first — same pattern as
  `WorkersViewModel.ToggleSkillGroup`), and the expanded body lists every one of the topic's
  `AppTourStep`s as its own row with its own "جرّبها" button (`TryTourCommand`, now takes a single
  `AppTourStep` and calls `RunTourAsync(new[] { step })` — the engine needed no change, a one-element
  list just disables "السابق" and shows "1 من 1" automatically). `TryFullTourCommand` keeps the old
  run-everything-in-order behavior available as a small button inside the expanded card, for anyone who
  wants the guided walkthrough instead of picking one feature. `HelpView.xaml`'s topic `ItemsControl`s
  dropped their 2-column `UniformGrid` panel for a plain single column — a `UniformGrid` sizes every cell
  in a row to the tallest, so one expanded card in a 2-column row left an ugly empty gap next to it.
  **`SelectFirstWorker` (`AppTourStep.cs`, bool)** exists because the Workers profile's skills/stars/
  weekly-history steps only render once a worker is selected — same problem `TabIndex` solves for Daily
  Entry's tabs, same fix shape: `RunTourAsync`, after `NavigateToTourScreen`, sets
  `WorkersViewModel.SelectedWorker = Workers.FirstOrDefault()` when the step asks for it. Selecting a
  worker triggers an **async** detail load (`OnSelectedWorkerChanged` → `SafeAsync.Run(LoadDetailAsync)`),
  not a synchronous one, so these steps get a longer settle delay (400ms vs. the normal 150ms) before the
  engine searches for the target — tune this first if a profile-dependent step ever flickers/misses its
  target after a slower machine or a heavier profile load.
  Workers topic went from 3 steps to 9 as the concrete example: `BestWorkerCardsRow` (new `x:Name` on the
  Grid at `WorkersView.xaml`'s winners row) is reused for two different steps — what the ranking means,
  and what clicking a card does (opens "ليه فاز؟" in week mode vs. the profile directly in month/custom
  mode) — the same "same target, two steps with different text" pattern already used elsewhere rather
  than adding a redundant second name. `SkillsSectionHeader` (new name on the skills section's `DockPanel`
  header) is likewise reused for both "add a skill" and "star-rating logic," since the actual star
  buttons live inside a per-stage `DataTemplate` and can't be individually named. `WeeklyHistoryHeader`
  (new name on the section's static header `TextBlock`, not the `ItemsControl` below it) and
  `OpenWorkerOrderButton` (new name on the existing "ترتيب العمال" header button) round out the new
  targets. Worker reordering (`WorkerOrderDialog`) opens as a separate modal `Window`, which
  `MainWindow`'s `TourOverlay` can't spotlight into — that step targets the trigger button only and
  explains the dialog's three input methods (drag, up/down buttons, typed rank) in the description text
  instead of demonstrating them live.
  **Scope note**: this depth pass covered Workers only, as the concrete worked example — the other 14
  topics (Products, Memory, Evaluation, Reports, ActivityLog, Settings, DepartmentAccounts, and the 7
  Daily Entry tabs) still have their round-1 step counts and are candidates for the same "surface every
  real feature separately" treatment in a follow-up round, now that the accordion structure they'd need
  already exists generically.
  **Guided practice mode ("جرّبها بنفسك")**: the user rejected passive spotlight-explain as the ceiling —
  wanted the real element clickable under the spotlight, advancing on the real action, in an isolated
  sandbox so nothing touches real factory data. This is a **new sibling system** next to the spotlight
  tour, not a replacement: `Tour/AppTourStep.cs`/`HelpTopic.TourSteps`/`MainWindow.RunTourAsync` are
  untouched; `Tour/GuidedPracticeStep.cs` (`GuidedPracticeStep`, `GuidedPracticeFlow`) and
  `MainWindow.RunGuidedPracticeAsync`/`RunGuidedStepAsync` are the parallel path, reusing the same
  `TourOverlay`/`PositionTourStep`/`_tourStepTcs`/Next-Back-Skip buttons so there's no duplicated UI.
  **Step completion is a predicate over ViewModel state** (`GuidedPracticeStep.IsComplete`, checked on
  `PropertyChanged`/`CollectionChanged` via `WatchSelectors` for nested objects), not a `Click` hook on
  the target element — deliberately, because the "add a skill" worked example needs to detect completion
  inside `DataTemplate`-generated content (a specific product card, a specific star) that structurally
  can never get a static `x:Name` (the same hard rule as the spotlight engine), so a raw element-click
  handler couldn't reach it even in principle. The predicate approach also verifies the *result* (stars
  reached ≥3), not just "a click happened somewhere."
  **`AppServiceRegistration.AddWorkforceManagerCore`** (`WorkforceManager.UI/AppServiceRegistration.cs`)
  extracts `App.xaml.cs`'s entire DI registration list (repos, services, Views/ViewModels, `AddDbContext`)
  into a shared `IServiceCollection` extension parameterized by connection string — `App.xaml.cs` now
  just calls it with the real `AppPaths.DbPath`. This exists because practice mode needs a second,
  isolated container with the *same* registrations, and hand-copying that list a third time (there's
  already one accepted, deliberately-separate copy in `WorkforceManager.Tests/TestDatabase.cs`) would let
  production code drift out of sync with nothing to catch it — unlike the test copy, a missing sandbox
  registration only surfaces as a runtime crash for a real user. `ServiceRegistrationTests.cs` was updated
  to scan this new file instead of `App.xaml.cs`, since that's where registrations actually live now.
  **`Sandbox/SandboxSession.cs`** builds a temp-file SQLite `ServiceProvider` via that same extension
  method, seeded by `Sandbox/SandboxDemoSeeder.cs` with obviously-fake data (products/workers named
  "تجريبي"), and holds **one root `IServiceScope` open for its whole lifetime** — not a scope-per-call
  like `TestDatabase` — so `Scoped` services behave "like a singleton within the session" exactly the way
  they do in the real `MainWindow._session`, not like a test fixture. `MainWindow.EnterSandboxModeAsync`
  swaps `MainContent.Content` to a View resolved from the sandbox provider instead of the real one;
  `FindTourTarget` already just does `FindName` on whatever `MainContent.Content` currently holds, so the
  spotlight positioning code needed zero changes to work against the sandboxed screen. Every
  `NavXxx_Checked` handler gained one line — clicking any sidebar item while `_sandboxActive` resolves the
  pending `_tourStepTcs` with `Skip` and returns instead of navigating, so the first click always exits
  practice mode and a second click (now that sandbox mode is off) does the real navigation, rather than
  trying to do both in the same handler invocation.
  **Fixed along the way, not new scope**: `SelectFirstWorker`/`RunTourAsync` used to resolve
  `WorkersViewModel` via `_session.GetRequiredService<WorkersViewModel>()` — but `WorkersView`/
  `WorkersViewModel` are `Transient`, so that call built a second, never-displayed instance distinct from
  the one `NavigateToTourScreen` had already put on screen, meaning the three `SelectFirstWorker` steps
  (skills, star logic, weekly history) were silently skipping in production before this round (the
  selected-worker profile panel they target on the *real* instance never actually rendered). Reading the
  ViewModel from `(MainContent.Content as FrameworkElement)?.DataContext` instead — the same source
  `RunGuidedPracticeAsync` uses — fixes both paths from one root cause.
  **Debugging note that cost real time**: `EnterSandboxModeAsync` originally called
  `workersVm.LoadAsync()` explicitly after resolving the sandbox `WorkersView`, on top of the
  `Loaded += async (_, _) => await viewModel.LoadAsync();` the View's own constructor already
  wires (this pattern — self-load on `Loaded`, not in the constructor — is standard across this
  app's screens, "عشان الواجهة متعلقش"). Two calls on the same fresh instance race; whichever
  finishes **second** re-runs `Workers.Clear()`, which resets the ListBox's `SelectedItem` (and
  therefore `SelectedWorker`/`Detail`) back to null via ordinary WPF selection behavior — silently
  undoing whatever `SelectFirstWorker` had already picked, sometimes *mid-flow*, after step 1 had
  already started. Compounded by `[RelayCommand] async Task` methods (here,
  `HelpViewModel.TryGuidedPracticeAsync`) swallowing exceptions by default — `AsyncRelayCommand`
  doesn't await/rethrow — so none of this produced any visible error, only a spotlight box in a
  nonsensical position and "التالي" appearing to dump the user straight back to the Guide (steps
  2/3's targets, now inside a collapsed panel, both silently reported "not found" and the loop ran
  to completion). Fixed by not issuing a second `LoadAsync()` call at all — waiting on the View's
  `Loaded` event (proof the one real call started) then polling `IsLoading` to `false` (proof it
  finished) — and separately by wrapping `RunGuidedPracticeAsync` in try/catch → `Notify.Error` so
  a future bug in this class surfaces instead of vanishing. **General rule for any future
  automation/orchestration code that navigates to a screen and needs its data ready**: check
  whether the View already self-loads on `Loaded` before calling its load method again — if it
  does, wait for that one call instead of adding a competing one.
  **"الدليل" redesigned into a card grid + full-width detail panel, deepened into a real
  reference, and given an FAQ + a "تعلم مميزات التحديث" section** — the accordion-per-topic
  layout from the previous round scaled badly once every topic's content got deep (round-1 only
  covered Workers). `HelpTopic` gained `SubTopics` (`IReadOnlyList<HelpTopic>`, self-referential,
  default empty) and `HelpTopics.MainTopics`/`DailyEntryTopics` (two separate lists) collapsed
  into one `Topics` list of 9, in true sidebar order — Daily Entry is now **one** topic whose 7
  old top-level topics became its `SubTopics`, not 7 separate cards; nothing about their
  `AppTourStep`/`GuidedPracticeFlow` content changed, only the outer packaging. `HelpViewModel`
  swapped `ToggleTopicCommand` for `SelectTopicCommand` (`SelectedTopic`, plain selection across
  all 9 cards, not an accordion) plus `ToggleSubTopicCommand` (an accordion scoped to
  `SelectedTopic.SubTopics` only — Daily Entry's own tabs, independent of card selection).
  `HelpView.xaml`'s grid is an `ItemsControl` over a `WrapPanel`, not `UniformGrid` — the same
  "one taller cell stretches the whole row" problem from the accordion-in-a-grid round would have
  come right back, and `WrapPanel` handles a variable column count for free at 900px vs. a wider
  window. Grid tiles reuse `CardButton` (`Themes/Core.xaml`, the existing "the whole card is a
  button" style — gold ring on hover, darkens on press, no size growth) instead of a bespoke
  template, and reuse `HelpTopic.IsExpanded` as the "this tile is selected" flag so the gold
  border is a plain same-object `DataTrigger`, not a converter comparing against `SelectedTopic`.
  Every new command binding in `HelpView.xaml` uses `RelativeSource AncestorType=UserControl`
  instead of the old `AncestorType=ItemsControl, AncestorLevel=N` counting — `SubTopics`/
  `LearnVersions`/`Faq` each add their own nesting level, and counting levels by hand does not
  survive that. The staggered entrance animation (`HelpView.xaml.cs`, new code-behind file for
  this View) is a **real per-tile `Storyboard`** built in `Loaded`, not a `Task.Delay` loop —
  `ItemContainerGenerator.ContainerFromIndex` → `VisualTreeHelper.GetChild` finds each tile's
  root element, then `DoubleAnimation`s on `Opacity` (0→1) and a `TranslateTransform.Y` (14→0)
  run with `BeginTime = index * 60ms`; only the *construction* of that per-item `BeginTime`
  happens in code, because WPF has no practical declarative way to stagger a `Style.Trigger`
  animation per `ItemsControl` row by index.
  **Content deepened per screen from live code+UI audits, not from `CLAUDE.md` alone** — this
  file can drift from the real UI, so every gap list here was cross-checked against the actual
  Views/ViewModels before writing a single new `AppTourStep`. New `x:Name`s were added strictly
  on static chrome (never inside a `DataTemplate`), same rule as every prior round: e.g.
  `SkillReviewCard`/`PeriodControlCard` (Workers), `AddRackingStageButton`/`NoStagesWarning`
  (Products), `DecliningWorkersCard`/`WorkerAveragesToolbar` (Reports),
  `AdvancedGroupingCheck`/`ExportPayslipStripsButton` (Report Builder — the payslip-strip export
  flow had zero Guide coverage before this round despite being a fully separate workflow),
  `ScrapReasonsCard`/`ReportIdentityCard`/`AppLogoCard`/`DarkModeCard`/`LogRetentionCard`
  (Settings).
  **`Tour/HelpFaq.cs`** (`FaqEntry`: plain `Question`/`Answer` text, no `TourSteps` — an FAQ
  answer is prose, not a spotlight) + `HelpFaq.Entries`, rendered as one more single-open
  accordion at the bottom of `HelpView.xaml` via `ToggleFaqCommand`. The draft list was written by
  scanning likely real confusion points, then **shown to the user for review before shipping** —
  one entry ("إمتى بيتقفل إنتاج اليوم؟") was pulled because it asked about day-closure, a feature
  already removed outright (see the day-closure removal note elsewhere in this file); shipping a
  guessed FAQ list unreviewed would have re-taught a dead concept.
  **"تعلم مميزات التحديث"** is a new, separate, version-scoped reference — not a rename of the
  "إيه الجديد؟" tour above and not sharing its tracking. `AppVersion.cs` (new,
  `WorkforceManager.UI` root) extracts the version-reading logic
  `SettingsViewModel.AppVersionText` already had inline
  (`AssemblyInformationalVersionAttribute`, `+commitHash` stripped, falling back to
  `AssemblyName.Version`) into `AppVersion.Current`, now the one shared source both
  `AppVersionText` and this feature read — **deliberately the real build version, not
  `AppTourContent.Version`** (that field is a manually-bumped content counter, decoupled from
  `Directory.Build.props`, and the user explicitly asked for no number to remember to bump).
  `Tour/LearnFeaturesContent.cs` holds `LearnFeaturesVersion { Version, Features:
  IReadOnlyList<HelpTopic>, IsExpanded }` — each feature is a plain `HelpTopic` again, no third
  content type, with a `GuidedPracticeFlow` only where the sandbox actually supports it today
  (Workers) and a `TourSteps` spotlight otherwise. `AppSettingsStore.LastSeenLearnVersion` is a
  **field deliberately separate from `LastSeenTourVersion`** — the two "what's new" offers are
  independent, and conflating them would let seeing one silently suppress the other.
  `App.OfferLearnFeaturesIfNew` mirrors `OfferAppTourIfNewAsync`'s shape (same startup dispatcher
  chain, right after it) but is **synchronous**, not `async` — navigation here is synchronous, so
  an `async Task` version would only be flagged CS1998 for having no `await`. It no-ops entirely
  (no dialog at all) when no `LearnFeaturesVersion` exists yet for the current build. The
  version-comparison itself is **not** inlined in that method — it is
  `LearnFeaturesContent.ShouldOffer(string? lastSeenLearnVersion, string currentAppVersion)`, a
  pure static predicate with no WPF/`Notify`/settings-file coupling, specifically so it is
  directly unit-testable; `WorkforceManager.Tests` cannot reference `WorkforceManager.UI` (only
  `WorkforceManager.UiTests` does), so its tests and a `HelpViewModel` accordion-command test
  (`SelectTopic`/`ToggleSubTopic`/`ToggleFaq`/`ToggleLearnVersion`) live in
  `WorkforceManager.UiTests`, not the main test project.
  **Adding a future update's Learn content is a data change, not a plumbing change**: bump
  `Directory.Build.props`'s `<Version>` the normal way for the release, then add one new
  `LearnFeaturesVersion` at the **top** of `LearnFeaturesContent.Versions` (newest-first) with
  that same version string and one `HelpTopic` per genuinely new, user-facing feature shipped in
  it — no other code changes required. The first-launch offer, the version-match lookup, and the
  permanently-browsable older-versions list all key off that list automatically. The very first
  entry (matching the app's current build) retroactively bundles the user-facing features from
  the whole development stretch this Guide redesign itself is part of, confirmed with the user
  before any walkthrough content was written for it.
  `WorkersView` (+ `WorkersViewModel`, `WorkerEditDialog`) is
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
  **The reports and the monitoring screens are separate on purpose**, because they do two different jobs.
  `ReportsView` (nav: "التقييم والمتابعة") is looked at, not exported: two tabs only.
  1. **إنتاج اليوم** — three numbers for the day (completed / started / scrapped) then a card per product.
  2. **إنتاج المنتجات** — the chart, split **by day, week or month** (`ChartGrain`; the week is always
     `WeeklySummaryService.GetWorkWeekRange`, never a second definition), with the scrap segment, a
     product filter, an average line, and a comparison against the preceding period of the same length.

  A third tab, **"تقييم اليوم", was deleted at the user's request** along with `NeedsAttentionService`,
  `PerformanceEvaluationService` and their DTOs — it restated the day tab's numbers in different words and
  its per-worker table is available (and exportable) in the report builder as "الإنتاج بالعامل". The one
  thing it uniquely surfaced, "a stage with zero qualified workers", still appears on the products screen
  next to the stage itself. Don't rebuild it as a screen; if it comes back it belongs where the user is
  already standing.

  **Scrap is counted two ways on purpose** (`ProductionChartService`): what is *subtracted* from completed
  output is last-stage scrap only — the same rule the daily summary uses, so every screen says one
  number — while what is *displayed* as scrap is every stage's scrap, because "how much did we
  lose?" includes the piece thrown away at stage one. Subtracting the early scrap too would double-count
  it: it never reached the last stage, so it was never in that number.
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
  **The accent half of that same bridge died silently when the library was upgraded, and it took a real
  render (a `DatePicker` calendar popup, screenshotted in indigo blue) to catch it.** `App.xaml` used to
  override `PrimaryHueMidBrush`/`PrimaryHueLightBrush`/`PrimaryHueDarkBrush` (+ `SecondaryHue*`, +
  `*ForegroundBrush` pairs) with gold, matching the resource names MaterialDesignThemes used to expose for
  its own accent colour. **MaterialDesignThemes 5.1.0 renders from a completely different set of keys**
  (`MaterialDesign.Brush.Primary`, `.Light`, `.Dark`, each with a `.Foreground` twin) — confirmed by
  walking the live merged dictionaries at runtime and finding zero references to the old `PrimaryHue*`
  names anywhere in the library's own styles, while `MaterialDesign.Brush.Primary` held `#FF3F51B5`
  (stock Material Indigo) untouched. The old override wasn't wrong when it was written; the library moved
  the keys out from under it, and nothing failed loudly because most of the app is styled by our own named
  styles, not the library's defaults — a `DatePicker` calendar is one of the few places still drawn
  entirely by MaterialDesignThemes' own template, so it was the one place with nowhere to hide the gap.
  **Fixed the version-appropriate way**: `ApplyMaterialDesignBaseTheme` now sets `theme.PrimaryLight/Mid/
  Dark` and `SecondaryLight/Mid/Dark` in code (each a `MaterialDesignColors.ColorPair` of colour +
  foreground), read from the palette *after* `ApplyPalette` has already run so they flip with the theme;
  `BundledTheme.PrimaryColor="Indigo"` can't take an arbitrary hex directly (named Material colours only),
  which is why this has to happen in code, not XAML. The dead `PrimaryHue*Brush` declarations are gone —
  confirmed nothing in this app's own XAML referenced them either, so removing them cost nothing.
  Re-verify with the same method if the library is upgraded again: enumerate
  `Application.Current.Resources` (root **and** merged dictionaries — the override sits in the root, the
  library's real keys sit inside `MaterialDesign2.Defaults.xaml`) for anything containing `"Primary"`, and
  confirm the key this app writes to is the same one the library's styles actually read.
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
  **The implicit `TextBlock` style only helps a `TextBlock` with no local `Foreground` and no explicit
  `FontWeight`/`Style` that resets it — it does not make `Foreground` reliably inherit through every control
  template in this app.** A user report after the fix above still showed black icons/text in ~20 views
  (`ProductsView` per-stage action icons, `MemoryView`, `ReportsView`, `WorkersView`, `SettingsView`,
  `DailyEntryView`, `ActivityLogView`, `InitialBalanceDialog`, `ReportBuilderView`). A regex sweep for
  `<TextBlock` / `<materialDesign:PackIcon` attributes with no `Foreground=` found ~50 more instances: every
  one had an explicit `FontWeight="Bold"`/`"SemiBold"` or sat inside an `IconButton`/unstyled `Button`,
  which is enough to make some renders fall back to WPF's plain black default instead of the themed ink —
  in practice, ambient `Foreground` inheritance through this app's button/icon templates is **not reliable
  enough to depend on**. Fixed by setting `Foreground="{DynamicResource TextPrimaryBrush}"` (text) or
  `{DynamicResource GoldBrush}` (the ProductsView stage-action icons — pencil/assign/pause/reorder chevrons;
  the trash icon stays `DangerBrush`) **explicitly on every such element**, rather than chasing why
  inheritance failed in each template. **The convention going forward: never rely on ambient `Foreground`
  inheritance for text or icons in this app — set it explicitly via `DynamicResource` on the element
  itself.** Verify with a real dark-theme render (`XamlReader.Parse` + `RenderTargetBitmap`, see the render
  harness pattern used throughout this file's history), not by reading the XAML — a `Foreground` that
  *looks* set two levels up the tree is exactly what silently failed here.
  **`StatusChip`'s `ControlTemplate` ignores the `ToggleButton`'s literal `Content` entirely** — it hardcodes
  `Icon`/`Display`/`AccentColor` bindings read from the `ToggleButton`'s `DataContext` (built for the
  `StatusChoices` `ItemsControl` chips, which supply exactly those three properties per item). Any
  `StatusChip`-styled `ToggleButton` given literal XAML content instead (an icon + `TextBlock` in a
  `StackPanel`, e.g. DailyEntryView's old "السجل" history toggle) renders that content **nowhere** — no
  `ContentPresenter` exists in the template to draw it — no matter what `Foreground` is set on it. This is a
  different failure mode than the inheritance issue above (content is discarded outright, not mis-coloured),
  and was only caught by rendering the actual chip and seeing the text missing, not just faint. `ShiftChip`
  is similarly unsuitable for literal content (`Text="{TemplateBinding Content}"` only accepts a plain
  string). **`ToolbarToggle`** (used by the Products/Reports filter and period toggles) is the right style
  for a `ToggleButton` with arbitrary literal content — its template uses a real
  `ContentPresenter`. The "السجل" toggle now uses `ToolbarToggle`; `StatusChip` itself was left unchanged
  since its one remaining use (`DailyEntryView`'s `StatusChoices` `ItemsControl`) is genuinely data-bound and
  correct. Before styling a new custom `ToggleButton`/`Button` with literal (non-databound) content, check
  the target style's `ControlTemplate` actually contains a `ContentPresenter` — several styles in this file
  don't, because they were built for one specific, narrower binding shape.
  **A `ui:ThemeBrush.ForegroundKey` binding always wins over a plain `Foreground="{DynamicResource ...}"`
  set on the same element**, so never add the latter "just in case" next to the former. The blanket
  Foreground-hardening sweep above briefly did this on 8 elements (rows/cells whose colour is meant to
  flip per-item — e.g. `NetColor`, `ChangeColor`, `TypeColor`, `AccentColor`) before being caught and
  reverted; verified empirically (`XamlReader.Parse` + set `DataContext` + read back `TextBlock.Foreground`)
  that `ThemeBrush`'s binding always resolves *after* the parse-time literal value and overwrites it via
  `SetResourceReference`, and falls back to the implicit style's colour (not to the literal one) via
  `ClearValue` when the bound key is empty — so the literal `Foreground` was never wrong, only dead weight
  that could mislead a future reader into thinking it mattered. If a `TextBlock`/icon already carries
  `ui:ThemeBrush.ForegroundKey`, its dark-theme visibility bug (if any) is in the *key it's bound to*
  returning an untinted colour, not in a missing `Foreground`.
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
- **The window layout scales to the screen; it is not sized to one machine.** Every screen is built
  against a fixed design area — `MainWindow.DesignWidth/DesignHeight` (1200×700) — because the sidebar is
  248 wide and the widest screen (Products) needs ~918 inside it. A 1366×768 laptop at the **125% scaling
  Windows picks by default** offers only 1093×614 DIPs, which is less, so content was simply **clipped
  with no warning** — the same failure as the Products button row, one level up. `MainWindow`'s root grid
  now carries a `ScaleTransform` on its **`LayoutTransform`**, set to
  `min(ActualWidth/1200, ActualHeight/700)` on every `SizeChanged` — it scales **up as well as down**.
  The scale was originally clamped at 1 on the reasoning that spare room on a big monitor should become
  more content, not bigger chrome. **That was wrong for this app and the user rejected it after seeing a
  render**: with the clamp, a wide screen only widened the columns while font sizes and icons stayed
  fixed, so the sidebar grew to 288 while its 14pt labels did not, leaving small text stranded in an empty
  panel — while the identical markup at 1362 looked comfortable. That is what "big on one screen, small on
  another" meant. Uncapped, **every 16:9 screen converges on the same ~1244 logical width** (the height
  term binds at any aspect wider than 1.71:1), so 1366, 1920 and 4K draw a literally identical layout and
  differ only in physical size. The accepted trade-off is that a large monitor shows the same number of
  rows more comfortably rather than more rows. There is deliberately **no upper cap**: the ratio is tied
  to the real screen so it is self-limiting, and an artificial ceiling would just create a cliff at one
  resolution. Content still fits everywhere — the widest screen needs 1128 against 1351 available.
  **`DesignHeight` is measured, not chosen, and the sidebar is what sets it.** The sidebar is the only
  thing that must render complete without scrolling, and it measures **706** (nine nav items plus the day
  card, account card and final-save button). It was originally guessed at 700, which clipped the last nav
  item — "الحسابات الإدارية" vanished entirely on large screens, because scaling up *reduces* the logical
  height (1020 ÷ 1.457 = 700) and the nav `StackPanel` is the fill element that absorbs the shortfall.
  It is now 760, leaving roughly one extra nav item of headroom. **Re-measure it if a nav item is added** —
  the nav list is wrapped in a `ScrollViewer` so a future overflow scrolls instead of disappearing
  silently, but scrolling a nine-item nav is the fallback, not the intent.
  **`LayoutTransform`, not `Viewbox`, and not `RenderTransform`**: a `Viewbox` measures its child at the
  child's own desired size and scales the drawn result, which defeats `*` columns and softens text.
  `LayoutTransform` hands the child the available space **divided by the scale**, so layout genuinely runs
  at 1200 and is then drawn smaller — proportional columns still work and glyphs are still rasterised
  vector-sharp at their real device size. The scale is **clamped at 1** on purpose: extra room on a big
  monitor belongs to the `*` columns as more visible content, not to inflating everything.
  `MinWidth`/`MinHeight` (900×560) stop the window shrinking below what stays legible, and
  `ClampRestoreSizeToScreen` trims the XAML's 1200×720 restore size to `SystemParameters.WorkArea` —
  without it, un-maximising on a small laptop left half the window (and its title bar) off-screen.
  Fixed split columns that used to pin a panel at one pixel width (Products 500, Memory 420) are now
  proportional with a `MinWidth` floor, so a wide monitor actually gets used.
  **The sidebar is a ratio, not a number.** At a fixed 248 it took 18% of a 1362-wide window and 13% of a
  1917-wide one — the same strip, reading wide on one machine and thin on the other, because what changes
  is the screen around it. It is now `clamp(210, 0.15 × logicalWidth, 320)`, which holds ~15% across the
  range people actually use and sends the rest to content. Note it is computed against the **logical**
  width (`ActualWidth / scale`), not the raw window width, or the ratio would be wrong whenever the
  scale-down above is active. The 210 floor is **measured, not guessed**: the longest item
  ("تسجيل الإنتاج اليومي") is 118.7 DIP in Tajawal at 14, and the fixed chrome around it is 84 (container
  margins 12+12, item padding 16+16, icon 18 plus its 10 gap) = 203, with the selected item's SemiBold
  measuring the same to a tenth. Re-measure before lowering it, and re-measure if a longer nav label is
  ever added.
- **Dialogs take their scale from `MainWindow.CurrentScale`, because they are outside its visual tree.**
  The `LayoutTransform` above lives on `MainWindow`'s root grid, so it reaches every screen but **no
  dialog** — each is its own top-level `Window`. Scaling up therefore left dialogs at their authored size
  on top of a magnified app: 30% smaller than their surroundings on a 1080 screen, 65% on 4K.
  `CrispWindows.ApplyScale` gives each dialog the same `ScaleTransform` on its content, and — this is the
  part that is easy to miss — also multiplies the **`Width`/`Height` set on the `Window` itself**, since
  those sit outside the content being transformed and would otherwise crop the scaled content inside a
  frame still at its old size. `NaN` means `SizeToContent` governs that axis and resizes itself. The
  dialog is then re-centred on its owner, because changing size after a window is shown leaves it visibly
  off-centre. `MainWindow` is excluded — it already scales internally, and scaling it here would square
  the factor. The default of 1.0 matters: messages such as "the program is already running" appear
  **before** `MainWindow` exists, so there is no scale to copy yet.
- **Sharp rendering is applied to every window from `CrispWindows`, not per-window.** `UseLayoutRounding`
  plus `TextOptions.TextFormattingMode="Ideal"`/`TextRenderingMode="ClearType"` were set only in
  `MainWindow.xaml`; the **other 30 windows — every dialog — had none of them**, and there is no implicit
  `TargetType="Window"` style to catch them (there never was one, and it would not have worked anyway:
  they are all derived classes, the same trap documented above for `MainWindow` and `DatePickerTextBox`).
  Dialogs are where it shows most — every one is `CornerRadius="18"` with a `DropShadowEffect` and hairline
  borders, and all of those land on fractional device pixels at any scaling other than 100%.
  `CrispWindows.Enable()` runs once in `OnStartup` and uses **`EventManager.RegisterClassHandler` on
  `typeof(Window)`**, which fires for derived types — so no existing dialog can miss it and no future one
  can either. It only sets a property whose local value is unset, leaving `MainWindow`'s explicit XAML
  values as the visible source of truth.
- **A card action row goes in a `WrapPanel`, never a horizontal `StackPanel`.** A horizontal
  `StackPanel` neither wraps nor compresses: when its children exceed the available width it just
  keeps laying them out past the edge, and the card's `Border` clips whatever hangs over — **silently,
  with no build error, no test failure, and no visual cue that a button is missing**. The Products
  screen hit this: its detail column is a fixed `Width="500"` (`ProductsView.xaml`), leaving
  500 − 10 margin − 28 `Card` padding = **462px**, while the five action buttons measure **565px** the
  moment "إضافة مرحلة الرص" is visible (it only shows while `HasRackingStage` is false, which is why
  the bug looked intermittent — with four buttons the row is 411px and fits). The gold "إضافة مرحلة"
  button was being sliced down to a sliver against the card edge. `WrapPanel` is the existing pattern
  for this everywhere else (`ActivityLogView`, `ReportBuilderView`, `ReportsView`). When converting one,
  give every child a **bottom** margin too (`0,0,6,6`) — a `StackPanel`'s children only ever needed a
  trailing margin, and without the bottom one the two rows touch after wrapping; subtract that 6 from
  the panel's own bottom margin so the spacing below the row stays what it was.
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
- **`PayslipStripExcelService` prints the whole team's payslips on one sheet**, **8 workers per A4 landscape
  page as a 2-row × 4-column grid** (raised from a single row of 4, at the user's request, to cut paper
  usage in half), cut apart along both the vertical *and* horizontal dashed lines — it reads
  `PayrollService.GetPeriodPayrollAsync` directly, so the numbers on paper always match the wage report on
  screen. `SlipsPerRow` (4) is the horizontal count and stays the same as before — only a second row was
  added underneath, reusing the exact same `SlotFirstColumn` columns (1/4/7/10) at a vertical `rowOffset`,
  since `WriteSlip` already took a `slot` for its column position and only needed a row-offset parameter to
  stack a second copy of the same layout beneath the first. `MaxBreakdownLines` dropped from 6 to 3 and
  every row height/font size was scaled down (title/name/period/section/line/net rows and their fonts) so
  two stacked slips have a realistic chance of fitting one page's height without `FitToPages(1,1)` shrinking
  them into illegibility — some shrink is still expected and acceptable (a full 6-line-breakdown, all-fields
  slip is still taller than half a landscape page), unlike the original 4-per-row design which was tuned to
  need **zero** shrink. **Every slip must have exactly the same line count**, even when workers worked
  different numbers of stages, or the cut line stops being straight; `WriteBreakdown`'s zero-padding is what
  keeps them equal — this now also keeps the *horizontal* cut between the two rows straight, since the top
  row's shared height (`topHeight`) is what the bottom row's `rowOffset` is computed from. Which lines print
  is now **user-selectable**
  (`PayslipStripField`: daily rate, produced workdays, days worked, stage breakdown, total pieces, absence/
  penalty deductions, net workdays, workdays wage, bonus, advance — factory/worker/period/net-amount are
  always on and aren't part of the list). Hiding a whole section is done by gating the section header too,
  not just its lines, so an empty section never prints a bare heading; hiding `StageBreakdown` specifically
  skips the padding logic rather than breaking it, so the cut-line invariant survives field selection.
  Field choice auto-persists to `AppSettingsStore.PayslipStripFields` (`null` = every field — an existing
  install sees no change after the update). **`PayslipFormatStore`** layers named, saved, switchable
  presets on top of that single live selection — same JSON-file-next-to-the-DB shape as
  `ReportTemplateStore` (`payslip-formats.json`), with two built-ins ("الفورمات الكامل" = every field,
  "مختصر" = net workdays + penalty + bonus). Picking a preset applies it to the live field checklist (which
  keeps auto-persisting from there); saving a new preset or renaming one folds in whatever is checked
  *at that moment*, not just the name — an earlier version's "rename" only touched the name and silently
  dropped any checklist edits the user had just made, and saving under a name that collided with a built-in
  produced two identically-labelled entries where the built-in (first in list order) won every reselect,
  making the user's edit vanish right after they saved it; both are guarded against now. **There is no
  single-worker full-page payslip window any more** (`PrintPayslipCommand`/`CanPrintPayslip`,
  `Views/PayslipWindow`, `ViewModels/PayslipData`) — it was removed outright at the user's request; don't
  reintroduce it. The team-wide strips above are the only payslip-printing path left.

### Database rules (audited — don't undo these)

- **Never call `.Date` on a date column inside a query.** `dp.Date.Date >= from.Date` translates to
  `date(...)` in SQL, which SQLite cannot answer from an index — so it scans the whole table and every
  `Date` index becomes pure write cost for zero read benefit. Every one of the 20 date predicates in
  `Repositories/` used to do exactly this. Compare the column directly (`dp.Date >= from.Date`); it is
  correct because **every write path normalises with `date.Date`**, so every `Date` column holds
  midnight. `DatabaseIntegrityTests` covers both halves: the range boundaries are inclusive, and every
  stored `Date` equals its own `.Date`. The one exception is `ActivityEvent.OccurredAt`, which stores a
  real time (`DateTime.Now`) — it uses a half-open range (`>= from.Date && < to.Date.AddDays(1)`).
- **CHECK constraints** guard the tables themselves: stars 1–5, stage quota > 0, stage difficulty
  multiplier > 0, production `PieceCount >= 0 AND PiecesPerWorkdayAtEntry > 0` (that column is the
  divisor behind every wage), adjustment amount > 0, daily wage >= 0, plus (added with Initial Balance)
  balance/range/usage quantities > 0, plus (an audit-round gap fix) `ProductionScrap.PieceCount > 0` and
  `ProductionStageOutput.PieceCount > 0`. The services already enforce all of these; the constraints
  exist so a future code path, a bad migration, or an external tool can't put the data in a state the
  reports would silently mis-total. They were verified against the live DB (0 violations) before being
  added. **A `[Range(...)]` data-annotation on a model property is a C# validation attribute only — EF
  Core's SQLite provider does not translate it into a real database CHECK constraint.** `ProductionScrap`
  and `ProductionStageOutput` both carried `[Range(1, int.MaxValue)]` on `PieceCount` for a long time,
  which *looked* like the same protection every sibling numeric column got, but a `CreateTable` migration
  for either table had no `CheckConstraints:` clause — confirmed by generating a fresh migration and
  reading it, not by assuming. Both feed the same "pending work" aggregates `DailyProduction.PieceCount`
  does, so a negative or zero row would have silently corrupted a report total exactly like the
  documented cases above. If a new numeric column needs a real floor/ceiling, add
  `HasCheckConstraint` in `AppDbContext`; a `[Range]` attribute alone is decoration.
- **Every `decimal` needs an explicit `HasColumnType`.** EF's SQLite provider maps `decimal` to TEXT by
  default, and TEXT compares lexicographically — `"10.5" < "9.0"` is true. `WorkerSkill.MeasuredRatio`
  was stored that way (the other three decimals were configured); it is now `decimal(5,2)`. A test
  asserts the column type so a new decimal can't quietly land as text.
- **Deleted as dead, don't reintroduce**: `Attendance.CheckInTime` / `CheckOutTime` (the only write path
  set them to `null` explicitly; hourly work is tracked in `HourlyWorkLog`, which has a real end hour),
  and the `Notes` column on `Attendance` / `DailyProduction` / `Penalty` / `HourlyWorkLog` — no caller
  ever passed a value, so the optional `notes` service parameters went with them (the `ProductionDayClosure`
  table this column also lived on is gone entirely now — see Day closure above). Also
  `IX_ActivityEvents_EventType`: no query filters on it, and 11 distinct values would
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
  **`LoginWindow` follows the user's theme** — it used to be pinned to light with its own
  `Palette.Light.xaml` in `Window.Resources` (window resources resolve before application ones, so the
  local palette beat whatever `ApplyTheme` had set). The original reasoning was that black on the very
  first screen reads as a loading screen rather than the app's front door; the user asked for the
  opposite outright — someone running dark mode wants the whole app dark from the first screen, and the
  jump from a white login to a black app was itself the jarring part. The local palette is gone, so the
  window inherits the application dictionaries like every other screen. `ApplyTheme` already runs in
  `OnStartup` before the first `LoginWindow` is built, and nothing reverts it on logout, so the theme
  holds across logout → login too.
- **No window sets `Icon` in XAML — `AppIcon.ApplyTo` is the only thing that sets a window icon.**
  `LoginWindow.xaml` and `MainWindow.xaml` both used to carry `Icon="…/Assets/app.ico"`, which is loaded
  *inside* `InitializeComponent()` with no error handling: a transient failure reading that resource
  throws `XamlParseException` and **kills the whole window construction**. That is not hypothetical — a
  real `crash.txt` caught it (`Cannot locate resource 'assets/app.ico'` during
  `LoginWindow.InitializeComponent()`), and the app simply refused to open. The attribute was also pure
  duplication: both constructors call `AppIcon.ApplyTo(this)` immediately after `InitializeComponent()`,
  which overwrites whatever XAML set. `AppIcon`'s own default is now a `Lazy<BitmapImage?>` with a
  try/catch instead of an eagerly-initialised `static readonly` field — a throwing type initialiser
  becomes a `TypeInitializationException` that rethrows on *every* later touch, so one transient failure
  would have killed icons for the rest of the session. A window with no icon beats a window that won't
  open. The exe's own icon (`<ApplicationIcon>` in the csproj) is a separate Win32-level thing and is
  unaffected by any of this.
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
  - **`PendingWorkService` is the only unbounded query in the app** — it sums every production row
    ever recorded, grouped by stage, and the daily-entry screen calls it every time a product is
    picked. On a 30-year database (432k rows) that took **1047 ms**, and it grows every single day.
    Two **covering** indexes fixed it (`DailyProduction(ProductionStageId, Date, IsDeleted, PieceCount)`
    and the matching one on `ProductionScrap`): SQLite computes the sum straight from the index without
    touching the table — **40 ms** on the same data. If you ever reorder those index columns the query
    silently falls back to a table scan, so measure with `EXPLAIN QUERY PLAN` (look for
    `COVERING INDEX`), don't eyeball. Every other query in the app is bounded by a date range.
- **Ranges no longer carry any lot identity** (`FlowRangeDto`): from-stage, to-stage, piece count.
  Ranges still may not overlap (a stage in two ranges is double-entry) and each covered stage still
  needs worker shares summing exactly to its pieces. A range may start anywhere in the line — starting
  mid-line needs no justification, because the pieces it consumes are implied by the arithmetic.
- **Production memories are the one and only exception to "line order is `SortOrder`".** A memory
  (`ProductionMemory` + `ProductionMemoryStage`, the "الذاكرة" screen) is a deferred plan: a product, a
  custom ordering of its stages, notes, and a reminder date. Pressing "ابدأ الآن" on a due reminder opens
  Daily Entry with that product and **validates that session's ranges against the planned sequence
  instead of the product's real line**, so a range the real line would reject as reversed is accepted.
  This was confirmed with the user as deliberate, with no extra guard or confirmation.
  **The exception is exactly one call.** `RecordFlowAsync` takes an optional `customStageOrder` and hands
  it to `StageRangeValidator.ValidateAndComputePiecesPerStage`, which already took the sequence as an
  explicit parameter — so there is **one rule evaluated against two orderings, never a second copy**
  (`FlowRangeTrimmer.Trim` was already parameterised the same way and needed no change either). The
  sequence is resolved by `ProductionLine.CustomOrder(activeLine, ids)`, built **from the real active
  line**, so a stopped stage or the racking stage can never enter it, and a duplicate is still rejected —
  one stage twice is double-counted wages whatever the ordering.
  **What deliberately does NOT get the custom order** is the part that matters most:
  `SyncStageGapBalancesAsync` used to share the same `orderedStages` variable but means something
  entirely different — it measures cumulative all-time gaps between stages that are *physically adjacent
  on the real line* and writes permanent `InitialBalance` rows from them. Fed a custom order it would
  compare stages that are not adjacent at all and mint balances that outlive the session, which
  `ReconcileAutoBalancesAsync` (always on the real line) would then disagree with forever. It now takes
  `realLine` explicitly. Everything else inside the method is per-stage and order-neutral. Outside it,
  nothing changes at all: reports, `PendingWorkService`, the Products screen and an ordinary session for
  the same product all keep reading `ProductionLine.Active`. `CustomStageOrderTests` exists to guard the
  boundary rather than the feature — it asserts gap balances still land on the stage the *real* line says
  work is stuck at, the daily report still counts the real last stage as completed, and the next ordinary
  session rejects exactly what it rejected before.
  **A plan may omit stages** (confirmed with the user), not just reorder them. The consequence is
  accepted, not overlooked: a skipped stage stays at zero output, so the gap calculation correctly raises
  an initial balance at that boundary — the pieces really did pass it by. Omitted stages are hidden from
  the session's cards entirely rather than shown greyed out, because a worker assigned to one would only
  be refused at save time with a confusing "stage not in this session" message.
  **The stale-plan hazard is real, not theoretical**: `DailyEntryViewModel`/`DailyEntryView` are
  **singletons**, so a plan's order left on a session would silently govern the next ordinary session.
  `FlowSessionViewModel._memoryStageOrder`/`_memoryId` are cleared together in `OnSelectedProductChanged`
  (a different product means a different plan) and die with `FlowSessions.Clear()` in `ResetForNewSession`;
  `StartFromMemoryAsync` always adds a **fresh** session rather than reusing the first one, so an unsaved
  distribution already on screen is never overwritten by a reminder.
  **A memory moves to the "منجزة" list only after a real production save for that session — not the
  moment the screen opens.** This reverses an earlier confirmed decision ("the reminder's job is to
  remind, and it finished it the moment it delivered the user to the screen"): real use showed a plan
  opened from its reminder and then abandoned (nothing ever saved) still sat in "منجزة" forever, with no
  evidence anything was actually produced. `App.StartMemorySessionAsync` no longer calls
  `MarkStartedAsync` before opening the screen — it only validates the plan (`GetStageOrderForSessionAsync`,
  still throws first if the plan has gone stale) and passes the memory's id through
  `MainWindow.OpenDailyEntryForMemoryAsync` → `DailyEntryViewModel.StartFromMemoryAsync` →
  `FlowSessionViewModel.ArmMemoryOrderAsync(stageOrder, memoryId)`. `MarkStartedAsync` itself is now called
  from inside the session's own save path, right after a save actually succeeds — its existing
  `if (memory.CompletedAt is not null) return;` guard makes a second save in the same session harmless.
  A plan whose product was since deactivated or deleted, or whose stages left the line, still shows its
  reminder with "ابدأ الآن" disabled and the reason spelled out; `BlockedReason` is derived at read time,
  never stored, because the product can change at any point after the plan was written.
  A plan that got marked "منجزة" without a real save (from before this fix, or any other mix-up) can be
  moved back to "نشطة" from its card — `ProductionMemoryService.ReactivateAsync` just clears `CompletedAt`
  and logs it as an edit; it refuses a plan that's already active.
  **Saving, editing, deleting, or reactivating a memory plan is logged**
  (`ProductionMemoryCreated`/`Edited`/`Deleted`, `ReactivateAsync` logs `ProductionMemoryEdited` too) —
  found as a real gap the same way the department-account one was: a user reported that signing off a day
  and then saving a plan still let the app close without asking again. `ProductionMemoryService` had zero
  `LogAsync` calls at all before this, for any of its writes. `PostponeAsync` (just moving a reminder date)
  is still deliberately unlogged — same reasoning as everywhere else in this file: log what has real
  value, not every write.
  **The Daily Entry screen marks a session as memory-driven**: `FlowSessionViewModel.IsFromMemoryPlan`
  drives a small banner at the top of the flow-session card ("الرحلة دي من خطة محفوظة في الذاكرة") so the
  user isn't confused about why a session opened pre-filled — the property existed before but was never
  bound in any XAML until this.
  **The "الذاكرة" screen itself**: the nav icon carries a due-count badge (same pattern as the activity-log
  badge, sourced from `GetDueAsync(DateTime.Today).Count`, refreshed at the same call sites as
  `RefreshActivityBadge`); Active cards show an overdue/due-today chip (`ProductionMemoryDto.IsOverdue`/
  `IsDueToday`, computed like `IsBlocked` — never stored) plus quick "ابدأ الآن" and "أجّل" buttons
  (the latter needs a dialog owner, so it's a `Click` handler in `MemoryView.xaml.cs` using the same
  `Window.GetWindow(this)` pattern as `ReorderStages_Click`, not a `Command`); both lists filter by product
  name through a `SearchText` property, same manual-rebuild-from-a-backing-list pattern as
  `ProductsViewModel.ApplyFilter` (not `ICollectionView`).
- **Day closure was removed outright** (`DayClosureService`, `ProductionDayClosure`, the lock/reopen
  button on Daily Entry, the "اليوم مقفول" badge on the Reports screen — all deleted, not deprecated).
  It used to let the user lock one date's production numbers against further edits after reviewing
  them; nothing replaces that specific behaviour, since every day is free to edit at any time now,
  including one that used to be closed. **This is a completely different concept from daily
  operations sign-off below**, which survives — closure locked numbers per date; sign-off is an
  app-wide "I reviewed everything that happened today" acknowledgement with no locking effect on
  editability at all. `ProductionDayClosures` was dropped via a real migration
  (`RemoveProductionDayClosure`) — the table carried only a point-in-time snapshot
  (`ClosedAt`/`CompletedPieces`/`StartedPieces`) with no foreign key pointing at it from anywhere, so
  the drop is lossless from every other table's perspective. `ActivityEventType.ProductionDayClosed`/
  `ProductionDayReopened` (14/15) and `DailyProductionReportDto.IsClosed`/`ClosedAt` are two different
  stories on removal: the enum values **stay** (real historical rows already reference them, same
  reasoning as `InitialBalanceMigrated` — nothing writes them anymore, but old activity-log entries
  must still render as Arabic text instead of a bare number), while `SensitiveAction.CloseProductionDay`
  was **deleted outright** (never persisted anywhere — it only ever flowed as a runtime parameter into
  `VerifyAsync` — so there's no historical row whose meaning depends on that number staying reserved).
  For the same reason the two surviving enum values stay listed in `ActivityEventRetention.ShortLived`
  even though nothing writes them: dropping them from that list wouldn't delete anything, it would
  quietly promote every old closure row to the 365-day default (retention is long-by-default), so a
  feature that no longer exists would start keeping its log entries *four times longer* than when it did.
- **A login is a DI scope, and logging out disposes it.** `MainWindow`, `DailyEntryView` and
  `DailyEntryViewModel` are **`Scoped`**, and `App.StartSession` creates one scope per login and resolves
  the window from it; `MainWindow` is handed that scope as `IServiceProvider` and resolves every screen
  from it rather than from the root. Logout (`App.SignOutAndRestartSession`) genuinely closes the window
  and disposes the scope, so the next account gets fresh objects. Before this, all three were `Singleton`
  and Logout never closed anything — it opened `LoginWindow.ShowDialog()` **on top of the still-open main
  window**, leaving the previous user's data sitting behind it, then reused the same window and undid the
  previous session by hand (`ResetForNewSession`, now deleted). That hand-cleanup was the only thing
  preventing leakage between accounts, and it silently fell behind every time a screen gained new state.
  Within a session `Scoped` behaves exactly like `Singleton`, so the documented reason `DailyEntryView`
  must outlive navigation — an unsaved production flow surviving a trip to another screen — is unchanged.
  **Order matters in the logout path**: `ShutdownMode` flips to `OnExplicitShutdown` *before* the window
  closes, because closing the main window while it is still `OnMainWindowClose` shuts the whole app down
  instead of reaching the login screen; `StartSession` puts it back. `ToastHost.Current` is a static, so
  the host now `Unregister()`s on `Closed` — otherwise it kept a dead window's entire visual tree alive
  and any toast raised while the login dialog was up would have gone to an unshown window and vanished.
  `SessionLifetimeTests` guards the lifetimes: flip one back to `Singleton` and it fails.
- **The sign-off gate is one method with three triggers.** `RunFinalSaveFlowAsync(SignOffTrigger)` is
  called by the "حفظ نهائي" button, by `MainWindow_Closing`, and by Logout. Logout is gated for the same
  reason closing is: it hands the machine to another account, so an unsigned day loses its owner either
  way. Only the **refusal message** varies by trigger — the button needs none (the user asked for the
  flow, the dialog is the answer), while the other two must say why the thing they asked for stopped.
  A second copy of the check for Logout is exactly what this project's "one rule per concern" forbids;
  it would have drifted on the first edit to either copy.
- **Daily operations sign-off** (`DailyOperationsSignOffService` + `DailyOperationsSignOff`) replaces
  an instant operations-password prompt on nearly every save/edit/delete with **one password entry at
  the end of the day** that covers everything. This split every `SensitiveAction` into two tiers:
  - **Tier A (unchanged, still an instant `SensitiveActionDialog.Ask` prompt)**: `DeleteWorker`,
    `DeleteProduct`, `DeleteStage` (and department/manager account deletion, which reuses
    `DeleteWorker`), `EditWorkerWage`, `SaveWageAdjustment` (advances/bonuses — direct EGP movement),
    and settings — only the activity-log retention days
    (`SettingsViewModel.SaveLogRetention`, `SensitiveAction.ChangeSettings`) are gated; the rest of the
    settings screen has no single "save" action to gate (every field auto-persists on change) and
    gating cosmetic/operational fields (logo, external backup folder, scrap reasons) would fight the
    whole point of this feature. `EditWorkerWage` and `ChangeSettings` were **real gaps**: the enum
    value existed but nothing ever called `VerifyAsync` with it before this — editing a worker's wage
    or shortening the log retention window (which can erase the very audit trail this feature relies
    on) went through with zero protection. `WorkerManagementService.UpdateWorkerAsync` now gates only
    when `DailyWageEgp` actually changes (renaming a worker never asks).
  - **Tier B (no more instant prompt)**: `RecordProduction`, `SaveAttendance`, `SavePenalty`,
    `RecordScrap`, `EditProductionPieces`, `DeleteProduction` — the routine, many-times-a-day actions
    that were the actual complaint. Every business method behind these had its `VerifyAsync` call and
    `operationsPassword` parameter **removed outright** (not left as a dead unused parameter) —
    `AttendanceService.RecordAttendanceBatchAsync`, `PenaltyService.RecordPenaltyAsync`/
    `UpdatePenaltyAsync`, `ProductionFlowService.RecordFlowAsync`, `ScrapService.RecordAsync`/
    `EnsureAllowedAsync`, `WorkdayCalculationService.UpdateProductionAsync`/`DeleteProductionAsync`/
    `DeleteProductionDayAsync`. The one funnel shared with Tier A deletions, `SoftDeleteService.DeleteAsync`,
    special-cases `descriptor.Action == SensitiveAction.DeleteProduction` to skip `VerifyAsync` while
    every other action through that funnel (worker/product/stage) still gates — this was cheaper and
    less error-prone than threading a second "skip the gate" parameter through the shared method.
    A few actions were already ungated with no `SensitiveAction` of their own (editing/deleting an
    initial balance, deleting a penalty) — those just got their `SensitiveActionDialog.Ask` replaced
    with the new confirm-only variant below for a consistent look; deleting a scrap row was **also**
    missing from the activity log entirely (no `ActivityEventType` existed for it) — closed with a new
    `ScrapDeleted` type, since Tier B's only remaining trail is the activity log.
  - **`SensitiveActionDialog.AskConfirm`** is the Tier B replacement for `Ask`: identical look (title/
    colour/icon driven by `SensitiveActionKind`, same Cancel = "go back and edit") but hides the
    password box **and** the "not configured" hint — Tier B was never going to ask for a password, so
    "no password configured" would be a non-sequitur, unlike genuine Tier A "not configured yet".
  - **The sign-off row is a global flag per calendar date, not per login account** — `DailyOperationsSignOff`
    has no `AppUserId`, deliberately, even though `OperationsPasswordService` verifies each account's
    *own* password. Any signed-in account entering *their* correct operations password signs off the
    whole day for the whole app; this was a direct decision (asked explicitly, not assumed) because
    the alternative — one sign-off row per account — would complicate "can the app close" and the
    startup catch-up below for no real benefit on what is in practice a single-operator-per-day tool.
  - **A signature only covers what happened *before* it — later activity re-arms the guard.**
    `IsFullySignedOffAsync(date)` is the real question the close-guard asks, not "does a sign-off row
    exist": it's signed off **and** nothing was logged after `SignedOffAt`. This was found in testing —
    the day was signed at 18:37, then a worker was deleted at 18:47 and a whole production day at
    19:12, and the app closed silently: 16 operations with no signature covering them, because the
    only signature predated them all. `SignOffAsync` therefore **updates** an existing row's
    `SignedOffAt` rather than refusing ("already signed off"), keeping one row per date (the unique
    index stands) carrying the *latest* signature; the full chain of signatures lives in the activity
    log, which is the audit trail that matters. `AcknowledgeLateAsync` upserts the same way, and
    `GetUnsignedPastDatesAsync` scans from the last signed date **inclusive** (not `+1`) for the same
    reason — a past day signed at 18:00 with activity at 20:00 must still surface at startup.
    **`DaySignedOff` events are excluded from that comparison**, and skipping that exclusion is not a
    detail: `LogAsync` runs *after* the transaction commits, so the signature's own event is always
    a few milliseconds *later* than `SignedOffAt` — every signature would instantly invalidate itself
    and demand another, forever. `GetActivitySinceLastSignOffAsync` feeds the review dialog from the
    same rule, so the user only ever reviews what they haven't already signed for.
  - **"Signed off on time" vs "caught up late" is derived, never stored**: compare
    `SignedOffAt.Date` to `Date` — no separate flag. `SignOffAsync` (today, from the "حفظ نهائي" button
    or `MainWindow.Closing`) and `AcknowledgeLateAsync` (past unsigned days, from the startup catch-up
    dialog, one password covering every listed date at once) both just insert the same shape of row.
  - **`GetUnsignedPastDatesAsync(today)` takes `today` as a parameter, not `DateTime.Today`** — a
    wall-clock read inside a Business method makes it untestable with a fixed date. `App.OnStartup`
    passes real `DateTime.Today`;
    `DailyOperationsSignOffServiceTests` passes `TestDatabase.Today`.
  - **One-time automatic cutover seed**: the very first call to `GetUnsignedPastDatesAsync` on a table
    that has never had a row (fresh migration on a customer DB with years of pre-feature history)
    inserts a sign-off row for `today.AddDays(-1)` with no activity-log entry (it's bookkeeping, not a
    real approval) so the feature's very first day never has to explain away years of unsigned
    history. Every date strictly after that seed is fair game for the catch-up dialog.
  - **`MainWindow.Closing` must stay 100% synchronous and always cancel first.** WPF keeps the window
    in its internal "closing" state until the handler returns, *even when `e.Cancel = true` was set* —
    so an `await` inside it hands control back to WPF mid-close, and the next `ShowDialog` throws
    `Cannot set Visibility to Visible or call Show, ShowDialog, Close, or
    WindowInteropHelper.EnsureHandle while a Window is closing`. That is exactly what shipped first and
    it silently let the app close with no prompt at all. The handler now cancels synchronously and
    defers the whole async flow (the "is it covered?" query, the password prompt, the review dialog) to
    `Dispatcher.BeginInvoke(..., DispatcherPriority.Background)`; on success it sets `_closeConfirmed`
    and calls `Close()` again, which the guard lets through. **Do not "optimise" this by caching the
    signed-off state to make the check synchronous** — that was tried, and it goes stale the moment any
    Tier B action writes to the log, which is precisely when the guard must fire.
  - **App close is blocked until today is signed off** (`MainWindow.Closing`) and **startup blocks on
    any unsigned past date** (`App.EnsureLateSignOffsAcknowledgedAsync`, called right after login and
    — unlike every other startup check around it — **not** wrapped in a swallow-all `try/catch`: this
    one is a security condition, not cleanup, so a failure here must stop startup, not get logged and
    ignored). Both dialogs share one code path, `MainWindow.RunFinalSaveFlowAsync`, so the button, the
    close-guard, and (via `LateSignOffCatchUpDialog`'s own confirm callback) the startup catch-up all
    ask the same way and write the same kind of row. `LateSignOffCatchUpDialog` has no working close
    button — `Window_Closing` cancels unconditionally until acknowledgement succeeds — because unlike
    every other dialog in the app, walking away from this one without answering isn't a valid choice.
- **Department accounts** (`DepartmentAttendanceService`, `Worker.HourlyRole` values `DepartmentManager`/
  `DepartmentHead`, the `DepartmentAccounts*` views): a manager/department-head login that is paid a full
  daily wage automatically, every day, with zero manual action. `WorkerRepository.GetDepartmentAccountsAsync`
  is their one query source; `GetActiveWithSkillsAsync`/`GetAllWithSkillsAsync` exclude them outright so
  they never appear in the workers screen, reports, or production flow, and `WeeklySummaryService`/
  `WorkerRecognitionRules` separately exclude them from team averages and "best worker" eligibility — two
  independent guards, checked independently, both correct.
  `EnsureDailyPresenceAsync` (`App.OnStartup`, same "no scheduled-job system" reasoning as the recognition
  titles below) backfills any day since account creation with no attendance/hourly row yet. **It used to
  re-scan every day from account creation to today, on every single startup, forever** — an account a year
  old cost 700+ sequential awaited queries on every launch, and the number only grows with account age
  (the same shape of bug `PendingWorkService` already had, except here the fix can't be "add an index"
  since it's round-trip count, not per-query cost, that's the problem). Fixed by asking each account's own
  data where it left off (`IHourlyWorkLogRepository`/`IAttendanceRepository.GetLastDateForWorkerAsync`,
  one indexed point query each) and only looping the actual gap — normally zero or one day. **Deliberately
  not a single shared cursor** (the first attempt used one, persisted in `AppSettingsStore`, mirroring
  `WorkerRecognitionService` below): a global "we've covered up to day X" cursor is wrong the moment a
  *second* account exists whose own last-known day is earlier than the first account's progress — the
  cursor has no way to know a newer account still needs its own gap filled. A test written specifically to
  catch this (`EnsureDailyPresenceAsync_ANewerAccountIsNotBlockedByAnOlderAccountsProgress`) is what found
  the shared-cursor design was wrong before it shipped. Per-account queries have no such failure mode:
  each account's start point depends only on its own rows.
  **`CorrectDayAsync`** (manual "fix one day" from the account's profile screen — attendance status,
  overtime hour) is the only way to change a department account's pay-bearing day, and **it used to write
  nothing to the activity log at all** — the one write path in the whole app that didn't, discovered
  during an audit pass and confirmed with a real test, not just by reading the code: sign a day off, call
  `CorrectDayAsync` for that date, and `DailyOperationsSignOffService.IsFullySignedOffAsync` still came
  back `true`, because that check reads the activity log to decide "did anything happen after the
  signature" and this path left no trace there. Fixed by logging
  `ActivityEventType.DepartmentAccountDayCorrected` (long-lived retention, grouped as money in the log
  screen, same reasoning as `WorkerWageChanged`) on every call — now behaves like every other attendance/
  wage edit in the app and correctly re-arms the sign-off guard.
- **Worker recognition titles** (`WorkerRecognitionService`/`WorkerRecognitionRules`,
  `WorkerPerformanceTitle`) are the official, permanently-recorded "أحسن عامل" awards — distinct from
  `WorkerWeeklySummaryDto.IsBestWorkerOfWeek`, which is recomputed live every time the screen opens and
  never stored. `AwardTitlesForClosedPeriodsAsync` runs once per startup (same "no scheduled-job system"
  reasoning as the department-account backfill above) and awards a title for each week/month that has
  **actually closed** since the last time it ran, using a persisted cursor
  (`AppSettingsStore.LastBestWorkerWeekComputedFor`/`LastBestWorkerMonthComputedFor`) capped at 8 weeks /
  3 months of catch-up so a long-idle install doesn't do a heavy recompute burst. This cursor design is
  safe here (unlike the department-account case above) because weeks and months are the same calendar
  periods for every worker — there's no per-entity "own creation date" that a shared cursor could skip.
  Both `WeeklySummaryService` and `WorkerRecognitionRules` independently exclude department accounts and
  hourly workers from eligibility (see above), so a manager account can never win a production title.
  **A department head's self-edit ("تعديل بياناتي") only locked the role field, not the wage field, even
  though the class's own doc comment named both** ("المسمّى وسعر اليومية مقفولين... تغيير سعر يوميته =
  زيادة راتب لنفسه" — role and wage are locked, changing your own wage rate is a self-raise).
  `DepartmentAccountEditDialog`'s constructor set `RoleBox.IsEnabled = !restrictToSelf` but never touched
  `WageBox.IsEnabled` — a head editing their own profile could freely retype their own daily wage, and
  `DepartmentAccountsViewModel.EditAccountAsync` passed it straight to `WorkerManagementService.
  UpdateWorkerAsync` with no operations-password argument at all. Whether that actually reached the
  database depended entirely on whether the factory had configured an operations password:
  `OperationsPasswordService.VerifyAsync` returns `IsAllowed = true` when none is configured (a
  deliberate, documented trade-off — "البوابة مفتوحة... قفلها فجأة كان هيوقف المصنع" — so pre-existing
  installs aren't locked out by a feature added later), which is also the exact state the Settings screen
  itself warns about ("⚠ مش متسجّلة — أي حد يقعد على الجهاز يقدر... يعدّل الأجور من غير أي تأكيد"). So an
  uninitialized/no-ops-password install let a head silently set their own pay; a configured one refused
  the save with a confusing generic error instead of ever disabling the field. Fixed by disabling
  `WageBox` the same way `RoleBox` already was, **plus** a ViewModel-level clamp
  (`restrictFields ? row.DailyWageEgp/Role : dialog.DailyWageEgp/Role`) so the persisted value never
  depends on the dialog's returned value alone for a self-edit — matching the "re-check even though the
  button is already hidden" defense-in-depth already used everywhere else in this same ViewModel. Neither
  field had any test coverage before this (`WorkforceManager.UiTests` had zero references to this dialog);
  `DepartmentAccountEditDialogTests` now asserts both fields lock together for a self-edit and both stay
  editable for an admin edit.
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
- **`PerformanceEvaluationService` no longer exists.** It ranked each worker against the day's team
  average and fed exactly one screen (the deleted "تقييم اليوم" tab) plus `NeedsAttentionService`; when
  both went, nothing called it and nothing tested it, so it went too rather than sitting as dead code
  with a DI registration. `StageBreakdownDto` was the one piece worth keeping (the weekly summary uses
  it) and now lives in its own file. Ranking a worker against the team on a single day is available in
  the report builder — as a report you can also export.
- `PayrollService.GetPeriodPayrollAsync(from, to)`: custom-period (e.g. monthly) wage sheet. Aggregates
  ALL days in the range directly (not whole weeks): produced + hourly workdays − absence/penalty
  deductions, × current wage = workdays-wage, then **+ bonuses − advances (EGP)** = net wage. Surfaced in
  `ReportBuilderView` as the "كشف الأسبوع" / "كشف أجور الشهر" built-in wage templates (see the report
  engine above) and as the source of the payslip strips (`PayslipStripExcelService`, see below) — there is
  no separate "كشف الأجور" screen or `WeeklyReportExcelService` any more, both were absorbed into the
  report builder. Weekly wage also shows in the weekly sheet (`NetWageEgp` column + totals row in Excel)
  and per-week in the worker profile. (NOTE: weekly sheet wage does NOT include EGP adjustments —
  advances/bonuses only flow through the period payroll + worker report + payslip strips.)
- `WageAdjustmentService.RecordAdjustmentAsync/RemoveAdjustmentAsync`: add/hard-delete an advance (سلفة) or
  bonus (حافز) in EGP for a worker on a date. Surfaced in DailyEntryView's "السلف والحوافز" tab (same
  attendance-row worker picker as penalties; سلفة shown red, حافز green). Both `PayrollService` and
  `ProductionReportService.GetWorkerReportAsync` fold these into net wage; the worker report shows the full
  breakdown line (أجر اليوميات + حوافز − سلف = الأجر النهائي), and the same breakdown prints on the payslip
  strips (see `PayslipStripExcelService` in the report engine section — there is no single-worker payslip
  window any more, it was removed outright).
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
- **`SensitiveActionDialog.Ask` takes a required `SensitiveActionKind` (Delete / Save)** — no default,
  on purpose. The dialog shipped hard-wired for deletion: a red header and an "أكّد الحذف" button on
  *every* gated operation, so saving a production run asked the user to confirm a **delete**. The kind
  drives the header colour, the button text and style, the reason label, and the error text. A wrong
  default here is worse than a compile error, which is why there isn't one. `AskConfirm` is the sibling
  entry point for Tier B actions (see daily operations sign-off above) — same window, same `kind`-driven
  styling, just the password box and "not configured" hint both collapsed.
- **`MessageBox.Show` is banned — every message goes through `Notify`, which renders `MessageDialog`.**
  The plain Win32 box was a white rectangle with a system question-mark icon and English "Yes"/"No"
  buttons in an app that is otherwise fully Arabic, RTL and gold-themed. `Notify` was already the only
  caller of `MessageBox` in the whole codebase, so the swap touched one file and **no call site**, which
  is also why none of them could drift semantically. `MessageDialog` is deliberately *not* a fourth mode
  of `SensitiveActionDialog`: that window's whole body is the password and reason inputs, it returns
  `SensitiveActionInput?`, and its buttons say "أكّد الحذف"/"أكّد واحفظ" — a Yes/No question would hide
  every part of it and reinterpret `null` as "No". They share the chrome (radius, shadow, draggable
  header), not the code. To add a call site, call `Notify.Ask` / `AskDangerous` / `Error` — never
  construct a dialog. `MessageKind` has no default for the same reason `SensitiveActionKind` doesn't.
  Two things the swap had to get right that a pure restyle would have missed: `MessageBox.Show` works
  from **any thread** while a custom `Window` does not (`Notify.Error` is called from `catch` blocks in
  background work, so `ShowCore` marshals through `Application.Current.Dispatcher`), and `Owner` throws
  if the main window has not been shown yet — messages like "the program is already running" fire before
  that, so the dialog falls back to `CenterScreen`.
- **A coloured dialog header uses the tint/ink *pair*, never the solid severity colour.** `DangerBrush`
  is `#A0342A` (dark) in the light theme and `#E08A6E` (light) in the dark one — the severity colours
  **invert**, while `SidebarInkBrush` stays light in both. So a solid `DangerBrush` header with
  `SidebarInkBrush` text is light-on-light in dark mode. `MessageDialog` pairs `DangerBgBrush`+
  `DangerBrush`, `WarnBgBrush`+`WarnBrush`, `InfoTintBrush`+`InfoBrush`, `GoldTintBrush`+`GoldDeepBrush`
  — each pair inverts together, so contrast holds in both themes. `MessageAppearance` returns **resource
  key strings** (same as `ToastHost`) so `SetResourceReference` keeps the binding live across a theme
  swap, and so the mapping is unit-testable without WPF. Note `SensitiveActionDialog` still uses the
  solid-colour header and has the same dark-mode weakness — left alone deliberately, not overlooked.
- **A `{DynamicResource}`/`{StaticResource}` reference to a key that doesn't exist fails completely
  silently** — no exception, no XAML-load error, `XamlLoadTests` doesn't catch it. The property is simply
  left unset, so a `Border.Background` renders as fully transparent instead of whatever the author
  intended. Three racking-stage/tag-only badges (`DailyEntryView`, `ProductsView`) had used
  `InfoBgBrush` — a key that never existed anywhere in the palette; the real key is `InfoTintBrush`,
  following the same tint/ink pairing as the line above (`InfoTintBrush`+`InfoBrush`). Found by diffing
  every `{Static/DynamicResource ...}` key referenced across `Views/*.xaml` against every key actually
  declared in `App.xaml`/`Themes/*.xaml`, not by eye — a targeted render before/after confirmed the badge
  had **no background pill shape at all** before the fix (just floating text) and the correctly-tinted
  pill after. Re-run that key-diff after any bulk rename of a palette brush; nothing else will catch it.
- **Excel export runs on a background thread** (`ExcelExport.RunAsync` wraps the write in `Task.Run`).
  Every caller passes a lambda that does its work synchronously and returns `Task.CompletedTask`, so it
  used to execute on the UI thread — a year's report with 14k detail rows froze the window for 3.3
  seconds, and that number grows with the factory's history. Safe off-thread because each caller opens
  its own DI scope inside the lambda and works on already-materialised DTOs.
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
  Three rules here were each paid for by a real defect — don't undo them:
  - **The snapshot is `VACUUM INTO`, never `File.Copy`.** The database runs in **WAL mode**: writes sit
    in a `-wal` sidecar until a checkpoint. Copying only the `.db` while the app is open was measured
    on the shipped build producing a backup with **no tables at all** (46 workers in the real file,
    "no such table" in the "successful" backup). `VACUUM INTO` reads through SQLite so it includes the
    WAL. It falls back to `File.Copy` on any failure — an imperfect backup beats none.
  - **Dates in file names use `CultureInfo.InvariantCulture` explicitly.** On a Windows set to a Hijri
    calendar the name came out `workforce_1448-02-28.db`, the cleanup parsed it as Gregorian year 1448,
    and deleted the backup seconds after taking it — the user had zero backups and no way to know.
    `App.PinCulture()` now also fixes the whole app to ar-EG + Gregorian so no Windows setting can
    shift a date anywhere.
  - **`SqliteConnection.ClearAllPools()` only appears in `RestoreBackup`.** It is process-global: it
    closes pooled connections for *every* database in the process. With `VACUUM INTO` the backup paths
    no longer need it, and it was also the cause of a 1-in-20 flaky test (`TestDatabase.Dispose` called
    it while other tests were mid-query — use `ClearPool(connection)` for one database).
- **Creator credit**: `SettingsViewModel.AppCreditText` ("تصميم وتطوير: مهندس سالم صالح") and
  `AppReleaseDatesText` render at the bottom of `SettingsView`, directly under `AppVersionText` and above
  "مكان البيانات". One deliberately placed spot — not the splash screen, not the window title — chosen
  because Settings is visited often enough to be findable but not part of daily flow. Not user-editable or
  hideable; that was explicit. The release dates are **not read from file timestamps**: `PublishSingleFile`
  leaves `Assembly.Location` empty, so `FirstReleaseDate`/`LatestReleaseDate` are baked in at build time as
  `AssemblyMetadataAttribute`s from `Directory.Build.props` (`FirstReleaseDate` fixed at 2026-08-25;
  `LatestReleaseDate` sits next to `<Version>` so both are bumped in the same edit, on the same release).
  If the metadata is ever absent (an old build, or a stripped assembly) `AppReleaseDatesText` returns
  empty rather than a placeholder — a blank line is less wrong than a fabricated date.
- **Version numbering — the patch digit caps at 9.** When a bump would push the patch past 9, it carries
  into the minor digit instead of climbing indefinitely (`1.5.9` → next release is `1.6.0`, not `1.5.10`).
  Decided 2026-09-13 (retroactively re-numbered `1.5.14`/`1.5.15` to `1.6.4`/`1.6.5` on the spot) — purely
  cosmetic, no behavior depends on it, just keep applying the carry on every future bump.
- **Activity-log retention** (`ActivityLogService.PurgeExpiredAsync`, run once per startup from
  `App.OnStartup` **after** the backup, so anything it deletes is still in today's backup; its failure is
  swallowed — a cleanup is not a startup prerequisite). **Two windows, not one**, because this log has no
  routine noise: every one of its event types is either a deletion, a money movement, or a daily save.
  **Six event types existed with Arabic names and a retention policy but nothing ever wrote them** — the
  only `LogAsync` call in the whole app was in `SoftDeleteService`, so the log was a deletion log while
  the screen promised more. Every type now has exactly one place that writes it (`ActivityLogCoverageTests`
  is what keeps that true).
  `ActivityEventRetention` (Core) lists only the **short-lived** types — administrative deletions plus the
  routine daily saves (production / attendance / creations) — and everything else gets the
  long window **by default**, so
  a new event type added later can't silently inherit a 90-day life just because someone forgot to list
  it. `ActivityLogRetentionTests` asserts exactly that inversion. Defaults: 90 days for deletions, 365 for
  money + `OperationsPasswordChanged` (it's the gate protecting the money operations, so "who changed it"
  belongs to the same question). `ScrapDeleted` (28) landed long-lived to match `ScrapRecorded`, not with
  the other administrative deletions — it wasn't in the original six, it never had *any* writer before
  daily operations sign-off closed the gap. `DaySignedOff` (29) is long-lived for the same reason as
  `OperationsPasswordChanged`: it's an audit record of who vouched for a day, not routine noise. Both are editable in Settings; **0 means off, never "delete everything"**,
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
- **Initial balance editing/deletion/history** (`InitialBalanceService`): a range's **from/to stage locks
  permanently the moment it has any usage** (`InitialBalanceRangeMath.UsedQuantity(range, usages) > 0`,
  scrap withdrawals count, mid-range production rows don't — same rule as everywhere else in this
  feature). Only the still-unused portion of `PieceCount` can move: `UpdateRangeAsync`/`EditAsync` reject
  shrinking below the used floor, and `RemoveRangeAsync` refuses outright once a range has any usage —
  the range row is the anchor `InitialBalanceUsage.InitialBalanceRangeId` points at, so deleting it would
  orphan real wage history. `EditAsync` is the one method the "تعديل" screen calls: it takes the *desired
  end state* (name, notes, full range list where an existing range carries its `Id` and a new one
  doesn't) and reconciles adds/resizes/removals in a single `SaveChangesAsync` — one DB round trip is
  already atomic, so no explicit `IUnitOfWork` transaction is needed here (contrast
  `WithdrawToScrapAsync`, which needs one because it makes two separate save calls).
  **Deletion now follows the same hard-vs-soft split as every other entity** (`DeletionScopeService`
  pattern): zero usage → hard delete (the row and its cascaded ranges disappear); any usage → soft-close
  via the `IsDeleted` flag `InitialBalance` already carried (`SoftDeletableEntity`) — the existing global
  query filter (`!b.IsDeleted` in `AppDbContext`) is what makes the card vanish from every list and makes
  a later withdrawal attempt fail with "not found", with no extra code. This replaced the old rule, which
  refused deletion entirely once anything had been withdrawn — the opposite of what was needed, since a
  balance with real wage history attached is exactly the case a manager wants to close out.
  **History is derived, not stored**: `GetHistoryForProductAsync` filters the same query
  `GetForProductAsync` uses down to `Status == Completed` (`Status` was already computed from
  `UsedQuantity` vs `Quantity`, nothing new added), so a balance drained to zero by real withdrawals
  moves itself out of the active list into History automatically. A balance closed by manual deletion
  (soft or hard) **never** appears there, deleted-with-usage included — it's `IsDeleted`, and the global
  filter excludes it before the Completed check ever runs. `GetProductSummaryAsync` sums the **same set**
  `GetForProductAsync` shows (active only, Completed excluded) **on purpose** — an earlier version summed
  every balance ever created for the product (active + history) so the total wouldn't "shrink" when one
  completed, but that made the top progress bar show a bigger number than the sum of the cards actually
  visible underneath it, which read as a bug (a completed balance's original quantity kept inflating
  "إجمالي" long after it had moved to History). The summary now always equals what's on screen; a
  completed balance's lifetime numbers are visible on its own card inside History, not folded into the
  active total.
  **The "zero ranges = whole line" shortcut lives in the UI** (`DailyEntryViewModel.AddInitialBalanceAsync`),
  not in `InitialBalanceService.CreateAsync`. `CreateAsync`'s contract is "creates exactly the ranges it's
  given" — many existing tests (and the same class of caller `IInitialBalanceRepository` serves for
  automatic gap-balances from `ProductionFlowService`) rely on being able to create with zero ranges and
  add specific ones afterward. Auto-filling inside `CreateAsync` broke that contract for every such
  caller; computing the default (first active stage → last active stage, full quantity) once in the
  ViewModel right before the request is built keeps the Business-layer method's meaning unchanged and
  only affects the one screen where "the user typed a count and hit save with nothing else" is an actual
  user gesture.
  **"سجل الرصيد" (`GetHistoryAsync`) now records every worker/stage that touched a withdrawal, not just
  the range's exit stage.** It used to skip intermediate-stage rows entirely (only the exit-stage row got
  an `InitialBalanceUsage`), so a range spanning multiple stages showed only the last worker in its usage
  log even though every stage in between has a real, fully-paid `DailyProduction` row — a user-reported
  confusion ("why does only one worker show when several worked on this"). `WriteUsageRowsAsync` now
  writes an `InitialBalanceUsage` for **every** row in the withdrawal. This does **not** double-count the
  balance's remaining quantity: `InitialBalance.UsedQuantity` moved from a flat `Usages.Sum(u =>
  u.Quantity)` to summing `InitialBalanceRangeMath.UsedQuantity` per range (the same "scrap always counts,
  production only counts on the range's exit stage" rule the range-level remaining calc already used) —
  so the extra intermediate-stage rows exist purely for the history view and never affect
  `RemainingQuantity`/`Status`. **Trap this hit immediately**: `InitialBalance.UsedQuantity` now reads
  `Ranges`, so any query that loads `Usages` without also loading `Ranges` silently computes 0 used
  quantity — `DeleteAsync` had exactly this bug (missing `.Include(b => b.Ranges)`) until
  `DeletionScopeTests`-style coverage caught it turning a soft-close into an attempted hard-delete that
  crashed on the `Usages` FK. Any new query touching `InitialBalance.UsedQuantity`/`RemainingQuantity`/
  `Status` must include both `Ranges` and `Usages`, not just one.
  **"عرض العمال" lives inside "سجل الرصيد" (`InitialBalanceHistoryDialog`) as a view toggle, not a
  separate button on the card.** The dialog already lists every usage chronologically (one row per
  withdrawal/worker/stage, per the fix above); the toggle regroups the same rows by `(WorkerName,
  StageName)` — summed quantity, occurrence count, first→last date — so "who worked on which stage" reads
  as one line per pairing instead of scrolling every date. Scrap-withdrawal rows (`WorkerName` empty, no
  worker involved) are excluded from the grouped view on purpose — the question it answers is "who
  worked here", not "how much went to scrap".
  **Deleting the production that caused an auto-created gap balance now reconciles it, instead of leaving
  it orphaned.** `SyncStageGapBalancesAsync` creates a `Source == DailyProduction` balance from a snapshot
  of cumulative totals at save time, with no FK back to any specific `DailyProduction` row — so deleting
  the production that produced the "before" side of that gap (one record, or a whole day) used to leave
  the auto-balance sitting there forever, representing a gap that no longer exists. `ProductionFlowService.
  ReconcileAutoBalancesAsync(productId, date)` re-runs the exact same boundary-gap computation after the
  fact and shrinks/removes any matching auto-balance down to the now-real gap. Two invariants keep this
  safe: it never reduces a balance below its own `UsedQuantity` (a partially-withdrawn auto-balance keeps
  exactly its used floor, never disappears), and it only touches balances that still have their **original
  unedited shape** (`Source == DailyProduction`, exactly one range, `PieceCount == Quantity`) — anything a
  user added a range to or resized via `EditAsync` is left alone, since it's no longer "just" the
  auto-computed snapshot. Wired into `WorkdayCalculationService.DeleteProductionAsync`/
  `DeleteProductionDayAsync`, right after `ProductionStageOutputService.RemoveIfNowOrphanedAsync` — **and
  after an extra `SaveChangesAsync`**, because `RemoveIfNowOrphanedAsync` only marks the ledger row for
  deletion in the change tracker; `GetStageTotalsUpToAsync` queries the database directly, so without that
  intermediate save the reconciliation reads the pre-deletion totals and does nothing. `Undo`Edit/Delete
  paths deliberately don't call this (same as they don't call `RemoveIfNowOrphanedAsync` either) — undo
  restores a prior state rather than correcting one, so it was never expected to shrink a gap.
  Needed extending `IInitialBalanceRepository` with `GetOpenAutoBalancesAsync`/`Remove` (Data layer) and
  injecting `ProductionFlowService` into `WorkdayCalculationService`, rather than injecting
  `InitialBalanceService` there — `InitialBalanceService` already depends on `ProductionFlowService`, so
  routing through it would have been circular; the whole reason `IInitialBalanceRepository` exists (see
  its own doc comment) is to let `ProductionFlowService` touch initial balances without going through
  `InitialBalanceService`.
  **Sending an initial-balance range to scrap (`WithdrawToScrapAsync`) supports a user-chosen quantity, up
  to the range's remaining amount — same as `WithdrawAsync`.** This was a pre-existing method (found during
  a "search before assuming greenfield" check, not built from scratch) that already took an explicit
  `pieceCount`; an initial pass toward this feature briefly forced it to always consume the whole remaining
  amount, but the user reverted that after seeing it in practice — they specifically want to control how
  much goes to scrap in one entry, e.g. scrap 20 now and leave the rest open for either production or a
  later scrap entry. So the parameter stayed, validated the same way it always was (`> 0`, `<= remaining`).
  Everything else about the method was already correct by construction and needed no changing:
  `ProductionScrap` carries no worker column at all (verified in the model itself), so zero wage impact was
  never a risk; its `Date` was already a free field, not pinned to today; and the stage-attachment /
  no-double-count logic (`stageId` must equal `range.FromStageId`) was already covered by its own tests and
  stayed untouched.
  **A balance that reaches zero remaining via scrap (fully or through several scrap entries) lands in
  History exactly like one closed by production** — same `Status == Completed` rule, no separate bucket —
  but carries `InitialBalanceDto.HasScrapUsage` (derived: `Usages.Any(u => u.ProductionScrapId != null)`, no
  new column) purely so the card can show a "اتقفل بهالك" tag; it does not change who appears in History.
  **The UI entry point is a small dedicated confirmation dialog (`ScrapBalanceRangeDialog`: quantity +
  reason + note + date, defaulting the quantity field to the full remaining), not the general `ScrapDialog`**
  used for normal daily-flow scrap. `ScrapDialog` exists to let the user freely pick a product/stage/quantity;
  here the product/stage are already pinned by the range the user clicked "تحويل لهالك" on, so reusing that
  picker would just be two redundant, disabled-feeling fields — only the quantity genuinely needs to stay an
  open input. Both paths still end up at the same `ScrapService.RecordCoreAsync`/`ProductionScrap` — only the
  front door differs.

## Environment note

.NET 8 SDK was installed via winget but may not be in PATH for fresh shells; if `dotnet` isn't found in
PowerShell, prepend `$env:PATH = [System.Environment]::GetEnvironmentVariable("PATH","Machine") + ";" +
[System.Environment]::GetEnvironmentVariable("PATH","User")`.
