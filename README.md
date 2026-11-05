# Fitzobit Delivery Management

Fitzobit Delivery Management keeps Fitzobit's meal-delivery customers: who they are, where they live, what they eat
(preference and meal type), their package (frequency, start and end date, paused days) and which **delivery group**
they belong to, with a searchable, filterable, exportable customer list. Data lives in **PostgreSQL**, locally and when hosted.

- **Backend:** ASP.NET Core 8, EF Core 8 with Npgsql (PostgreSQL), ClosedXML (Excel) and PDFsharp-MigraDoc (PDF, MIT licence) for exports
- **Frontend:** React 18 + TypeScript (Vite), served by the same host
- **Hosting:** Render (web) + Neon (PostgreSQL), both free; see [deploy/RENDER.md](deploy/RENDER.md)

---

## Contents

1. [Architecture](#architecture)
2. [Requirements](#requirements)
3. [Local PostgreSQL](#local-postgresql)
4. [Running the app](#running-the-app)
5. [Database and migrations](#database-and-migrations)
6. [Customers](#customers)
7. [Delivery days, end dates and pauses](#delivery-days-end-dates-and-pauses)
8. [Drop-down values (Settings)](#drop-down-values-settings)
9. [Customers list, filters and export](#customers-list-filters-and-export)
9a. [Dashboard](#dashboard)
10. [Delivery groups](#delivery-groups)
10a. [Customer locations (geocoding)](#customer-locations-geocoding)
10b. [Delivery Classification and group suggestions](#delivery-classification-and-group-suggestions)
11. [Authentication and first-run setup](#authentication-and-first-run-setup)
12. [Audit trail](#audit-trail)
13. [WhatsApp architecture](#whatsapp-architecture)
14. [Security](#security)
15. [Configuration and environment variables](#configuration-and-environment-variables)
16. [Testing](#testing)
17. [Deployment](#deployment)
18. [Troubleshooting](#troubleshooting)

---

## Architecture

A modular monolith with clean layering. Dependencies point inwards only.

| Project | Responsibility |
|---|---|
| `src/Fitzobit.Domain` | Entities and pure rules: customers and pauses, drop-down values (`DictionaryEntry`), delivery groups, users, audit, WhatsApp messages, and the delivery calendar (`IDeliveryCalendarService`). No EF Core dependencies. |
| `src/Fitzobit.Application` | Use cases: customers (create/edit/pause/query/export), delivery schedule, drop-down values, delivery groups, users and sign-in, audit, dashboard, WhatsApp status. Talks to the database only through `IFitzobitDbContext`. |
| `src/Fitzobit.Infrastructure` | `FitzobitDbContext`, entity configurations, PostgreSQL migrations, `DatabaseConfigurator`, customer-id sequence, Excel export (ClosedXML), PDF export (PDFsharp-MigraDoc), geocoding providers, password hashing, WhatsApp Cloud API client. |
| `src/Fitzobit.Web` | ASP.NET Core host: thin API controllers, cookie authentication, role policies, CSRF, security headers, safe error handling; serves the built UI from `wwwroot`. |
| `src/Fitzobit.UI` | React + TypeScript single-page app. `npm run build` writes to `src/Fitzobit.Web/wwwroot`. |
| `tests/Fitzobit.Tests` | xUnit: unit tests, PostgreSQL integration tests (real migrations) and HTTP tests (the real web host). |

Conventions: enums stored as text, all timestamps in UTC, dates without a time (start/end/pause) as PostgreSQL `date`;
every create/update is stamped with who/when; business errors become `409` responses with a readable message.

## Requirements

- .NET SDK 8 or later (the projects target `net8.0`)
- Node.js 18+ and npm (only to build the UI)
- PostgreSQL 14+ running locally (developed with PostgreSQL 18 on Windows)

## Local PostgreSQL

1. Install PostgreSQL (https://www.postgresql.org/download/windows/) and note the password of the `postgres` user.
2. Store the connection string as a **user secret** (kept in your Windows profile, never in the repository):

   ```powershell
   dotnet user-secrets set "Database:ConnectionString" "Host=localhost;Port=5432;Database=fitzobit;Username=postgres;Password=YOUR_PASSWORD" --project src\Fitzobit.Web
   ```

   Check it with `dotnet user-secrets list --project src\Fitzobit.Web`. Instead of a user secret you can set the
   environment variable `Database__ConnectionString`.
3. Start the app (below). The database `fitzobit` is created and migrated automatically on first start.

User secrets are read in the `Development` environment, which `start.ps1`, `dotnet run --launch-profile http` and
Visual Studio use. The tests use the same user secret (or `FITZOBIT_TEST_PG`), see [Testing](#testing).

## Running the app

```powershell
.\start.ps1             # builds the UI if needed, then serves app + API on http://localhost:5080
.\start.ps1 -RebuildUI  # force a fresh UI build after UI changes
```

Open **http://localhost:5080**. The first time, the app asks you to create the administrator account
(see [first-run setup](#authentication-and-first-run-setup)). On every start the app applies pending migrations.

UI development with hot reload:

```powershell
dotnet run --project src\Fitzobit.Web --launch-profile http   # API on :5080
cd src\Fitzobit.UI; npm install; npm run dev                 # UI on :5173, proxies /api to :5080
```

Build everything:

```powershell
dotnet build Fitzobit.sln
cd src\Fitzobit.UI; npm run build       # type-checks (tsc) and builds into src\Fitzobit.Web\wwwroot
```

## Database and migrations

Tables: `Customers`, `CustomerPackages`, `PackagePauses`, `CustomerNotes`, `DictionaryEntries`, `DeliveryGroups`, `CustomerDeliveryGroupAssignments`,
`CustomerDeliveryGroupAssignmentHistory`, `Users`, `AuditLogs`, `WhatsAppMessages`, `DataProtectionKeys`, plus the
sequence `CustomerNumberSequence`.

Rules enforced by the database as well as by the code:

- `Customers.CustomerId` and `Users.UserName` are unique; `DictionaryEntries` is unique per type and code.
- A customer has at most **one active delivery group** (filtered unique index `UX_CustomerDeliveryGroupAssignments_ActiveCustomer`).
- Per package: `PausedCount` ≥ 0, `Quantity` 1–20, end date never before the start date; one pause per package and day
  (unique index); coordinates are valid (check constraints).
- Drop-down values cannot be deleted while a package or customer uses them (foreign keys with `RESTRICT`).
- Concurrent saves of the same customer or package are detected through PostgreSQL's row version (`xmin`).

Migrations (`src/Fitzobit.Infrastructure/Persistence/Migrations`) are applied automatically at startup
(`Database:MigrateOnStartup`):

| Migration | Contents |
|---|---|
| `InitialPostgreSql` | The original schema (customers, plans, catalogue, imports, groups, users, audit, WhatsApp) |
| `CustomerDictionariesAndDeliveryDates` | Drop-down values, delivery dates, pauses (one package per customer), see below |
| `DeliveryGroupNumbersAndGeocoding` | Numbered groups, assignment history, geocoding fields |
| `CustomerPackagesNotesAndDeliveryTime` | Several packages per customer, dated notes, preferred delivery time (see below) |

`CustomerDictionariesAndDeliveryDates` is written by hand so no customer data is lost:

- `Location` becomes `Address` (renamed, not dropped); CustomerIDs, names, phones, pincodes, groups, history and audit are untouched.
- Drop-down values are seeded. Each existing customer's old plan is kept as text (`LegacyPlan`, shown as *Previous plan*)
  and, where unambiguous, mapped to the new values: *Veg/Egg Plan* tier → Preference, *Regular/Medium/Large Fit Box,
  Oats* → Meal type, monthly/weekly → Frequency. Status Active/Inactive is kept. Salads are not guessed; start dates
  did not exist before. **Dashboard → Incomplete details** lists the customers to complete.
- The retired Import, Catalogue and Plan tables move unchanged to the PostgreSQL schema **`legacy`**, together with
  `legacy."CustomerPlans"` (every customer's original plan, tier and import ids). The app no longer uses them; drop
  the schema (`DROP SCHEMA legacy CASCADE`) once you no longer need the history.
- Rolling back (`dotnet ef database update InitialPostgreSql`) restores the previous schema and plan links.

`CustomerPackagesNotesAndDeliveryTime` (2026-10-16) follows the owner's decision to **start fresh**: the old single package per
customer (preference, meal type, frequency, start/end date, paused count) and the old `CustomerPauses` table are dropped, not
converted. Customers, addresses, locations, groups, history and audit stay. Existing customers then have no package and no
delivery time and show as *Incomplete* until both are entered.

After changing the model:

```powershell
dotnet ef migrations add <Name> -p src\Fitzobit.Infrastructure -s src\Fitzobit.Infrastructure -o Persistence\Migrations
```

Creating a migration needs no database; `dotnet ef database update` uses the connection string in `FITZOBIT_DB_CONNECTION`.

## Customers

**Add Customer** (`/customers/new`, also on the Customers page and the dashboard):

| Field | Rules |
|---|---|
| Customer ID | Generated on save: `FITZ-000001`, `FITZ-000002`, … from a PostgreSQL sequence (safe for simultaneous saves; numbers already used by an existing customer are skipped). Existing customers keep their original IDs. Never editable. |
| Customer name | 2–150 characters, must contain letters |
| Phone number | 10-digit Indian mobile number (`+91`, `0`, spaces and dashes accepted); landlines and duplicates are warnings |
| Preferred delivery time | **Required.** One exact time per customer (e.g. 7:30 AM) for all their packages; stored to the minute |
| Status | From the database (Settings → Dropdown values); defaults to Active |
| Address, Pincode | Address required; pincode 6 digits (a different pincode inside the address is a warning) |
| Packages | **At least one.** *Add another package* adds more (several can run at the same time, e.g. lunch monthly and breakfast weekly). Each: Preference, Meal type, Quantity (boxes per delivery, 1–20), Frequency, Start date (Monday–Saturday, within a year); the end date is calculated by the server and shown read-only |
| First note | Optional (up to 2,000 characters); saved as the first dated note |
| Latitude / Longitude | Read-only; filled by geocoding after saving |

**Customer panel** (click a row in the Customers table): customer details with delivery time, a card per package (dates,
delivery days left, pauses), **Notes**, location, delivery group, previous plan (customers from the old system), group history
and audit trail. Actions:

- **Edit**: name, phone, delivery time (required), status, address, pincode. A pincode the customer already has cannot be
  cleared. A save is refused if someone else changed the customer since it was opened.
- **Add package** / **Edit** a package (changing its start date or frequency recalculates its end date only after a confirmation
  that shows old → new dates; the old dates stay in the audit trail) / **Delete** a package entered by mistake (only before its
  first delivery day).
- **Notes**: newest first, each with date and writer (and "edited" with who/when). Anyone can add; the writer or an Admin can
  edit or delete a note. Every change (with the old text) is in the audit trail.
- **Pause a delivery**, **Change group**, **View history**.

## Delivery days, end dates and pauses

All date rules are in one place, `IDeliveryCalendarService` (`Fitzobit.Domain/Deliveries`); `DeliveryScheduleService`
and `CustomerPauseService` (Application) use it. The browser never calculates dates; it asks
`GET /api/customers/schedule?frequencyId=…&startDate=…`.

- Deliveries run **Monday to Saturday. Sundays are never delivery days.**
- A package is a number of delivery days, the frequency's value: **Monthly = 26**, **Weekly = 6**.
- `CalculateEndDate(start, days)`: the start date is delivery day 1; the end date is the last delivery day.
  Weekly from Monday 12 Oct 2026 → Saturday 17 Oct; Monthly from Monday 5 Oct 2026 → Tuesday 3 Nov.
- **Pausing** a delivery day pauses every running package whose frequency allows it (Settings → Dropdown values → Frequencies,
  *Can pause*; only today or later, never a Sunday, only for **Active** or **Paused** customers). No meal is lost: **every**
  paused day moves the package's end date to the next delivery day (`ExtendEndDateForPause`; a paused Saturday moves to Monday).
  There is **no limit** (since 2026-11-05; before, only the first 3 did — the `UnlimitedPauses` migration recalculated current
  packages). If the moved end date reaches an upcoming day *paused for everyone*, that day is paused too and the end date moves
  again. Each pause is recorded per package (`PackagePauses`) with the old and new end date and an optional reason, all in one
  transaction; package row versions and the unique (package, date) index stop a repeated or concurrent request from counting twice.
- **Pause for everyone** (Admins, Customers page): festive or emergency days pause every running package of Active and Paused
  customers, any frequency; end dates always move; removable before the day.
- **Paused count**: own paused days and days paused for everyone, e.g. "4 (2 own + 2 for everyone)" — on the package card,
  in the Customers table column *Paused Days* (sortable; export column `paused`) and on Daily Orders (page, Excel, PDF).
- When a package's start date or frequency changes, its end date is recalculated; every pause inside the new period still
  counts and its `PausedCount` becomes the number of own pauses inside it (moving the start after the old period starts at 0).
- The delivery-day count of each frequency is read from the database (`DictionaryEntries.NumericValue`); 26 and 6 exist
  only as the initial seed values.
- "Today" is the date in India (UTC+5:30).

## Drop-down values (Settings)

**Settings → Dropdown values** (Admin) manages Preferences, Meal Types, Frequencies and Customer Status
(`DictionaryEntries`: type, stable code, display name, order, active, delivery days for frequencies). Admins can add,
rename, reorder, activate and deactivate values. Changes apply immediately to the Add/Edit screens.

- A value used by customers cannot be deleted: deactivate it. Those customers keep it (shown as *no longer offered*)
  until someone changes them; it is no longer offered for new choices.
- The last active value of a type cannot be deactivated.
- The statuses Active, Inactive and Paused are system values: they can be renamed but stay active.
- Changing a frequency's delivery days affects end dates calculated from then on; stored end dates are kept.

API: `GET /api/dictionaries` (active values, all signed-in users), `GET/POST /api/dictionaries/{type}`,
`PUT /api/dictionaries/{type}/order`, `PUT/DELETE /api/dictionaries/entries/{id}` (Admin).

## Customers list, filters and export

**Customers** (`/data`) is the customer table, **one row per customer**: Customer ID, Customer Name, Phone Number, Address,
Pincode, Delivery Time, Packages (one line per package, e.g. *Veg Medium ×1 · Monthly* with its dates and pauses), Start Date
(earliest package start), End Date (latest package end), Group Number, Status, Location, Latitude, Longitude, Created At,
Updated At.

- **Columns**: show/hide any column (at least one stays visible); *Reset columns* returns to the standard set,
  *Show all columns* shows all 16. The choice is remembered per browser.
- **Search**: Customer ID, name, phone, address, pincode, meal type, group number ("3", "Group 3").
- **Filters** (combine with AND): group (or *No group*), preference, meal type, frequency (customers having **any** package
  with that value), status (all from the database),
  exact pincode, address contains, location (located / approximate / unavailable), *Incomplete only* (no package, no delivery time or no pincode). Active filters
  show as chips and in the URL.
- **Sort**: Customer ID, name, address, pincode, delivery time, group, start, end, status, latitude, longitude, created, updated.
- **Paging**: 10 / 25 / 50 / 100 rows. Everything runs on the server.

**Export** (button above the table, or *Export selected* when rows are ticked) opens a dialog:

| Choice | Options |
|---|---|
| Format | **Excel (.xlsx)** or **PDF** |
| Rows | **Filtered customers** (every page of the current filters, search and sort) · **This page** · **Selected rows** · **All customers, ignoring filters** |
| Columns | **Visible columns** (exactly the table's visible columns, in the same order; the default) or **All columns** (full export) |

The server selects the rows itself with the same filter, sort and paging code as the table (`CustomerQueryService`), so the
file never depends on what the browser has loaded. Column keys are checked against a fixed list (`CustomerExportColumns`);
unknown or repeated columns are refused. Both formats receive the same rows and columns. Every export is audited (format,
scope, row count, columns, filters).

- **Excel**: one *Customers* sheet with only the chosen columns: navy header, autofilter, frozen header row, column
  widths, Customer ID / phone / pincode stored as **text** (never numbers or scientific notation), dates as real Excel dates
  (`dd mmm yyyy`), coordinates with 6 decimals. A separate *Export info* sheet lists who exported, when, the rows, the
  columns and the filters (it never adds columns to the data sheet).
- **PDF**: A4 with 12.7 mm margins; heading (scope, number of customers, columns, filters, generation time and user); a
  table with the header row repeated on every page and alternate row shading; footer with generation time and
  *Page X of Y*. Column widths are measured from the text: the page stays portrait for a few columns and turns landscape,
  with a smaller font (9 → 6.5 pt) only when needed; names and addresses wrap, IDs/phones/dates never break, nothing is cut
  off. Dates read `12 Oct 2026` in `Export:TimeZone`.
- Limits: `Export:MaxRows` (100,000) for Excel and `Export:PdfMaxRows` (2,000) for PDF; a 2,000-row, all-column PDF takes
  ≈ 2 s on a PC (longer on the free Render instance).
- Fonts for PDF: Arial on Windows, Liberation Sans in the Docker image (`fonts-liberation`), otherwise the Segoe WP font
  embedded in PDFsharp, so PDF export works even where no font is installed.
- Example files from a test run (fake customers): `samples/exports/`.

## Dashboard

Live counts from PostgreSQL (every card links to the matching customers): Total customers (with the number of packages),
Active, Paused and Inactive customers (by status code), active Delivery Groups, Unassigned customers (and how many of them are
active), customers With / Without coordinates (approximate ones noted), one card per frequency counting **packages** (e.g.
Weekly / Monthly packages), packages of active customers ending within 7 days. Charts: customers by delivery group, packages by
meal type or customers by status, customers by pincode or city. *Recent customer changes* lists the last 8 customer events from
the audit log (added, edited, paused, package added/edited/deleted, note added/edited/deleted, group changes).

## Delivery groups

Groups are identified by **number** (Group 1, Group 2, …; `DeliveryGroups.GroupNumber`, unique, > 0). The manager decides
how many exist: **Create groups** adds N groups with the next free numbers (allocated under a PostgreSQL advisory lock, so
simultaneous requests never collide). Groups have optional notes, can be deactivated once empty (the number stays
reserved), and deleted only if never used. The page shows per group: customers, located customers, customer-status
distribution, pincodes, areas, meal types and frequencies. A customer has at most one active group (also a database rule);
every assignment, move and removal is recorded in `CustomerDeliveryGroupAssignmentHistory` (previous group, new group,
who, when, reason) and in the audit log. Groups from the old model were numbered in creation order; their old name is in the notes.

## Customer locations (geocoding)

Latitude/longitude come only from geocoding; they are never typed or guessed.

- **Service city** (Settings → General → Delivery area, Admin): the one city Fitzobit delivers in. Its area is looked up once;
  addresses are searched inside it and matches more than `Geocoding:ServiceAreaMarginKm` (15 km) outside are rejected.
  Nothing is looked up until the city is set.
- **Provider abstraction:** `IGeocodingService` (Application). Implementations (Infrastructure): `NominatimGeocodingService`
  (OpenStreetMap, free, no key, ≤ 1 request/second per its usage policy, default), `GoogleGeocodingService` (needs
  `Geocoding__Google__ApiKey`), `DisabledGeocodingService` (`Geocoding:Provider=None`).
- **Strategy** (`CustomerGeocodingService`): full address → locality from the address → pincode area (marked *Approximate*,
  can be turned off with `Geocoding:AllowPincodeApproximation=false`). A match that is only the city or region is rejected.
  Stored per customer: status (Pending / Located / NotFound / Failed), match level (Address / Locality / Pincode), matched
  place, provider, time, attempts, next retry.
- **Background worker** (`GeocodingWorker`): saving a customer never waits for geocoding. New customers and changed
  addresses/pincodes are queued (coordinates of a changed address are cleared first) and looked up within seconds. Errors and
  rate limits are retried after 1 min, 5 min, 30 min, 2 h … up to `Geocoding:MaxAttempts` (5), then *Failed*. "Retry location"
  (customer panel, bulk in the customer list, or Settings for all unavailable) queues a new lookup.
- Customers without coordinates show **Location unavailable**; they are not drawn on the map but can be assigned by hand.

## Delivery Classification and group suggestions

**Delivery Classification** (`/map`) is the data-classification view: every located customer is a point, X = longitude,
Y = latitude (scaled so distances are equal on both axes), coloured by group, with a star at each group's centre, optional
group outlines (convex hull for 3+ locations, a line for 2, none for 1) and labels (customer labels only when few are in view).
Each group is labelled on the map with its **areas** (e.g. *Group 2 — RS Puram · Gandhipuram · Saibaba Colony*), and the legend
lists every group with its customer count and areas with counts (an area is the locality read from the address, or the
pincode when no locality is known; spellings like "RS Puram" / "R.S. Puram" count as one). A summary row shows the number of
groups, customers on the map, customers **without coordinates** and **unassigned** customers; customers without coordinates
are listed beside the map and can be assigned by hand.
Scroll to zoom, drag to pan, fit to all, hover for details, click or Shift+drag to select, find a customer, hide groups in the
legend. Drawn on a canvas; tested with 5,000 customers (map data ≈ 130 ms, 12-group suggestion ≈ 0.8 s).

- **Current groups:** select customers and assign/move/remove them (preview, reason, saved immediately with history).
- **Suggest groups:** deterministic K-Means on the coordinates (k-means++ with fixed seeds and 8 restarts; the same customers
  always give the same result). **Auto** finds the natural number of areas from the average silhouette (2–15), judged on distinct locations
  (customers located only to their locality share a point), choosing the fewest groups within 0.05 of the best score, so
  neighbouring localities form one delivery area instead of one group per locality; or enter the number.
  Optional *Balance group sizes* (no group above ≈ 15 % over the average) and *Place customers without a location by pincode*.
  Suggested groups reuse the existing group numbers they overlap most, so as few customers as possible move. The manager then
  moves customers between groups in the draft (the server recalculates outlines and the change list), reviews the list of
  changes, and saves: missing groups are created and all changes are applied in one transaction with history.
- This is a classification aid, not a route optimiser; customers without a location are left unchanged unless assigned by hand.

## Authentication and first-run setup

Everyone signs in. The **server enforces roles on every API call**:

| Role | Can |
|---|---|
| **Admin** | Everything: users, drop-down values, audit log, database details, plus all Manager work |
| **Manager** | Customers (add, edit, pause, export), delivery groups, dashboard, settings (read) |

**First-run setup.** While no user exists the app shows *Create the administrator*. This works only from a browser on
the server itself (`localhost`, never through a tunnel or proxy), or from anywhere with the setup code configured on the
server (`Auth:SetupCode`; Render generates one). `Auth:AllowRemoteSetup=true` opens it completely and should only be
set briefly. The administrator then adds Managers under **Settings → Users**.

Passwords: at least 10 characters with letters and numbers, not containing the user name; stored as salted PBKDF2
hashes. Five failed sign-ins lock an account for 15 minutes. Changing or resetting a password, or changing a
role/active status, signs that user out everywhere. Sessions use an HttpOnly, SameSite=Strict cookie, 10 hours sliding.
Cookie-protection keys are stored in the database, so sign-ins survive restarts and redeploys.

## Audit trail

Important actions are recorded with **who, what, when, the record, and old/new values**: sign-ins, users, customers
created/updated/paused, drop-down value changes, group assignments/moves/removals, groups and exports. Admins browse
them under **Settings → Audit Log**; each customer's panel shows its own trail. Older entries (imports, catalogue) stay readable.

## WhatsApp architecture

The app is prepared for the **official WhatsApp Business (Cloud) API only**. It never automates WhatsApp Web, never
stores WhatsApp passwords, never hard-codes tokens and never reports a message as sent unless the API accepted it.
`IWhatsAppService` (Application) is the abstraction; `WhatsAppCloudApiService` (Infrastructure) sends approved
templates; `WhatsAppMessages` stores history. Until configured, Settings → WhatsApp shows **“WhatsApp integration not
configured”** and the app works normally. Configure with `WhatsApp__PhoneNumberId`, `WhatsApp__BusinessAccountId` and
`WhatsApp__AccessToken` (the token only as an environment variable or user secret).

## Security

- Authentication on every API endpoint (fallback policy) and role policies (`Operate`, `Admin`) checked by the server.
- CSRF protection: state-changing requests must send the `X-XSRF-TOKEN` header (issued by `GET /api/auth/state`).
- Headers: Content-Security-Policy, `X-Frame-Options: DENY`, `nosniff`, `Referrer-Policy: no-referrer`,
  Permissions-Policy, `Cache-Control: no-store` for the API, HSTS outside Development; no `Server` header.
- Rate limiting on sign-in and setup plus account lockout; forwarded headers trusted from loopback only (or all, on Render).
- Input validation on every write; Customer ID, end date and paused count are never taken from the client;
  EF Core parameterised queries.
- Exports: rows chosen on the server, columns from a fixed list, file names generated by the server, PDF text written as
  plain text (control characters removed), Excel values written as values (no formulas); only signed-in Managers/Admins.
- Outgoing HTTP requests (geocoding) are not logged (`System.Net.Http.HttpClient` at Warning), so the Google API key and
  customer addresses in request URLs never reach the logs.
- Errors: readable messages for business rules; unexpected failures return a generic message with a reference id.
  Passwords, connection strings, tokens and stack traces are never returned.
- Secrets: none in the repository (user secrets locally, environment variables when hosted). Swagger only in Development.

## Configuration and environment variables

Settings live in `src/Fitzobit.Web/appsettings.json`; any of them can be overridden with an environment variable
using `__` for nesting.

| Setting / variable | Default | Purpose |
|---|---|---|
| `Database__ConnectionString` | empty (user secret locally) | PostgreSQL: `Host=…;Database=…;Username=…;Password=…` or a `postgresql://` URL (Neon) |
| `Database__MigrateOnStartup` | `true` | Apply migrations at startup |
| `ASPNETCORE_URLS` | `http://localhost:5080` (launch profile) | Listening address |
| `ASPNETCORE_ENVIRONMENT` | `Production` (`Development` via `start.ps1`) | Development enables user secrets, Swagger and non-Secure cookies |
| `Export__MaxRows` / `Export__PdfMaxRows` / `Export__TimeZone` | 100,000 / 2,000 / `Asia/Kolkata` | Excel and PDF row limits, date time zone |
| `Geocoding__Provider` | `Nominatim` | `Nominatim` (OpenStreetMap, free), `Google` or `None` |
| `Geocoding__Nominatim__ContactEmail` | empty | Optional contact sent to OpenStreetMap (recommended by its usage policy) |
| `Geocoding__Google__ApiKey` | empty | Secret; only when Provider is `Google` (environment variable or user secret) |
| `Geocoding__ServiceAreaMarginKm` / `Geocoding__AllowPincodeApproximation` / `Geocoding__MaxAttempts` | 15 / `true` / 5 | Geocoding rules |
| `Geocoding__BackgroundEnabled` | `true` | Run the background geocoder |
| `Auth__SessionHours` | `10` | Sliding session length |
| `Auth__SetupCode` | empty | Secret that allows first-run setup from another machine |
| `Auth__AllowRemoteSetup` | `false` | Allow first-run setup from anywhere without a code (use only briefly) |
| `Auth__RequireHttpsCookies` | `true` outside Development | Mark cookies `Secure` |
| `ForwardedHeaders__TrustAllProxies` | `false` | Trust `X-Forwarded-*` from any proxy (Render) |
| `WhatsApp__PhoneNumberId`, `WhatsApp__BusinessAccountId`, `WhatsApp__AccessToken` | empty | WhatsApp Cloud API |
| `FITZOBIT_DB_CONNECTION` | | Connection string for `dotnet ef database update` |
| `FITZOBIT_TEST_PG` | | PostgreSQL login for the tests (defaults to the user secret) |

## Testing

```powershell
dotnet test
```

The tests need PostgreSQL. They use `FITZOBIT_TEST_PG` if set (a login allowed to create databases), otherwise the
user secret `Database:ConnectionString`. Every database test creates its own temporary database from the real
migrations and drops it afterwards. 166 tests:

- **Delivery calendar (unit):** Sundays excluded, Weekly = 6 and Monthly = 26 delivery days, start/end dates, counting,
  pause extension (Saturday → Monday), invalid input.
- **Drop-down values:** seeding, active options in order, add/rename/reorder/deactivate/delete rules, values in use.
- **Customers:** generated sequential IDs, concurrent creation, existing IDs kept and skipped, field validation (delivery
  time, at least one package, quantity, note length), inactive/wrong-type values refused, schedule preview, audit, concurrency.
- **Packages:** several at once, add/edit with confirmed recalculation, delete only before the first delivery, database checks.
- **Notes:** newest first with writer, the writer edits/deletes, other managers cannot, admins can, audit.
- **Pauses:** all running packages paused, blocked when one has no pauses left, extension by delivery days, maximum 3 (also as a database rule), invalid dates, duplicates, only active
  customers, stale/concurrent requests not counted twice, renewal resets the count.
- **Database:** PostgreSQL configuration, schema/indexes/constraints, and an upgrade from the old schema with old
  plan/import data (customers preserved, values mapped, legacy tables archived, sequence continues after existing IDs).
- **Customers list:** filters, search, sort, paging, filter options, group removal.
- **Export:** Excel and PDF get the same rows and columns for every kind of filter, selected rows and the current page;
  only the chosen columns (e.g. Active + Group 2 with 3 columns); text/date cells; PDF orientation and multi-page tables;
  unknown columns and oversized PDFs refused; audit; embedded font fallback.
- **Dashboard:** status, group, coordinate and frequency counts, recent changes.
- **Delivery groups:** numbering (also under concurrency), notes, (de)activation, deletion, one active group, bulk
  assign/move/remove in one save with history and audit, per-group distributions.
- **Geocoding:** success, locality and pincode fallback, city-only and out-of-area matches rejected (no fake coordinates),
  errors retried then failed, rate limiting, no lookups without a city or provider, address changes reset the location;
  Nominatim and Google adapters against canned HTTP answers (never leaking the API key).
- **Classification:** requested and automatic group counts (also with many customers sharing a locality point), group
  area names, determinism, unlocated customers, reuse of existing numbers,
  balancing, scope by status, draft evaluation, transactional save with history, refusing inactive groups, boundaries
  (none/point/line/polygon, collinear points, 20,000 points).
- **HTTP (the real web host):** the Add Customer workflow, Settings drop-down administration, removed endpoints,
  roles, first-run setup, CSRF, lockout, sessions, security headers, safe errors, no secrets in settings.

## Deployment

**Render + Neon (free, live at https://fitzobit.onrender.com):** see [deploy/RENDER.md](deploy/RENDER.md). Render builds the
`Dockerfile`; the only database setting is `Database__ConnectionString` (Neon). Migrations run on start-up.
Alternatives: [deploy/TAILSCALE.md](deploy/TAILSCALE.md) (from this PC) and [deploy/AZURE.md](deploy/AZURE.md).

## Troubleshooting

| Symptom | Fix |
|---|---|
| “Database:ConnectionString is not set” at start-up | Set the user secret (see [Local PostgreSQL](#local-postgresql)) or `Database__ConnectionString` |
| Same error, and the message says the user secrets file is *not found* | Use the local settings file instead: `src/Fitzobit.Web/appsettings.Local.json` with `{ "Database": { "ConnectionString": "Host=localhost;Port=5432;Database=fitzobit;Username=postgres;Password=<your password>" } }`. It is read only in Development (environment variables still take priority), is git-ignored, and is never copied to the build output, publish folder or Docker image |
| Need test data | With the app running and signed up: `.\samples\Add-SampleCustomers.ps1` (or `-BaseUrl http://localhost:5080`) adds 20 fake Coimbatore customers through the API; running it again adds nothing |
| “password authentication failed for user postgres” | Fix the password in the user secret |
| Tests fail with “Tests need PostgreSQL” | Set the user secret or `FITZOBIT_TEST_PG` |
| Build fails with “file is locked by Fitzobit.Web” | Stop the running app: `Get-Process Fitzobit.Web \| Stop-Process` |
| UI changes don't show | `.\start.ps1 -RebuildUI` or `cd src\Fitzobit.UI; npm run build` |
| “Setup must be completed on the server itself” | Open the app at `http://localhost:5080` on the server, or set `Auth__AllowRemoteSetup=true` temporarily |
| A value is missing from a drop-down | Settings → Dropdown values: it may be inactive |
| Need an empty local database | `DROP DATABASE fitzobit` (psql or pgAdmin), then start the app |
| Errors in the browser | The message includes a reference id; find it in the server log for details |
