# Fitzobit Delivery Management — Handoff

> **For the next chat:** read this whole file first, then inspect the code it points to before changing anything.
> The app was rebuilt around a **new business model** (Parts 1–3 on 2026-10-08, Part 4 on 2026-10-16, pushed and live on 2026-10-17;
> Part 5 on 2026-10-19 is **not yet committed** — see §0). The user sends the next request in chat — continue from there.
> **Do not rebuild working functionality. Never run `git commit` / `git push`** (the user does that; read-only git is fine).

---

## 0. Current state (2026-10-19, after Part 5)

| Item | Value |
|---|---|
| Project root | `E:\ClaudeWorkspace\Fitzobit` (Windows 11; shells: PowerShell + Git Bash) |
| Git | GitHub **Deepak7725/Fitzobit** (private), branch `main`. Parts 1–4, the pincode-lookup fix and the logo/brand colours were pushed by the owner (2026-10-17). **Part 5 (§1) is uncommitted in the working tree.** |
| **Live site** | **https://fitzobit.onrender.com** — Render free web service `fitzobit` (Docker, Singapore, auto-deploy from `main`, `render.yaml`). Runs Parts 1–4 + logo. Service city **Thanjavur** (set by admin `sivasankari_fitzobit`, display name Siva Sankari). The owner cleared the old sample customers and enters real Thanjavur customers (FITZ-000001…). Frequencies there: Monthly 26, Weekly 6, **Trail** 1 (code `TRAIL`, added by the owner). |
| Live database | **Neon** free PostgreSQL, project `fitzobit` (`shy-surf-54281209`), Singapore, branch `production`, db `neondb`, role `neondb_owner`, direct (non-pooler) connection. On the next deploy the app migrates it automatically (data kept). |
| Render env vars | `Database__ConnectionString` (Neon, secret), `ForwardedHeaders__TrustAllProxies=true`, `Auth__SetupCode`. `Database__Provider` is unused (harmless, may be deleted). |
| Database | **PostgreSQL only** (no SQLite anywhere). Local DB `fitzobit` on the local PostgreSQL 18 service (user `postgres`). Connection string: **`src\Fitzobit.Web\appsettings.Local.json`** (git-ignored, Development only); the user secret `Database:ConnectionString` (UserSecretsId `fitzobit-web-local`) also works when saved by the user himself. Local DB `fitzobit` is migrated (all 4 migrations) and holds the owner's **test data**: 20 fake Coimbatore customers FITZ-000001…020 (from `samples/Add-SampleCustomers.ps1` data), all located (service city Coimbatore set), in 5 delivery groups, **25 packages** (random; 000003/007/011/015/019 have two), random delivery times 06:00–08:45, 29 random notes. Login: user **`deepak`** (Admin) — recreated by the owner on 2026-10-16 after he forgot the password (Claude deleted the old `Users` row at his request; nothing references `Users` by FK). Claude does not know the password. |
| Migrations | 5: Part 4 added `20261016094926_CustomerPackagesNotesAndDeliveryTime` ("start fresh": drops the old single package per customer — see §1 Part 4); Part 5 added `20261019063025_AlternatePhoneAndPausableFrequencies` (column `Customers.AlternatePhoneNumber`, `DictionaryEntries.CanPause`, sets it TRUE for frequency code MONTHLY only). `has-pending-model-changes` = none. |
| Run locally | The owner runs the app from **Visual Studio** (IIS Express profile → https://localhost:44363, or `http` → http://localhost:5080) and uses it in his own **Chrome**. `.\start.ps1` → http://localhost:5080 also works. After UI changes run `npm run build` in `src\Fitzobit.UI` (wwwroot is served from disk; he reloads with Ctrl+F5); after C# changes he restarts in VS. |
| Tests | **174 passing**, all on PostgreSQL (`FITZOBIT_TEST_PG` or the same user secret; every test creates/drops its own temp DB). Build: 0 warnings. UI: `tsc` clean (no ESLint configured). `dotnet publish -c Release` OK. |
| Docs | `README.md` (complete, current), `deploy/RENDER.md` (active hosting), `deploy/AZURE.md`, `deploy/TAILSCALE.md` (alternatives). |

### 0.1 What the user still has to do
1. **Commit + push Part 5.** On deploy Neon gets the new migration (Monthly can pause; Weekly and Trail cannot — the owner can
   change this per frequency in Settings → Dropdown values → Frequencies → Edit → "Can pause").
2. (Done 2026-10-17: Parts 1–4 pushed, service city set live, admin created.) Delete `Auth__SetupCode` on Render if still there.
4. **Reset the Neon password** (it was visible in a screenshot in an earlier chat) and update `Database__ConnectionString` on Render.
5. Optional: keep-awake pinger (cron-job.org → `/api/health` every 10 min, 07:00–22:00 IST); `Geocoding__Nominatim__ContactEmail` on Render.
6. After the first deploy, try one **PDF export** on the live site (the Linux/Docker font path could not be run locally — see §5).

### 0.1b Follow-ups done after Part 4 (2026-10-16/17)
- **Pincode lookup fix** (pushed): Nominatim was called with `postalcode=` **and** `city=`; with both it returns the city itself,
  which is rejected as too coarse, so no customer ever got a pincode location. Now only `postalcode` (+ the city viewbox).
- **Logo and brand colours** (pushed): `public/logo.png` (sign-in card), `logo-mark.png` (white tile in the sidebar),
  `favicon-32/64.png`, `apple-touch-icon.png` (built from the owner's 2000×2000 PNG; `favicon.svg` removed). Colours: primary
  green `#6f8a4f` (exact logo green, owner's choice despite 3.9:1 contrast), sidebar/sign-in olive `#313b29` (the `--navy-*`
  tokens keep their names), avatar orange `#cf4604` (`--accent`). Status colours, info boxes and map group colours unchanged.
- Thanjavur addresses: OpenStreetMap knows few small nagars; the app uses the **last address part before the city** as the area,
  so spelling matters (map spellings: "A V P Alagammal Nagar", "Melavastachavady", "Annai Sathya Nagar", "Vilar"). Otherwise the
  pincode area (approximate) is used.
- Test data added to the owner's local DB through the app's own API from his signed-in Chrome tab (Claude in Chrome, a new tab,
  `fetch` with header `X-XSRF-TOKEN` from cookie `XSRF-TOKEN`): random packages, delivery times and notes (see Database row).
- **Dialog fix:** the Change password dialog (opened from the user menu in the top bar) was cut off — the top bar has
  `backdrop-filter`, which makes `position: fixed` children relative to the bar. `Modal` in `components/feedback.tsx` now renders
  through `createPortal(…, document.body)`; z-order unchanged (modal 90 > drawer 85, toasts 100). UI type-checks and builds.
- Possible next items (not requested yet): a "forgot password"/admin reset for the only admin; a script or action to add
  packages to existing customers.

### 0.2 User preferences
- Edits only; the user commits/pushes. Keep changes minimal, incremental and **verified** (build + tests + browser check for UI).
- **Free** hosting/services only unless he asks. Never hard-code credentials; env vars / user secrets.
- Explain simply; click-by-click steps for anything he must do (he sends screenshots).
- **Ask, don't assume:** when a request is ambiguous, ask with options (he answered multi-question option lists well) and show the
  plan for confirmation before big changes; do not add rules/limits he did not ask for (or name them explicitly).
- He prefers that Claude does routine data entry for him in his signed-in Chrome (local app only) instead of typing it himself.
- He thinks visually. Delivery grouping = customers as points by longitude/latitude, split into natural areas like a
  matplotlib K-Means plot (not a fixed 3–4 clusters). **Part 3 clarification:** in Coimbatore, *RS Puram + Gandhipuram +
  Avinashi Road* and nearby form one group, *Sundarapuram + Kurichi + Eachanari* another — he wants to **see the area names**
  of each group. Implemented (§1, Part 3).

---

## 1. The business model now

Fitzobit delivers meals in **one city**, Monday–Saturday (**Sundays are never delivery days**). The old Excel import,
catalogue and plans are gone. Customers are entered in the app, located on a map, and split into **numbered delivery groups**.

### Part 1 (done) — new customer model, PostgreSQL, dropdowns, delivery dates, pauses
- Removed Import / Import History / Catalogue / Plans everywhere; SQLite removed. Old data preserved (see migrations).
- **Customer:** `CustomerId` (generated `FITZ-000001…` from PostgreSQL sequence `CustomerNumberSequence`; existing ids kept,
  generated numbers skip ids already taken), CustomerName, PhoneNumber (+Normalized), Address (+Normalized), Pincode,
  Locality/City (derived from address), PreferenceId/MealTypeId/FrequencyId/StatusId → `DictionaryEntries`, StartDate/EndDate
  (`date`), PausedCount (0–3, DB check), Latitude/Longitude (numeric 9,6), geocode fields, LegacyPlan, Created/Updated At/By, `xmin`.
- **DictionaryEntries** (type, stable Code, DisplayName, SortOrder, IsActive, IsSystem, NumericValue): Preference (Veg, With Egg,
  None), MealType (Regular, Medium, Large, Meal bowl, Oats), Frequency (Monthly=26, Weekly=6 delivery days — **seed values only**,
  read from `NumericValue` everywhere), CustomerStatus (Active/Inactive/Paused = system: renamable, not deactivatable).
  **Settings → Dropdown values** (Admin) has tabs Preferences / Meal Types / Frequencies / Customer Status: view, add, rename,
  delivery days (frequencies), reorder (↑/↓), activate/deactivate, delete only if unused. The UI never hard-codes dropdowns
  (`useDictionaries()` → `GET /api/dictionaries`, active values only; the server refuses inactive values for new choices).
- **Delivery dates:** one authoritative implementation, `IDeliveryCalendarService` (`Fitzobit.Domain/Deliveries`):
  IsDeliveryDay, NextDeliveryDay, CountDeliveryDays, CalculateEndDate(start, days, skipped), ExtendEndDateForPause.
  `DeliveryScheduleService.DeliveryDays(frequency)` reads `NumericValue`. Start date must be Mon–Sat. End date always
  server-calculated (`GET /api/customers/schedule`); changing start/frequency on edit requires `ConfirmScheduleChange`.
- **Pauses** (`CustomerPauseService`, table `CustomerPauses`): see §1 Part 3 for the complete rules.

### Part 2 (done) — numbered groups, geocoding, delivery map / classification
- **Groups:** `DeliveryGroups.GroupNumber` (unique, > 0), notes in Description. Create N groups at once (advisory lock), deactivate
  when empty, delete if never used. One active group per customer (filtered unique index). Every assign/move/remove →
  `CustomerDeliveryGroupAssignmentHistory` (prev, new, who, when, reason) + audit.
- **Geocoding:** `IGeocodingService` with `NominatimGeocodingService` (OpenStreetMap, default, free, ≤1 req/s), `GoogleGeocodingService`
  (`Geocoding__Google__ApiKey`), `DisabledGeocodingService` (`Provider=None`). `CustomerGeocodingService`: address → locality →
  pincode area (approximate); rejects city-only and out-of-area matches; never invents coordinates. Service city in `AppSettings`.
  `GeocodingWorker` (BackgroundService) processes Pending customers; retries 1m, 5m, 30m, 2h… up to 5 then Failed.
- **Classification map** (`/map`, `DeliveryMapPage` + canvas `ClusterPlot`): X = longitude, Y = latitude, coloured by group, red
  star per group centre, hulls, labels, zoom/pan, hover, box select, search, legend, unlocated list. Modes *Current groups* and
  *Suggest groups* (deterministic K-Means, Auto count, Balance, pincode placement) → draft → evaluate → save in one transaction.

### Part 3 (done 2026-10-08) — exports, settings, pause rules, dashboard, classification areas, polish
- **Exports (Excel + PDF)** — `CustomerExportService` (Application), writers in `Fitzobit.Infrastructure/Exports`:
  - Request `POST /api/customers/export` = `CustomerExportRequest { Scope: Filtered|Page|Selected|All, Format: Excel|Pdf,
    Filter, Ids, Columns, Page, PageSize }`. Rows are chosen **on the server** with `CustomerQueryService.ApplyFilter/ApplySort`
    (+ Skip/Take for Page) — never from the browser's loaded rows. Columns = keys from `CustomerExportColumns.All` (18 columns,
    same keys as the table); empty = all columns; unknown/duplicate keys → 409. Both formats get identical rows and columns.
  - UI: **Export** button / *Export selected* → `ExportDialog` (in `ViewDataPage.tsx`): Format, Rows (Filtered / This page /
    Selected / All ignoring filters), Columns (**Visible columns** default = table's visible columns in order, or All columns).
    Last format remembered in localStorage.
  - Excel (`CustomerExcelWriter`, ClosedXML): only chosen columns, header style, autofilter, frozen row, widths, text format `@`
    for ID/phone/pincode, real dates `dd mmm yyyy`, coordinates `0.000000`, separate *Export info* sheet (filters, columns, user, time).
  - PDF (`CustomerPdfWriter`, **PDFsharp-MigraDoc 6.2.4**, MIT): A4, heading (scope, count, columns, filters, generated by/at),
    header row repeated on each page (`HeadingFormat`), striped rows, footer "Generated … · Page X of Y". Widths measured with
    XGraphics; portrait unless needed; font 9 → 8 → 7.2 → 6.5 pt; names/addresses wrap, IDs/dates never break (NBSP).
    Fonts: `PdfFonts` resolver — Arial/Segoe UI (Windows), Liberation/DejaVu (Linux; Dockerfile installs `fonts-liberation`),
    else **embedded Segoe WP from PDFsharp** (`PdfSharp.WPFonts`), so it never fails for lack of a font.
  - Limits: `Export:MaxRows` 100,000 (Excel), `Export:PdfMaxRows` 2,000 (2,000 rows × 18 columns ≈ 2 s locally; 5,000 took 9 s).
  - Audit `ExportPerformed` with format, scope, rows, columns, filters. Example files: `samples/exports/`.
- **Columns:** any column can be hidden (at least one stays visible; Customer ID no longer forced). *Reset columns* = standard set
  (`DEFAULT_HIDDEN`), *Show all columns*. Table labels = export headers (Customer ID, Customer Name, Phone Number, Group Number…).
- **Pause rules completed:** only status **Active** may pause (Inactive/Paused → 409 with a clear message; the drawer's Pause button
  is disabled with the reason). Max 3, not Sunday, today or later, inside the package, not twice the same day. Single SaveChanges
  (= one transaction) + `xmin` row version + unique (CustomerId, PausedDate) + check 0–3 → no double counting. When start date or
  frequency changes, `PausedCount` = pauses inside the new period (renewal → 0; > 3 refused).
- **Status:** always by stable dictionary code (`CustomerStatusCodes` ACTIVE/INACTIVE/PAUSED), names from the dictionary —
  used in list/filters/details/dashboard/classification scope/exports. No competing enum.
- **Dashboard** (`DashboardService`, `DashboardPage`): Total / Active / Paused / Inactive, Delivery Groups, Unassigned (+ how many
  active), With / Without coordinates (approximate noted), one card per frequency (Weekly/Monthly…), Packages ending ≤ 7 days;
  charts by group, meal type or status, pincode or city; **Recent customer changes** (last 8 customer audit events). Cards link
  to filtered customer lists. No Catalogue/Import metrics remain.
- **Navigation:** Dashboard · Customers (`/data`) · Add Customer · **Delivery Classification** (`/map`) · Delivery Groups · Settings.
  No Import / Import History / Catalogue / Plans links, routes or APIs (`/import` → Not found; `/api/imports|catalogue|plans` → 404).
- **Delivery Classification areas (user's request):** `MapGroupDto.Areas` (top localities with counts; locality from address, else
  "PIN …"; "RS Puram"/"R.S. Puram" merged) and `MapPointDto.Area`. Map draws a tag above each centre star:
  *Group 2 / RS Puram · Gandhipuram · Saibaba Colony*; legend "Groups and their areas"; summary chips: groups, on the map,
  **without coordinates**, **unassigned**.
- **Auto group count fixed:** silhouette is now judged on **distinct locations** and the **fewest groups within 0.05 of the best**
  win (`GeoClustering.ChooseGroupCount`, `SilhouetteTolerance`). Before, locality-level geocoding (many customers on one point)
  made "one group per locality" look perfect (22 test customers → 8 groups); now → 3 natural areas.
- **Security fix:** `Logging:LogLevel:System.Net.Http.HttpClient = Warning` — .NET 8 logs outgoing request URLs at Information,
  which would have written the Google API key and customer addresses into the Render logs.
- **UX polish:** export dialog with busy/disabled states and inline errors; phone layout (header buttons wrap — the Customers page
  scrolled sideways before; dashboard cards 2 per row); recent-change texts; settings Export tab rewritten.

### Part 4 (done 2026-10-16) — notes, preferred delivery time, several packages per customer
Decisions asked from the owner (do not change without asking): dated note history; exact delivery time per customer, required;
several packages running at the same time; package fields = preference, meal type, quantity, frequency, start date (+ calculated
end date) — no status per package; a pause applies to **all packages running that day** and is **blocked** if one has used its
3 pauses; packages can be edited and deleted (delete only before the first delivery day); notes: writer edits/deletes own, Admin
any; Customers table and exports **one row per customer** with a package summary; dashboard meal-type/frequency charts count
**packages**; Add Customer needs **at least one** package and can take a first note; existing data: **start fresh** (not converted).
- Domain: `CustomerPackage` (Quantity 1–20, PausedCount 0–3, `xmin`), `PackagePause` (unique package+date, cascade with its
  package), `CustomerNote` (≤ 2,000 chars, CreatedBy = user name); `Customer.PreferredDeliveryTime` (`time`). Removed from Customer:
  PreferenceId, MealTypeId, FrequencyId, StartDate, EndDate, PausedCount, Pauses (table `CustomerPauses` dropped).
- Application: `CustomerCommandService` (create with packages + note; update = customer fields only), `CustomerPackageService`
  (add/update/delete), `CustomerNoteService` (add/update/delete + `CanEdit`), `CustomerPauseService` (all running packages),
  `CustomerQueryService` (row aggregates: package count, earliest start, latest end; package filters = "any package"; packages
  loaded per page via `PackageSummariesAsync`), exports (columns `deliveryTime`, `packages`; Excel time cells), dashboard,
  group statistics and map tooltip use the packages; dictionary "in use" counts packages (customers for status).
- API: `POST /api/customers` (packages[], note, preferredDeliveryTime "HH:mm:ss"), `POST /api/customers/{id}/packages`,
  `PUT|DELETE /api/customers/packages/{id}`, `POST /api/customers/{id}/notes`, `PUT|DELETE /api/customers/notes/{id}`,
  `POST /api/customers/{id}/pauses` (no ExpectedUpdatedAt any more).
- UI: `CustomerForm.tsx` (`CustomerFormFields` with delivery time, `PackageFields`), `AddCustomerPage` (packages list + note),
  `CustomerDrawer` (package cards, `PackageDialog`, `NotesSection`, pause preview), Customers table columns Delivery Time and
  Packages (localStorage key bumped to `…hiddenColumns.v4`), dashboard wording, Settings "In use" column.
- Chosen by Claude, mentioned to the owner (change if asked): quantity range 1–20.
- Verified in the browser on scratch DB `fitzobit_pk` (dropped): Add Customer with 2 packages + first note → FITZ-000001, end
  dates Tue 17 Nov (Monthly from Mon 19 Oct) and Mon 26 Oct (Weekly ×2 from Tue 20 Oct); table shows 7:30 AM and both packages;
  pausing Tue 20 Oct paused both (→ 18 Nov / 27 Oct, 1 of 3 each); note add → edit ("edited … by") → delete; package quantity
  edit; delete of a not-yet-started package; edit without delivery time refused; dashboard counts; PDF export with the new
  columns; phone width without horizontal scroll; no console errors.

### Part 5 (done 2026-10-19, not yet committed) — alternative phone, typed end dates, notes column, new pause rules
Decisions asked from the owner (do not change without asking):
- **Alternative phone** (optional): same 10-digit rule as the main number, must differ from it, may be shared by other customers;
  shown in Add/Edit customer, the customer panel, a Customers table column and exports. **Not** searchable.
- **End date can be typed** in the package Edit dialog: changing start/frequency still recalculates (with confirmation); then the
  end date may be overridden with any Monday–Saturday date on/after the start (`UpdatePackageRequest.EndDate`; null or equal to the
  calculated date keeps the calculation). Later pauses extend from the saved end date. New packages: always calculated.
- **Notes column** on the Customers screen: every note, newest first, with date and writer; also an export column `notes`
  ("19 Oct 2026, Siva Sankari: text; …").
- **Pauses**: a per-frequency switch `DictionaryEntry.CanPause` (Settings → Dropdown values → Frequencies; Monthly on, others and new
  ones off). Only pausable packages are paused; others running that day are still delivered (`PauseResultDto.NotPaused`). Several
  days per request (`PauseCustomerRequest.Dates`), chosen on a calendar (`components/PauseCalendar.tsx`); `POST
  /api/customers/{id}/pauses/preview` returns the same result without saving. Per package, **all time**, the first
  `MaxPauses` = 3 paused days extend the end date; **unlimited** further days are recorded with `NewEndDate = PreviousEndDate`
  (`PackagePause.ExtendedEndDate`). `PausedCount` (0–3, DB check unchanged) = extending pauses; every paused day is in `Pauses`.
  A start-date change keeps the pauses; only the first 3 inside the new period extend it. Still: Active customers only, today or later,
  no Sundays, whole request refused if any day is invalid.
- Verified in the browser on scratch DB `fitzobit_ui` (dropped): table columns, alt phone validation (same number refused), pausing
  5 days at once (first 3 moved end 18 → 21 Nov, Weekly package still delivered), typed end date (Sunday refused, 25 Nov saved),
  Settings "Can pause" column, no console errors.

### Verified for real (2026-10-08, scratch DB `fitzobit_e2e`, since dropped)
First-run setup → dashboard (empty and filled) → service city Coimbatore (found via OpenStreetMap) → Add Customer through the form
(FITZ-000001 generated, Weekly from Mon 12 Oct → end Sat 17 Oct, all dropdowns from DB) → geocoded (RS Puram, Located (area)) →
24 more customers (24/25 located; "Kurichi Pirivu" not found → no coordinates, listed as unlocated) → pause Sat 17 → end Mon 19;
3 pauses → 4th refused; inactive customer pause refused → Auto suggestion: Group 1 Peelamedu, Group 2 RS Puram · Gandhipuram ·
Saibaba Colony…, Group 3 Sundarapuram · Eachanari · Podanur · Kurichi → saved (history "-→2 … Delivery groups reclassified") →
manual assign/move with history → Customers: Status Active + Group 2 + only 3 columns → Export dialog sent exactly those
filters/columns → Excel and PDF each 9 rows, 3 columns, matching SQL; also search, pincode, meal type, frequency, combined filters
and selected rows checked against SQL → Settings: added Frequency "Fortnightly" (12) → appeared in Add Customer, end date
12 Oct → 24 Oct; deactivated → gone from the form and refused by the server → old pages 404 → phone width: no horizontal scroll
on any page. Browser console: only the intentionally provoked 409/404s. Server log: no warnings/errors.

---

## 2. Architecture (modular monolith, .NET 8 + React)

```
src/Fitzobit.Domain          entities + pure rules: Customers, Dictionaries, DeliveryGroups, Deliveries (calendar),
                             Geography (GeoClustering K-Means/silhouette/balance, GroupBoundaries hull), Settings, Users, Auditing
src/Fitzobit.Application     use cases via IFitzobitDbContext: Customers (command/query/export + CustomerExportColumns/pause/
                             schedule/location classifier), Dictionaries, DeliveryGroups, Geocoding, Classification, Dashboard,
                             Users/Auth, Auditing, Messaging
src/Fitzobit.Infrastructure  FitzobitDbContext, EntityConfigurations, Migrations (PostgreSQL), DatabaseConfigurator,
                             SequenceCustomerIdGenerator, geocoding providers, Exports (CustomerExcelWriter, CustomerPdfWriter,
                             PdfFonts), hashing, WhatsApp client
src/Fitzobit.Web             ASP.NET Core host, thin controllers, auth/CSRF/headers, GeocodingWorker, serves wwwroot
src/Fitzobit.UI              React 18 + TS + Vite → src/Fitzobit.Web/wwwroot (git-ignored; Docker builds it)
tests/Fitzobit.Tests         xUnit, PostgreSQL integration + HTTP (WebApplicationFactory; geocoding disabled there)
```

Migrations (`src/Fitzobit.Infrastructure/Persistence/Migrations`): `20261008024903_InitialPostgreSql` (old schema = production
today) → `20261008092400_CustomerDictionariesAndDeliveryDates` (hand-written; old plan/import/catalogue tables moved to schema
**`legacy`**) → `20261008100654_DeliveryGroupNumbersAndGeocoding` (hand-written). Both have working Down migrations.
Schema checked in Part 3: FKs all RESTRICT (Customers → DictionaryEntries ×4; pauses, assignments, history → Customers/Groups);
unique: CustomerId, GroupNumber, (DictionaryType, Code), (CustomerId, PausedDate), one active assignment per customer; checks:
PausedCount 0–3, EndDate ≥ StartDate, lat/long ranges, GroupNumber > 0; lat/long numeric(9,6); dates `date`; timestamps timestamptz.

Conventions: primary constructors, records for DTOs, XML doc summaries, few comments. Enums as strings; DateTimes UTC;
`SaveChanges` stamps Created/Updated At/By. `BusinessRuleException` → 409, `NotFoundException` → 404, concurrency/unique
conflicts → 409, others → 500 with reference id. Audit via `IAuditLogger` in the same SaveChanges. Cookie auth (Admin/Manager,
policies `Admin`/`Operate`, fallback = authenticated), CSRF header `X-XSRF-TOKEN`, security headers (CSP frame-ancestors none),
data-protection keys in DB. `/api/health` anonymous.

Toolchain: .NET SDK 9 targeting net8.0, EF Core / Npgsql 8.0.11, ClosedXML 0.104.2, PDFsharp-MigraDoc 6.2.4, React 18, Vite 5,
lucide-react, Node 24 (Docker node:20).

### Configuration (env var form; secrets never in source)
`Database__ConnectionString` (secret) · `Database__MigrateOnStartup` · `Auth__SetupCode` (secret, first run) · `Auth__SessionHours` ·
`Auth__AllowRemoteSetup` · `ForwardedHeaders__TrustAllProxies` (Render) · `Geocoding__Provider` (Nominatim/Google/None) ·
`Geocoding__Nominatim__ContactEmail` · `Geocoding__Google__ApiKey` (secret) · `Export__MaxRows` / `Export__PdfMaxRows` /
`Export__TimeZone` · `WhatsApp__*` (token secret) · `FITZOBIT_TEST_PG` (tests) · `FITZOBIT_DB_CONNECTION` (`dotnet ef`).

---

## 3. How to work on the code

- Build: `dotnet build Fitzobit.sln`; UI: `cd src\Fitzobit.UI; npx tsc --noEmit -p .; npm run build`.
- Tests: `dotnet test tests\Fitzobit.Tests`. If VS/IIS Express locks `bin\Debug`, add
  `--artifacts-path E:\ClaudeWorkspace\Fitzobit\artifacts\testbuild`.
- New migration: `dotnet ef migrations add <Name> -p src\Fitzobit.Infrastructure -s src\Fitzobit.Infrastructure -o Persistence\Migrations`
  — **always review/hand-edit**: EF scaffolds renames/drops that lose data. Check `dotnet ef migrations has-pending-model-changes`.
- Verifying a migration on realistic old data: `git archive HEAD | tar -x -C <scratch>` (no repo changes), run it against a
  scratch DB, create data through its API, then run the new version on that DB.
- Scratch DB access with psql: the postgres password is in `src\Fitzobit.Web\appsettings.Local.json`. Parse it with a small script into a temporary pgpass
  in the scratchpad (`localhost:5432:*:postgres:<pw>`), set `PGPASSFILE`, never print it, delete it afterwards.
  psql: `"/c/Program Files/PostgreSQL/18/bin/psql.exe"`.
- Run a scratch instance: `Database__ConnectionString=…;Database=<scratch>` + `ASPNETCORE_ENVIRONMENT=Development`,
  `dotnet run --project src\Fitzobit.Web --no-launch-profile --urls http://localhost:5091` (background); `Geocoding__Provider=None`
  to avoid real lookups. Stop with PowerShell `Get-CimInstance Win32_Process … CommandLine -like '*Fitzobit.Web*'` → Stop-Process.
  The app serves `wwwroot` from disk, so `npm run build` is visible after a page reload (C# changes need a restart).
- E2E pattern used in Part 3: first-run setup with generated throwaway credentials kept in the scratchpad; bulk data via
  `fetch('/api/customers', …)` from the page with header `X-XSRF-TOKEN` (cookie `XSRF-TOKEN`); export files verified by a Python
  script (urllib login + openpyxl + PyMuPDF, both installed) against SQL; to avoid browser downloads, patch `window.fetch` and
  `HTMLAnchorElement.prototype.click` in the page to capture the request instead.
- Docker is not installed locally (no WSL either); `dotnet publish -c Release` reproduces the Docker build step.

### Gotchas
- Bash tool: heredocs with quotes/backticks (TSX, `''`) break → write a `.py` edit script to the scratchpad with the Write tool and
  run it (exact-match asserts). Some files had BOMs (read with `utf-8-sig`).
- `sleep N; cmd` chains are blocked → use `until <check>; do sleep 2; done` or a for-loop with curl.
- Browser pane: `resize_window` 1440×900 for screenshots, preset `mobile` for phone checks (reset to desktop afterwards);
  scroll needs a prior screenshot; iframes of the app are blocked by CSP (by design).
- Login form field order: *Your name*, then *User name*.
- Real OpenStreetMap lookups are slow by design (1/s); 25 customers ≈ 30 s. Indian addresses often match only at locality level
  → many customers share one point (that is why Auto judges distinct locations).
- PDFsharp's font resolver can be set only once per process (`PdfFonts.EnsureRegistered`).
- **User secrets written by Claude's sandbox are invisible to the user's own programs** (2026-10-08). The secrets file
  `%APPDATA%\Microsoft\UserSecrets\fitzobit-web-local\secrets.json` had been created by a sandboxed Claude session (its ACL
  carries the sandbox SID `S-1-15-3-…`); Claude's tools could read it, but Visual Studio's app saw "not found" →
  "Database:ConnectionString is not set". Fix: the user saves the secret himself (VS: right-click Fitzobit.Web → Manage User
  Secrets). **Never set user secrets or other machine-wide files from the sandbox; give the user the steps instead.**
  Claude's sandboxed process list also hides the user's processes (use `dangerouslyDisableSandbox` only for read-only checks).
  `Program.cs` now stops with the environment name and the secrets file path (+ found / not found) when the string is missing.
  Working setup now: **`src/Fitzobit.Web/appsettings.Local.json`** (git-ignored, `.dockerignore`d, `CopyToOutput/PublishDirectory=Never`)
  holds the local connection string; `Program.cs` loads it only in Development (from the content root = project folder).
  It is inserted **before** the environment-variable source, so `Database__ConnectionString` still wins (a first version added it
  last and silently pointed a scratch instance at the real local DB; caught and fixed the same day).
  2026-10-09: the fix inserted it before the FIRST environment source — that is the DOTNET_/ASPNETCORE_-prefixed host source,
  which comes before appsettings.json, whose empty "ConnectionString" then overrode the local file. Now inserted before the
  last *unprefixed* environment source. Verified outside the sandbox: local file only → connects; env var set → env var wins.
  Files in the project folder written by Claude ARE visible to Visual Studio (unlike the user profile).

---

## 4. Samples (fake people)
- `samples/Sample_Customers_Coimbatore_20.xlsx` — 20 customers in 20 real Coimbatore localities (old-app migration checks).
- `samples/Add-SampleCustomers.ps1` — adds the 20 fake Coimbatore customers through the running app's API (asks for user name and
  password; skips phones that already exist; `-BaseUrl https://localhost:44363` default = VS IIS Express, `http://localhost:5080`
  for start.ps1). Tested twice on a scratch DB: 20 added, then 20 skipped. Sulur (641402) lies outside the city and is expected to
  stay "Location unavailable".
- `samples/exports/` — example Excel/PDF exports from the Part 3 test run (Active + Group 2 with 3 columns; all 18 columns landscape).
- Other old-model Excel samples remain in `samples\`; `Fitzobit_WhatsApp_Catalogue.xlsx` in the root is old data, unused.

## 5. Known limitations
- Part 4 "start fresh": all existing package data is removed by the migration (owner's decision); packages must be re-entered.
- `samples/Add-SampleCustomers.ps1` creates new customers only (one package, 07:30); it does not complete customers that already
  exist without a package.
- Docker/Linux path not run locally: the `fonts-liberation` apt step and PDFsharp on Linux are untested here (the embedded
  Segoe WP fallback means PDF export still works if fonts are missing). Check one PDF export after deploying.
- PDF export capped at 2,000 rows (the free Render instance has 0.1 CPU); larger lists → Excel.
- Pauses cannot be undone (by design so far). Changing a frequency's delivery days affects end dates calculated from then on;
  stored end dates are kept.
- Geocoding precision depends on OpenStreetMap (often locality level in India); Google is more precise but needs a key + billing.
- Auto grouping is a classification aid (K-Means + silhouette heuristic), not a route optimiser; the manager reviews before saving.
- "Recent customer changes" links to the customer list search, not directly to the customer panel.
- Real browser file download was not clicked in the test (captured instead); the download code path is unchanged from Part 1.

## 6. Free-plan limits (told to the user)
- Render free: sleeps after 15 min (first visit 30–60 s), 750 h/month, 0.1 CPU, 512 MB, no disk.
- Neon free: 1 GB storage, 100 compute-h/month, sleeps after 5 min (wakes ~1 s), 6-hour restore window.

---

## 7. Prompt to paste into the new chat

```
Continue working on the Fitzobit Delivery Management app in E:\ClaudeWorkspace\Fitzobit.
First read E:\ClaudeWorkspace\Fitzobit\HANDOFF.md completely (section 0 = current state and open items,
section 1 = what Parts 1-4 of the new business model built, section 3 = how to build/test/verify).
Inspect the existing code before changing anything. Do not rebuild working functionality.
Use PostgreSQL (local appsettings.Local.json / Neon). Do not run git commit or git push - I commit and push myself.
If anything is unclear, ask me with options instead of assuming.
Then help me with: <write your next request here>
```
