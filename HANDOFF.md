# Fitzobit Delivery Management — Handoff

> **For the next chat:** read this whole file first, then inspect the code it points to before changing anything.
> The app was rebuilt around a **new business model**. **Parts 1–14 are all pushed and live** (last commit `a872fe0`, 2026-11-10):
> Parts 1–9 (2026-10-08 … 10-31), 10 Pause for everyone, 11 No paused days limit, 12 Money page, 13 Money cost lines + history,
> 14 Fill missing prices, 15 Manager-only settings changes (`a33b796`, 2026-11-12) — see §00.
> **Start with §00.** The owner wants to be asked ("don't assume, ask doubts") — use multiple-choice questions, then show the plan.
> **Do not rebuild working functionality. Never run `git commit` / `git push`** (the user does that; read-only git is fine).

---

## 00. STATUS (2026-11-12) — Part 15 pushed and live (`a33b796`); new meals + prices entered on live

- **Part 15 (2026-11-12, pushed `a33b796`, live): only Manager accounts change settings.** Owner's answers (option questions):
  - Manager-only **changes**: Dropdown values (add/rename/reorder/activate/delete), Users (add, edit, reset password), Delivery area
    (service city), Renewal reminder settings, Pause for everyone (preview/add/remove). **Admins only view** them (tabs visible, no buttons;
    Customers button reads "Paused for everyone" and opens the list read-only). Audit log and System info: **both** roles. Money: Manager only
    (unchanged).
  - Code: new policy `Policies.Manage` (role Manager) replaces `Policies.Admin` (removed); GET endpoints of users, audit, system,
    dictionaries/{type}, festive-pauses are `Operate`. UI uses `isManager` (DictionariesTab, UsersTab, DeliveryAreaCard, RenewalSettingsCard,
    FestivePauseDialog `readOnly`, ViewDataPage, DeliveryMapPage, SettingsPage shows all tabs + System card to both).
  - Rules Claude added (told the owner): **while no active Manager exists, an Admin may add users** (first-run setup still creates an Admin, and
    the owner's local DB has only Admin `deepak` — otherwise nobody could ever create a Manager). `UserService`: "At least one active Manager"
    replaces the old "at least one active administrator" check.
  - Tests: `WebSecurityTests.Managers_operate_and_change_settings_while_admins_only_view_them`, deactivation test rewritten for Managers,
    `CustomerApiTests.Managers_manage_dropdown_values_in_settings`; helper `WebTestHost.SetupManagerAsync`. 233 passing. UI built.
    Browser check on scratch DB `fitzobit_ui` (dropped): Admin sees Dropdown values/Users/General read-only; Admin adding a 2nd Manager -> 403;
    Manager edits and adds meal types.
- **Done on live 2026-11-12 as `manager_fitzobit`** (owner signed in; Claude entered via the API), from the WhatsApp catalogue read
  2026-11-12 (new meal type ids 17, 18; Meal bowl = id 7, code MEAL-BOWL kept): add meal types **Protein Salad Bowl** and **Protein Punch Meal Box** (owner: price only for preference None;
  Veg and With Egg blank = not sold, staff pick None), rename **Meal bowl -> Standalone Protein Salad**; new price list (monthly/weekly/trail): Regular 3499/899/189,
  Medium 3999/999/189, Large 4599/1149/189, Standalone Protein Salad 2899/894/189, Protein Salad Bowl 3799/949/189,
  Protein Punch Meal Box 3799/949/189, Oats 3599/999/189, oats with whey protein 4299/1199/189, Detox 1199/399/189 (Detox weekly Rs 399 set by
  the owner 2026-11-12, not in the catalogue; Trail prices not in the catalogue either, owner kept them). Sold packages keep their price (owner: only new customers/plans get new prices).
- Website (E:\ClaudeWorkspace\FitzobitWebsite, separate repo): `config.js` menu updated to the same prices + the 2 new meals (no egg) +
  "Standalone Protein Salad"; pushed `fc00095`, live on fitzobit.netlify.app. Owner also wants **meal photos from the Fitzobit WhatsApp chat** on the website (to do).
- Parts 1-14 pushed and live (`a872fe0`).
- Live data (2026-11-10): 69 customers; live meal types Regular, Medium, Large, Meal bowl, Oats, Detox Drink, "oats with whey protein";
  preferences Veg / With Egg / None; frequencies Monthly 26, Weekly 6, Trail 1. Manager account `manager_fitzobit` exists (created by the
  owner); admin `sivasankari_fitzobit`. Money page price list on live: same price for every preference; Regular 2999/899/189,
  Medium 3499/999/189, Large 3999/1149/189, Meal bowl 2899/894/189 (= Protein Salad prices), Oats 3499/999/189, Detox Drink 1049/333/189,
  oats with whey protein 3849/1199/189 (monthly/weekly/trail). Costs: delivery Rs 30, helper Rs 25 per box, no extra cost lines yet;
  targets Monthly 30 %, Weekly 40 %, Trail 40 %.
- Claude ran "Fill missing prices" on live (2026-11-10, as `manager_fitzobit` in the owner's Chrome): 32 finished packages filled
  (30 Trail at Rs 189/box, 2 Meal bowl weekly at Rs 894); Oct-Nov earnings have 0 unpriced boxes.
- Owner doubted the "oats with whey protein" earnings (Nov: 20 boxes, Rs 2,961): verified correct - one customer, FITZ-000052 Azar, Monthly
  Rs 3,849 (26 Oct-26 Nov, paused 7 and 9 Nov) = 3849/26 x 20 days. Explained monthly boxes earn less per day than weekly.
- Told the owner: Detox Drink loses money with these costs (Nov: 68 boxes earned ~Rs 46 each vs Rs 55 delivery + helper = -Rs 1,609 left
  for food). Possible next request: a lower cost for detox, or per-meal-type costs (today all cost lines apply to every box).
- How Claude works on the live site: Claude in Chrome (owner's signed-in Chrome, Manager session), `fetch` on same-origin API with header
  `X-XSRF-TOKEN` from cookie `XSRF-TOKEN`; read-only unless the owner asks for a change. WhatsApp desktop catalogue: keyboard Tab works,
  mouse clicks do not reach its panel, Enter on a product row ADDS IT TO THE CART (never press it).

---

## 0. Current state (2026-11-11, after Part 14)

| Item | Value |
|---|---|
| Project root | `E:\ClaudeWorkspace\Fitzobit` (Windows 11; shells: PowerShell + Git Bash) |
| Git | GitHub **Deepak7725/Fitzobit** (private), branch `main`, in sync with origin up to `f82d3d8` (Daily Orders). Pushed: Parts 1–9 incl. pincode fix, logo, Google Maps link (`0966a70`), renewal reminders (`4f375e8`), same-day pause (`c30072f`), auto-Active on package save (`20cbeba`), no-cache start page (`3560a8a`), GitHub wake-up (`a1cff95`), Daily Orders (`f82d3d8`). Parts 10-12 pushed as `f6ac924`, Part 13 as `458fdaa`, Part 14 as `a872fe0`. Nothing uncommitted except HANDOFF.md. |
| **Live site** | **https://fitzobit.onrender.com** — Render free web service `fitzobit` (Docker, Singapore, auto-deploy from `main`, `render.yaml`). Runs Parts 1–14 (until 2026-10-26 a tab kept old screens after a deploy; fixed by `Infrastructure/StaticFileCaching.cs`: index.html `no-store`, /assets/* (hashed) 1 year immutable, other files 1 hour). Service city **Thanjavur** (set by admin `sivasankari_fitzobit`, display name Siva Sankari). The owner cleared the old sample customers and enters real Thanjavur customers (FITZ-000001…). Frequencies there: Monthly 26, Weekly 6, **Trail** 1 (code `TRAIL`, added by the owner). |
| Live database | **Neon** free PostgreSQL, project `fitzobit` (`shy-surf-54281209`), Singapore, branch `production`, db `neondb`, role `neondb_owner`, direct (non-pooler) connection. On the next deploy the app migrates it automatically (data kept). |
| Render env vars | `Database__ConnectionString` (Neon, secret), `ForwardedHeaders__TrustAllProxies=true`, `Auth__SetupCode`. `Database__Provider` is unused (harmless, may be deleted). |
| Database | **PostgreSQL only** (no SQLite anywhere). Local DB `fitzobit` on the local PostgreSQL 18 service (user `postgres`). Connection string: **`src\Fitzobit.Web\appsettings.Local.json`** (git-ignored, Development only); the user secret `Database:ConnectionString` (UserSecretsId `fitzobit-web-local`) also works when saved by the user himself. Local DB `fitzobit` is migrated (all 4 migrations) and holds the owner's **test data**: 20 fake Coimbatore customers FITZ-000001…020 (from `samples/Add-SampleCustomers.ps1` data), all located (service city Coimbatore set), in 5 delivery groups, **25 packages** (random; 000003/007/011/015/019 have two), random delivery times 06:00–08:45, 29 random notes. Login: user **`deepak`** (Admin) — recreated by the owner on 2026-10-16 after he forgot the password (Claude deleted the old `Users` row at his request; nothing references `Users` by FK). Claude does not know the password. |
| Migrations | 12: Part 13 added `20261110105543_MoneyCostHistory` (MoneyCostVersions/Lines/ProfitTargets; copies the Money.* AppSettings into a first version valid from 2000-01-01, then deletes them; Down restores them). Part 12 added `20261110095915_MoneyModule` (PackagePrices, CustomerPackages.Price, seeded prices/costs/targets). Part 11 added `20261105060851_UnlimitedPauses` (check `PausedCount >= 0` + one-time recalculation of current packages, see §1 Part 11). Part 10 added `20261104164420_FestivePauses`. Part 8 added `20261026100639_RenewalReminders`. Part 7 added `20261021043225_GoogleMapUrl` (`Customers.GoogleMapUrl`, varchar 2000). Part 6 added `20261020074639_PastPackages` (`CustomerPackages.ArchivedAt` + index; no data change). Part 4 added `20261016094926_CustomerPackagesNotesAndDeliveryTime` ("start fresh": drops the old single package per customer — see §1 Part 4); Part 5 added `20261019063025_AlternatePhoneAndPausableFrequencies` (column `Customers.AlternatePhoneNumber`, `DictionaryEntries.CanPause`, sets it TRUE for frequency code MONTHLY only). `has-pending-model-changes` = none. |
| Run locally | The owner runs the app from **Visual Studio** (IIS Express profile → https://localhost:44363, or `http` → http://localhost:5080) and uses it in his own **Chrome**. `.\start.ps1` → http://localhost:5080 also works. After UI changes run `npm run build` in `src\Fitzobit.UI` (wwwroot is served from disk; he reloads with Ctrl+F5); after C# changes he restarts in VS. |
| Tests | **233 passing**, all on PostgreSQL (`FITZOBIT_TEST_PG` or the same user secret; every test creates/drops its own temp DB). Build: 0 code warnings (a full `--no-incremental` rebuild shows 2 analyzer warnings EF1002 in `DatabaseMigrationTests.cs` helpers that predate Part 10/11). UI: `tsc` clean (no ESLint configured). `dotnet publish -c Release` OK. |
| Docs | `README.md` (complete, current), `deploy/RENDER.md` (active hosting), `deploy/AZURE.md`, `deploy/TAILSCALE.md` (alternatives). |

### 0.1 What the user still has to do
1. (Done 2026-11-10) Parts 10-14 pushed; missing prices filled on the live site.
2. **12 pm wake-up = GitHub Actions** `.github/workflows/wake-up.yml` (11:40, 11:55, 12:05 IST) — pushed and tested green by the owner
   (2026-10-27). cron-job.org did NOT work (Render answered its servers with a large page and never started the app); owner told to
   disable that job.
3. (Done 2026-10-17: Parts 1–4 pushed, service city set live, admin created.) Delete `Auth__SetupCode` on Render if still there.
4. **Reset the Neon password** (it was visible in a screenshot in an earlier chat) and update `Database__ConnectionString` on Render.
5. Optional: `Geocoding__Nominatim__ContactEmail` on Render. (Keeping the server awake all day would also keep Neon awake — the workers query every minute — and could exceed Neon's free compute hours.)
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

### 0.1c Other things discussed (not code)
- Trademarks (2026-11-04): official IP India search (tmrsearch.ipindia.gov.in, needs the owner's email/mobile/OTP login and a CAPTCHA per
  search — the owner types those; Claude fills the fields and reads results). **FITZOBIT**: no conflicts in classes 43 (word + phonetic),
  30, 9, 35. **REKAMAN** (a software product): none in 9/42 word search, but phonetic class 42 found **"Recoman"** (Pracoss Technologies,
  registered, software/IT services, valid to 2029) — likely objection; advised a trademark agent. Domains (2026-11-04): fitzobit.com,
  fitzobit.in, rekaman.in were free; rekaman.com taken (since Feb 2024). Not legal advice.
- WhatsApp reminders are sent from whichever WhatsApp account is logged in where "Send reminder" is clicked (suggested a WhatsApp Business
  number linked via WhatsApp Web); no app-side limit, but WhatsApp may restrict spammy numbers.

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

### Part 5 (done 2026-10-19, pushed `3d84344`) — alternative phone, typed end dates, notes column, new pause rules
Decisions asked from the owner (do not change without asking):
- **Alternative phone** (optional): same 10-digit rule as the main number, must differ from it, may be shared by other customers;
  shown in Add/Edit customer, the customer panel, a Customers table column and exports. **Not** searchable.
- **End date can be typed** in the package Edit dialog: changing start/frequency still recalculates (with confirmation); then the
  end date may be overridden with any Monday–Saturday date on/after the start (`UpdatePackageRequest.EndDate`; null or equal to the
  calculated date keeps the calculation). Later pauses extend from the saved end date. New packages: always calculated.
- **Notes column** on the Customers screen: every note, newest first, with date and writer; also an export column `notes`
  ("19 Oct 2026, Siva Sankari: text; …").
- **Pauses** (3-day limit replaced by Part 11: every paused day extends): a per-frequency switch `DictionaryEntry.CanPause` (Settings → Dropdown values → Frequencies; Monthly on, others and new
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

### Part 14 (2026-11-10, pushed and run on live) - Fill missing package prices
Finished (past) packages cannot be edited, so packages sold before their box had a price (Trail, Meal bowl) could never get one. Owner set
Trail = Rs 189 for every box and asked to fill them. `MoneyService.FillMissingPricesAsync` (`POST api/money/prices/fill-missing`, Manager
only): every package with `Price` null, current and past, gets list price x quantity where the list has a price; returns filled / still
missing; one audit line without amounts. UI: button "Fill missing prices from the price list" + confirm in the unpriced warning. Tests:
`MoneyTests.Missing_prices_are_filled...`, web test (Admin 403). 233 tests passing.

### Part 13 (done 2026-11-10, pushed `458fdaa`) — Money: own cost lines, cost history, day total
Owner's answers (option questions, 2026-11-10):
- **Cost lines** the Manager adds by name ("Plate" Rs 10): always **per box, for all boxes**; Delivery + petrol and Helper stay as their own
  fixed fields; every line is shown separately in the breakdown, the day total, the earnings cards and tables (`EarningsDto.CostNames`,
  `MoneyLineDto.OtherCosts`). Max 30 lines, names unique (case-insensitive), <= 60 chars.
- **History for everything** (costs, lines, profit %): table `MoneyCostVersions` (unique `EffectiveFrom`) + `MoneyCostLines` + `MoneyProfitTargets`.
  Save has a **"Valid from" date (default today)**; same date = that version replaced; frequencies not sent keep the previous target. Each
  delivery day uses the latest version with EffectiveFrom <= day (before the first: the first). `GET api/money/settings?date=`.
  The card shows today's values and a one-line history ("from 09 Nov 2026 Rs 65.0/box · from the start Rs 55.0/box").
- **Day total** under the breakdown: for the selected box (meal type · preference) and a chosen day (default today): boxes, earned, each
  cost, target profit, left for food (`MoneyLineDto.Key` = "mealTypeId-preferenceId").
- **Unpriced packages** now listed by customer (link to `/data?q=CODE`) under the warning (`EarningsDto.UnpricedPackages`) — the owner asked
  what "1 box(es) belong to packages without a price" meant on the live site.
- Deleting a frequency also deletes its profit targets. Tests: `MoneyTests` history + migration-carry-over tests.
- Verified on scratch DB `fitzobit_ui` (dropped): added Plate Rs 10 valid from 9 Nov in the UI; history line; today/month tables with a
  Plate column; month view charged Plate only from 9 Nov; Medium Veg day total; unpriced Meal bowl package listed.

### Part 12 (done 2026-11-10, pushed `f6ac924`) — Money page (Manager accounts only)
Owner + Siva's request (phone call; owner's summary and answers to option questions, 2026-11-10):
- **Who:** only **Manager** role accounts see it (policy `Policies.Money` = role Manager; Admins get 403, no menu item, package prices are
  null in their API responses and their package edits ignore `Price`). Menu item "Money" (`/money`, `pages/MoneyPage.tsx`).
- **Costs per box** (AppSettings `Money.DeliveryCostPerBox` = 30 delivery person + petrol, `Money.HelperCostPerBox` = 25 helper) and **target
  profit % of the selling price** per frequency (`Money.ProfitTarget.<FrequencyCode>`: MONTHLY 30, WEEKLY 40, TRAIL 40; missing = 30).
- **Price list** `PackagePrices` (meal type x preference x frequency = package price for 1 box; unique; > 0; blank = not sold). Seeded from
  the live WhatsApp catalogue (2026-11-10), same price for every preference (owner: Egg = Veg): Regular/Medium/Large weekly 899/999/1149,
  monthly 2999/3499/3999; Oats (= Oats & Muesli) 999/3499; by name: "%whey%" meal type (owner's "Oats with whey protein" = Protein Oats
  Bowl) 1199/3849, "%detox%" (Detox drink) 333/1049. No Meal bowl, salads or Trail prices (owner fills them on the page).
  Deleting a dictionary value also deletes its prices.
- **Package price** `CustomerPackages.Price` (whole package, quantity included): list price x quantity when added (Add Customer, Add package);
  existing packages filled once by the migration; a Manager can change it in the package Edit dialog (price box follows the list when box/
  quantity/frequency change until typed; "Use list price"); others changing box/quantity/frequency get the list price. Price list changes
  never touch sold packages. Audit entries say a price changed but never the amount (Admins read the audit log).
- **Per-meal breakdown** (client, same formula as `MoneyService.Split`): price / deliveries; profit = that x target %; food budget = the rest
  after delivery + helper + profit (red when negative). Food cost is not entered (owner's choice: "show it as budget").
- **Earnings** `GET /api/money/earnings?from&to` (max 400 days): every delivery day a package delivers (covers the day, not paused; past
  packages included; customer status ignored) earns price / frequency deliveries and costs (delivery + helper) x quantity; totals, by box,
  by delivery group; boxes of unpriced packages counted separately. UI periods: Today / This week (Mon-Sat) / This month.
- API `api/money/settings` (GET/PUT), `api/money/prices` (GET/PUT), `api/money/earnings`. Tests: `MoneyTests` + web test (Manager 200, Admin 403).
- Verified on scratch DB `fitzobit_ui` (dropped) as a Manager: seeded prices/costs, breakdown (Regular Veg monthly leaves Rs 4.2 for food;
  weekly -Rs 0.1), earnings today/week matched hand calculation, price save kept the old price on a sold package, package dialog list-price
  follow (x2 = Rs 5,598); Admin got 403 and no prices.

### Part 11 (done 2026-11-05, not yet committed) — no paused days limit, paused count
Owner's decisions (asked with options, 2026-11-05):
- **No limit:** every own paused day moves the package's end date one delivery day (`CustomerPackage.MaxPauses` removed; check
  `CK_CustomerPackages_PausedCount` is now `>= 0`). The per-frequency **Can pause** switch stays (owner has it on for Monthly and Weekly).
  `PausedCount` = own paused days inside the package; re-dating counts every own pause inside the new period (no `Take(3)`).
- **Old pauses recalculated** by migration `20261105060851_UnlimitedPauses` (runs on the next deploy): for current packages only (not
  past/archived), each own pause inside the package with `NewEndDate = PreviousEndDate` now moves the end date one delivery day (Sunday
  skipped), in date order; the pause row gets old → new end; `PausedCount` recounted; one audit row per customer (`CustomerPackageUpdated`,
  "Package end date recalculated: … (13 Nov 2026 → 16 Nov 2026)", performer `system`). Down only restores the check (clamps to 3).
- **Festive fix (owner approved):** when an own pause moves an end date onto an upcoming day *paused for everyone*, that day is paused for
  the package too and the end date moves again (`CustomerPackageService.ApplyFestiveDays`, shared with add/re-date). Pause preview/result
  lists them (`PausedPackageDto.FestiveDays`, replaces `ExtendingPausesLeft`).
- **Paused count** with split, text from `CustomerPauseService.PausedDaysText` (TS `pausedDaysText` in `format.ts`): "4 (2 own + 2 for
  everyone)", "2 (own)", "1 (for everyone)", "0". Shown on the package card ("Paused"), in a new Customers column **Paused Days** (key
  `paused`, sortable, hideable; counts distinct paused dates of current packages; `CustomerListItemDto.PausedDays/OwnPausedDays`), export
  column `paused` ("Paused Days"), and Daily Orders (page line under the orders, Excel column 12 "Paused Days", PDF small line under Orders)
  counting the packages delivered that day. The old "· N paused" in the Packages cell was removed.
- Bulk pause ("Pause for everyone") already extended — unchanged. Manual end date already exists (package pencil → End date) — unchanged;
  typed end dates are still extended by later pauses (owner confirmed).
- All "first 3" texts removed (customer panel, Pause dialog, package dialog, Add Customer, Settings → Frequencies, Pause for everyone dialog,
  README). Tests: `PausedDaysTests` (festive fix, counts in list/export/Daily Orders, migration recalculation) + updated pause tests.
- Verified on scratch DB `fitzobit_ui` (dropped): Customers column + sort, package card "4 (2 own + 2 for everyone)", pause preview
  (12 Dec → Mon 14 Dec, "3 own paused day(s) in total"), Daily Orders lines, all four exports 200, no console errors.

### Part 10 (done 2026-11-04, not yet committed) — Pause for everyone (festive / emergency days)
Owner's decisions: Admin-only button "Pause for everyone" on the Customers page (`components/FestivePauseDialog.tsx`: calendar, server
preview, reason, list of festive days with Remove). `FestivePauseService` (Application/Customers), `api/festive-pauses` (GET list,
POST preview, POST, DELETE {id}); entity `FestivePause` (unique Date, Reason) + `PackagePause.FestivePauseId`; migration
`20261104164420_FestivePauses`; audit `FestivePauseApplied` / `FestivePauseRemoved`.
- Pauses **every package running that day, any frequency** (Weekly/Trail too), of **Active and Paused** customers (not Inactive); a day the
  customer already paused is left. Festive pauses **always** move the end date one delivery day and do **not** count towards `PausedCount`
  (the customer's own 3); own-pause recalculation ignores them.
- Packages added/created later, or re-dated (start/frequency change, calculated end date), get the upcoming festive days automatically
  (`CustomerPackageService.ApplyFestiveDaysAsync`); festive pauses outside a re-dated package are dropped. A typed end date is kept as typed.
- Removable only before the day: each affected end date moves back one delivery day and the pause rows are deleted; customers switched to
  Paused because of it become Active again when that day was the next one decided by the 12 pm rules.
- Bulk status change was offered and declined. Verified on scratch DB `fitzobit_ui` (dropped).

### Part 9 (done 2026-10-31, pushed `f82d3d8`) — Daily Orders page (kitchen totals + packing per group)
Owner's design: menu "Daily Orders" (`/orders`, Admin + Manager). `DailyOrdersService` (Application/DailyOrders), `GET /api/daily-orders[?date=]`,
`POST /api/daily-orders/export {date, format}`; writers `DailyOrdersExcelWriter` / `DailyOrdersPdfWriter` (Infrastructure/Exports).
- Day: opens on the next delivery day (Saturday → Monday), any date can be picked; Sundays = no deliveries.
- Counts **boxes** (quantity) of every package delivering that day: started, not finished, not paused that day, customer status **Active**
  (current status — tomorrow's list is final after the 12 pm run). Past (archived) packages count when looking back at their dates.
- Kitchen totals per **meal type × preference** (dictionary sort order) + a groups × meals table; per group (by number, "No group" last)
  the totals and customers in delivery-time order with all packages on one line, phone(s), address, Google Maps link, all notes.
- Excel: "Kitchen totals" sheet + one sheet per group. PDF (A4 landscape): totals page + each group on a new page. Exports audited.
- No migration. Verified on scratch DB `fitzobit_ui` (dropped): numbers, page, both exports, PDF pages rendered and checked.

### Part 8 (done 2026-10-26, pushed `4f375e8`) — renewal reminders
`RenewalReminderService` (Application/Customers), `GET /api/customers/renewals[?customerId=]`, `POST /api/customers/packages/{id}/renewal-reminder`,
`GET|PUT /api/settings/renewal` (PUT Admin); AppSettings `Renewal.DaysBefore`, `Renewal.Message`; migration `20261026100639_RenewalReminders`
(`CustomerPackages.RenewalReminderAt/By`); audit `RenewalReminderSent`. UI: `components/renewals.tsx` (list window, Send button = a real
`<a target=_blank>` to wa.me that also records), dashboard card "Renewals Due", panel badge on the package, `RenewalSettingsCard`.
Claude's choice: a package counts only once it has **started**. Design as agreed:
- **Renewal due** = a current package whose delivery days left (today included, paused days not counted) ≤ N; N set in Settings
  → General (1–30, default 3); all frequencies incl. Trail; not when the customer already has another package starting on/after
  that end date; Active and Paused customers only (not Inactive).
- **Dashboard** card "Renewals due (n)" → list window: customer, package, end date, days left, "Reminded <date> by <user>",
  Send reminder button, open customer. **Customer panel**: "Renewal due" badge + Send reminder + "Reminded … by …" on the package.
- **Send reminder** = free WhatsApp click-to-chat (`https://wa.me/91<main phone>?text=…`, opens WhatsApp/WhatsApp Web, the user
  presses Send) to the **main phone**; clicking records reminder date/time/user on the package (+ audit). No automatic sending
  (the WhatsApp Cloud API code exists but is not configured: needs Meta Business + templates, paid).
- **Settings → General → "Renewal reminders"**: days before (default 3) and an editable English message with `{name}`,
  `{package}`, `{endDate}`, `{daysLeft}`; default "Hi {name}, your Fitzobit {package} package ends on {endDate}. Would you like to
  renew? Just reply to this message. Thank you!"
- Not included (owner's choice): Customers-screen filter, Tamil text, choosing the alternative phone.
- Other ideas offered 2026-10-26, not chosen yet: tomorrow's delivery sheet per group + kitchen count, payments, delivered/missed
  tick-off, undo a pause, exact location from the Google Maps link, no Ctrl+F5 after deploys (Cache-Control on index.html),
  forgot password.

### Part 7 (done 2026-10-21, pushed `0966a70`) — optional Google Maps link
Owner's decisions: `Customer.GoogleMapUrl` (optional, ≤ 2000 chars) on Add/Edit customer (Address section), **only stored, shown and
exported** — it does not set coordinates. Accepted: http(s) google.TLD/maps…, maps.google.TLD/…, maps.app.goo.gl/…, goo.gl/maps/…
(`CustomerCommandService.IsGoogleMapUrl`, same check in `CustomerForm.tsx`). Shown in the customer panel ("Open in Google Maps", new tab)
and a "Google Map" table column ("Open map"). Export column `googleMap` (kind `ExportColumnKind.Url`): Excel hyperlink cell, PDF clickable
link printed in 36-character lines. Verified in the browser on scratch DB `fitzobit_ui` (dropped); the PDF look itself was only checked
by tests (link annotation present, 120-row layout).

### Part 6 (done 2026-10-20, pushed `c15add4`) — daily 12 pm rules (past packages, automatic statuses)
Owner's decisions (do not change without asking): `CustomerAutomationService` (Application) + `CustomerAutomationWorker` (Web, checks at
start-up and every 5 min, `Automation:Enabled`; off in WebTestHost). For each day D from 12 pm India time, looking at the next delivery
day N (Saturday → Monday):
1. Packages with EndDate ≤ D get `ArchivedAt` = **past packages**: kept read-only in the customer panel (collapsed "Past packages (n)"),
   left out of the Customers list/filters/summaries/start-end columns, exports, dashboard counts, group/map meal types and pauses;
   editing one → 409 ("Use Add package for a renewal"). Dictionary "in use" counts still include them.
2. A customer left with **no current package** (running or future) → **Inactive** (only then; a second package keeps them Active).
3. A package starting on N and status **Inactive or Paused** → **Active**.
4. Status **Active** and every package running on N is paused → **Paused**; status **Paused**, the last delivery day ≤ D fully paused and
   something delivers on N → **Active**. A Weekly/Trail package still delivering keeps the customer Active.
- Event-based: a status set by hand is left alone unless one of these fires. Customer edits do **not** trigger the rules.
- Saving a pause or adding/editing a package re-applies the latest due run to that customer (`ApplyNowAsync` = `RunDayAsync(DueDay())`):
  after 12 pm today's run (tomorrow paused → Paused at once); before 12 pm yesterday's run, which looks at today, so a pause for **today** saved
  in the morning → Paused at once (owner, 2026-10-26; FITZ-000040 case). A pause for tomorrow saved in the morning waits for 12 pm.
- Adding a package, or changing a package's dates (start/frequency/end), also makes an **Inactive or Paused** customer **Active** when
  their packages deliver on the next delivery day, even if the package started earlier (`ApplyNowAsync(packageSaved: true)`; owner,
  2026-10-26, Siva sankari case: package added after it started stayed Inactive). Other package edits (e.g. quantity) do not.
- Live runs were late (22 Oct 3:23 pm, 23 Oct 2:47 pm, 24+25 Oct only at 25 Oct 11:39 pm IST) although the owner says the cron-job.org
  ping is set up; check its settings/history (URL, 12:01, Asia/Kolkata). Days without changes leave no audit entry.
- Missed days run in order (AppSettings `Automation.LastRunDate`); the very first run only runs the due day (it still archives every
  package that already ended). Audit actions `CustomerPackageEnded`, `CustomerStatusChangedAutomatically` (summary ends "(12 pm run)",
  performer "system" in the background; shown in Recent customer changes).
- Pausing is now allowed for **Active and Paused** customers (Inactive still refused). Incomplete = never had a package (past ones count).
- Verified on scratch DB `fitzobit_ui` (dropped): worker ran on start; finished weekly package → past, Inactive → Active for a package
  starting tomorrow; pausing tomorrow after 12 pm → Paused at once; Past packages section read-only; Pause button enabled for Paused.

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

### Scratch-DB browser check (used for every part since Part 5)
The scratchpad is cleared between chats, so recreate this PowerShell script there and run it in the background (PowerShell tool):
```
$settings = Get-Content 'E:\ClaudeWorkspace\Fitzobit\src\Fitzobit.Web\appsettings.Local.json' -Raw | ConvertFrom-Json
$env:Database__ConnectionString = $settings.Database.ConnectionString -replace '(?i)Database=[^;]*', 'Database=fitzobit_ui'
$env:ASPNETCORE_ENVIRONMENT = 'Development'; $env:Geocoding__Provider = 'None'
Set-Location 'E:\ClaudeWorkspace\Fitzobit'
dotnet run --project src\Fitzobit.Web --no-launch-profile --urls http://localhost:5091 --artifacts-path E:\ClaudeWorkspace\Fitzobit\artifacts\uicheck
```
Wait for `http://localhost:5091/api/health`, open it in the built-in browser, do first-run setup from the page with a password generated in
the page (`crypto.randomUUID()+'Aa1!'`, never shown), create data with `fetch` + header `X-XSRF-TOKEN` (cookie `XSRF-TOKEN`). Clean up by
stopping the process (`Get-CimInstance Win32_Process`, CommandLine like `*Fitzobit.Web*` and `*5091*` -> `Stop-Process`) and
`dotnet ef database drop -f -p src\Fitzobit.Infrastructure -s src\Fitzobit.Infrastructure` with `FITZOBIT_DB_CONNECTION` set the same way.
Writing the postgres password into a pgpass file is blocked by the sandbox classifier: keep connection strings in memory only.
Browser pane gotcha: after a layout change, clicks by coordinate can hit the wrong calendar day; prefer refs (`find`) or check the selection.

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
First read E:\ClaudeWorkspace\Fitzobit\HANDOFF.md completely (section 00 = my next request and the questions
to ask me, section 0 = current state and open items, section 1 = what Parts 1-14 built, section 3 = how to
build/test/verify).
Inspect the existing code before changing anything. Do not rebuild working functionality.
Use PostgreSQL (local appsettings.Local.json / Neon). Do not run git commit or git push - I commit and push myself.
If anything is unclear, ask me with options instead of assuming.
Then help me with: <write your next request here>
```
