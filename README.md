# Fitzobit Delivery Management

Fitzobit Delivery Management turns customer Excel files into organised delivery operations: it imports and
validates customers, classifies them by location, pincode and plan, lets managers build **delivery groups**,
and gives a searchable, filterable, exportable view of every customer, all from a local **SQLite** database.
The plan catalogue comes from Fitzobit's WhatsApp Business catalogue export.

- **Backend:** ASP.NET Core 8, EF Core 8, SQLite (provider-switchable), ClosedXML for Excel
- **Frontend:** React 18 + TypeScript (Vite), served by the same host
- **Runs locally:** no database server, Docker or cloud service is required

---

## Contents

1. [Architecture](#architecture)
2. [Requirements](#requirements)
3. [Running the app](#running-the-app)
4. [SQLite setup and database location](#sqlite-setup-and-database-location)
5. [EF Core migrations](#ef-core-migrations)
6. [Authentication and first-run setup](#authentication-and-first-run-setup)
7. [Excel template](#excel-template)
8. [Import workflow](#import-workflow)
9. [Delivery groups](#delivery-groups)
10. [View Data](#view-data)
11. [Filtering, sorting and paging](#filtering-sorting-and-paging)
12. [Excel export](#excel-export)
13. [Catalogue and plans](#catalogue-and-plans)
14. [Audit trail](#audit-trail)
15. [WhatsApp architecture](#whatsapp-architecture)
16. [Security](#security)
17. [Configuration and environment variables](#configuration-and-environment-variables)
18. [Testing](#testing)
19. [Deployment](#deployment)
20. [PostgreSQL](#postgresql)
21. [Troubleshooting](#troubleshooting)

---

## Architecture

A modular monolith with clean layering. Dependencies point inwards only.

| Project | Responsibility |
|---|---|
| `src/Fitzobit.Domain` | Entities and pure rules (customers, plans, catalogue, delivery groups, imports, users, audit, WhatsApp messages). No EF Core or provider dependencies. |
| `src/Fitzobit.Application` | Use cases: catalogue sync and price warnings, Excel import (validation, plan matching, classification, assignment, commit), delivery groups, customer queries/edits/export, users and sign-in, audit queries, dashboard, WhatsApp status. Talks to the database only through `IFitzobitDbContext`. |
| `src/Fitzobit.Infrastructure` | `FitzobitDbContext`, entity configurations, migrations, provider selection (`DatabaseProviderConfigurator`), SQLite tuning, Excel reading/writing (ClosedXML), password hashing, WhatsApp Cloud API client. |
| `src/Fitzobit.Web` | ASP.NET Core host: thin API controllers, cookie authentication, role policies, CSRF, security headers, safe error handling; serves the built UI from `wwwroot`. |
| `src/Fitzobit.UI` | React + TypeScript single-page app. `npm run build` writes to `src/Fitzobit.Web/wwwroot`. |
| `tests/Fitzobit.Tests` | xUnit tests: unit, SQLite integration (real migrations) and HTTP integration (the real web host). |

Conventions: enums stored as text, money as `decimal(12,2)`, all timestamps in UTC; every create/update is
stamped with who/when; business errors become `409` responses with a readable message.

## Requirements

- .NET SDK 8 or later (the projects target `net8.0`)
- Node.js 18+ and npm (only to build the UI)
- Windows, macOS or Linux. The commands below use PowerShell.

## Running the app

```powershell
.\start.ps1             # builds the UI if needed, then serves app + API on http://localhost:5080
.\start.ps1 -RebuildUI  # force a fresh UI build after UI changes
```

Open **http://localhost:5080**. The first time, the app asks you to create the administrator account
(see [first-run setup](#authentication-and-first-run-setup)).

On every start the app:

1. creates or upgrades `data\fitzobit.db` by applying EF Core migrations, then
2. syncs the catalogue from `Fitzobit_WhatsApp_Catalogue.xlsx` (idempotent: running it again never duplicates anything).

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

## SQLite setup and database location

Nothing to install: the database is a single file, created automatically.

| | |
|---|---|
| Default location | `data\fitzobit.db` under the repository root (plus `-wal` / `-shm` files while running) |
| Setting | `Database:ConnectionString` (relative paths resolve against the folder containing `Fitzobit.sln`, or `Fitzobit:BasePath`) |
| Tuning | WAL journal, `synchronous=NORMAL`, 64 MB cache, foreign keys on (`SqliteConnectionTuning`) |
| Backup | Stop the app and copy `data\fitzobit.db*`, or use `sqlite3 data\fitzobit.db ".backup backup.db"` while it runs |
| Reset | Stop the app and delete `data\fitzobit.db*`; it is recreated (empty, with the catalogue) on the next start |

Main tables: `Users`, `Customers`, `Plans`, `PlanAliases`, `CatalogueItems`, `CataloguePricingTiers`, `DeliveryGroups`,
`CustomerDeliveryGroupAssignments`, `CustomerDeliveryGroupAssignmentHistory`, `ImportBatches`, `ImportRows`,
`ImportDraftGroups`, `ImportPlanMappings`, `AuditLogs`, `WhatsAppMessages`.

Rules enforced by the database as well as by the code:

- `Customers.CustomerId` is unique, and so is `Users.UserName`.
- A customer has at most **one active delivery group**: filtered unique index
  `UX_CustomerDeliveryGroupAssignments_ActiveCustomer`. A move deactivates the old row and writes history and audit.
- Foreign keys with explicit delete behaviour; catalogue tiers belong to their item.
- Indexes back every View Data filter and sort (pincode, plan, status, import batch, created date, active assignments by customer and by group).

## EF Core migrations

Migrations are applied automatically at startup (`Database:MigrateOnStartup`). They are provider-specific, so there are two sets
built from the same model:

| Provider | Folder | Migrations |
|---|---|---|
| SQLite | `src/Fitzobit.Infrastructure/Persistence/Migrations` | `InitialCreate`, `Part2CustomerImport`, `AssignmentLookupIndexes`, `Part3AuthAuditMessaging`, `DataProtectionKeys` |
| PostgreSQL | `src/Fitzobit.Infrastructure.PostgreSql/Migrations` | `InitialPostgreSql` (the full schema) |

After changing the model, add a migration to **both** sets (the `dotnet-ef` tool is in the local tool manifest; `dotnet tool restore` installs it):

```powershell
dotnet ef migrations add <Name> -p src\Fitzobit.Infrastructure -s src\Fitzobit.Infrastructure -o Persistence\Migrations
dotnet ef migrations add <Name> -p src\Fitzobit.Infrastructure.PostgreSql -s src\Fitzobit.Infrastructure.PostgreSql -o Migrations
```

`dotnet ef` uses `data\fitzobit.db` (or `FITZOBIT_DB_CONNECTION`) for SQLite, and `FITZOBIT_PG_CONNECTION` for PostgreSQL.

## Authentication and first-run setup

Everyone signs in. There are two roles; the **server enforces them on every API call** (hidden buttons are only cosmetic):

| Role | Can |
|---|---|
| **Admin** | Everything: users, plan activation, remembered plan mappings, catalogue re-sync, audit log, database details, plus all Manager work |
| **Manager** | Upload and import customers, View Data, edit customers, delivery groups, export, dashboard, catalogue (view), settings (read) |

**First-run setup.** While no user exists the app shows *Create the administrator*. For safety this works only from
a browser on the server itself (`localhost`, never through a tunnel or proxy), or from anywhere with the setup code
configured on the server (`Auth:SetupCode`; Render generates one). `Auth:AllowRemoteSetup=true` opens it completely and
should only be set briefly. Setup is refused once any user exists. The administrator then adds Managers under **Settings → Users**.

Cookie-protection keys are stored in the database (`DataProtectionKeys`), so sign-ins survive restarts and redeploys.

Passwords: at least 10 characters with letters and numbers, not containing the user name; stored as salted PBKDF2
hashes (ASP.NET Core Identity hasher). Five failed sign-ins lock an account for 15 minutes; error messages never
reveal whether a user name exists. Changing or resetting a password, or changing a role/active status, signs that
user out everywhere. Users change their own password from the menu under their name.

Sessions use an HttpOnly, SameSite=Strict cookie (`fitzobit.auth`), 10 hours sliding (`Auth:SessionHours`), marked
`Secure` outside Development.

## Excel template

Download it from **Upload & Create Groups → Download template** (`GET /api/imports/template`).
Required columns (header names are matched flexibly, e.g. `Customer ID`, `customer name`):

| Column | Rules |
|---|---|
| CustomerID | Required, unique in the file. Existing IDs are matched, never overwritten. |
| CustomerName | Required, 2–150 characters, must contain letters. |
| PhoneNumber | 10-digit Indian mobile number; `+91`, `0`, spaces and dashes are accepted. Landlines are a warning. |
| Location | Required. Include the 6-digit pincode, e.g. `14 DB Road, RS Puram, Coimbatore 641002`. |
| Plan | Text as the customer knows it, e.g. `Medium Fit Box Monthly Veg`. Mapped to a catalogue plan. |

Only `.xlsx` files up to 10 MB / 20,000 rows are accepted; files with macros are rejected; formulas are not evaluated.
Sample files with obviously fake customers are in `samples\`.

## Import workflow

**Upload & Create Groups** is a 7-step wizard. Nothing touches customers or groups until the last step.

1. **Upload**: file checks (type, size, structure, zip safety).
2. **Validate**: every row gets *Valid*, *Warning* or *Error* with reasons (missing/duplicate CustomerID, invalid phone,
   missing pincode, unknown plan, existing customer…). Error rows are skipped.
3. **Classify**: counts by pincode, area, city and plan; plan mapping. Ambiguous plans are never chosen automatically;
   a manual choice is remembered for future files.
4. **Create groups**: draft new delivery groups or use existing ones.
5. **Assign customers**: one by one, in bulk, or all customers of a pincode/area/city, always with a preview.
   Optional rule-based suggestions (existing assignment → pincode → location → area → city).
6. **Review**: exactly what will be saved, exceptions, and group changes for existing customers.
7. **Save**: one transaction. Any failure rolls everything back and nothing is saved.

Every import is listed under **Import History**. Opening one shows its summary and the rows, errors and warnings,
with a link to the imported customers in View Data.

## Delivery groups

A delivery group (e.g. *Coimbatore North*) is defined by managers and is **not** the same as a location: one group can
cover several areas, pincodes and plans. The **Delivery Groups** page shows total groups, assigned and unassigned
customers, a distribution chart, and each group's customers, pincodes, locations and plans. Groups can be added,
renamed, deactivated (once empty), deleted (only if never used) and customers moved between groups with a reason.
Every change is kept in the assignment history.

## View Data

**View & Manage Data** (`/data`) is the customer table: CustomerID, Name, Phone, Location, Pincode, Plan, Delivery
Group, Status, Created, Updated and Actions. It has column visibility (remembered per browser) with reset, row
selection, sticky header, horizontal scrolling on small screens, and clear empty states.

Click a row to open the customer panel:

- **Details**: customer, delivery (current group, assignment date) and import (batch link, created/updated by whom).
- **Edit**: name, phone, location, plan/variant, status. CustomerID is the business key and cannot change. The same
  validation as the import applies; if someone else changed the customer since you opened it, the save is refused
  instead of silently overwriting their change.
- **Change group**: choose a group (or remove from group), see a preview, give a reason, confirm.
- **History**: group history and the audit trail with old → new values.

Selecting rows enables bulk *Change group* and *Export selected*.

## Filtering, sorting and paging

All of it runs on the server, so the browser never loads every customer.

- **Search** (debounced): CustomerID, name, phone (formatted or digits), location, pincode, plan, delivery group.
- **Filters** (combine with AND): delivery group (or *No group*), plan, exact pincode, location contains,
  status (*Active*, *Inactive*, *Unassigned*), import batch.
- **Sort**: CustomerID, name, location, pincode, plan, delivery group, created, updated; ascending or descending.
- **Paging**: 10 / 25 / 50 / 100 rows, “Showing 1–25 of 2,500 customers”.

Active filters appear as removable chips and in the URL (e.g. `/data?group=3&plan=1&pincode=641002`), so a view can
be bookmarked. Dashboard and group charts link straight to the matching filtered view.

## Excel export

**Export to Excel** offers three scopes:

| Scope | Contents |
|---|---|
| All customers | Every customer, regardless of filters |
| Filtered customers | Exactly the customers matching the current search and filters (the same server query as the table, in the same order) |
| Selected customers | The rows you ticked |

Columns: CustomerID, CustomerName, PhoneNumber, Location, Pincode, Plan, DeliveryGroup, Status, CreatedAt, UpdatedAt.
Formatting: navy bold header, autofilter, frozen header row, sized columns, CustomerID/phone/pincode stored as text
(no scientific notation or lost zeros), dates as real Excel dates in Indian time (`Export:TimeZone`). An
*Export info* sheet records who exported, when, the scope and the filters. Each export is written to the audit log.

## Catalogue and plans

`Fitzobit_WhatsApp_Catalogue.xlsx` is the source of the catalogue (10 items, 18 pricing tiers): item, category,
display price, tiers, billing period, meal count, description, availability. Values are stored **exactly as
published**. Inconsistencies are flagged, never corrected:

- **Price mismatch**: *Medium Fit Box Subscription (5 Items) - monthly* displays ₹699 while describing Veg ₹2,699 / Egg ₹2,899 per month.
- **Weekly/monthly conflict**: both *Standalone high Protein Salads* items (weekly and monthly) show ₹2,299.

One plan exists per catalogue item. Admins can deactivate/reactivate plans, remove remembered plan mappings and
re-sync the catalogue (**Settings → Plans / Catalogue**).

## Audit trail

Important actions are recorded with **who, what, when, the record, and old/new values**: sign-ins (and failures),
users, imports, customers created/updated, group assignments/moves/removals, groups, plans, plan mappings and exports.
Audit rows are written in the same transaction as the change. Admins browse them under **Settings → Audit Log**;
each customer's panel shows its own trail.

## WhatsApp architecture

The app is prepared for the **official WhatsApp Business (Cloud) API only**. It never automates WhatsApp Web, never
stores WhatsApp passwords, never hard-codes tokens and never reports a message as sent unless the API accepted it.

- `IWhatsAppService` (Application) is the abstraction; `WhatsAppCloudApiService` (Infrastructure) calls
  `POST {ApiBaseUrl}/{ApiVersion}/{PhoneNumberId}/messages` with an approved template.
- `WhatsAppMessages` stores message history (purpose, template, status Sent/Delivered/Read/Failed, provider id,
  timestamps), ready for customer notifications, delivery/plan/subscription reminders, delivery-group notifications,
  templates, history and delivery/read status.
- Until it is configured, the app shows **“WhatsApp integration not configured”** (Settings → WhatsApp) and works
  normally without it.

To configure, set on the server (the token only as an environment variable or user secret):

```powershell
$env:WhatsApp__PhoneNumberId     = '<phone number id>'
$env:WhatsApp__BusinessAccountId = '<business account id>'
$env:WhatsApp__AccessToken       = '<permanent access token>'   # never in appsettings.json or source control
```

## Security

- Authentication on every API endpoint (fallback policy) and role policies (`Operate`, `Admin`) checked by the server.
- CSRF protection: state-changing requests must send the `X-XSRF-TOKEN` header (token issued by `GET /api/auth/state`).
- Headers: Content-Security-Policy, `X-Frame-Options: DENY`, `nosniff`, `Referrer-Policy: no-referrer`,
  Permissions-Policy, `Cache-Control: no-store` for the API, HSTS outside Development; no `Server` header.
- Rate limiting on sign-in and setup (10 per minute per address) plus account lockout.
- Behind a tunnel or reverse proxy on the same machine, the visitor's address and scheme come from `X-Forwarded-*`
  headers (trusted from loopback only), and first-run setup refuses any proxied request.
- Uploads: `.xlsx` only, size and row limits, zip-bomb guard, macros rejected, formulas not evaluated.
- Input validation on every write; EF Core parameterised queries (no SQL is built from user input).
- Errors: users get a readable message; unexpected failures return a generic message with a reference id, and the
  details go to the server log. Passwords, connection strings, tokens and stack traces are never returned.
- Secrets: none in the repository. Swagger is enabled only in Development.

## Configuration and environment variables

Settings live in `src/Fitzobit.Web/appsettings.json`; any of them can be overridden with an environment variable
using `__` for nesting (e.g. `Database__ConnectionString`).

| Setting / variable | Default | Purpose |
|---|---|---|
| `ASPNETCORE_URLS` | `http://localhost:5080` (launch profile) | Listening address |
| `ASPNETCORE_ENVIRONMENT` | `Production` (`Development` via `start.ps1`) | Development enables Swagger and non-Secure cookies |
| `Database__Provider` | `Sqlite` | `Sqlite` (local file) or `PostgreSQL` (hosted, e.g. Neon) |
| `Database__ConnectionString` | `Data Source=data/fitzobit.db` | SQLite file |
| `Database__MigrateOnStartup` | `true` | Apply migrations at startup |
| `Database__SqliteJournalMode` | `WAL` | Use `DELETE` on network storage (Azure App Service) |
| `Fitzobit__BasePath` | folder containing `Fitzobit.sln` | Base for relative paths (set it for published deployments) |
| `CatalogueSeed__Enabled` / `CatalogueSeed__FilePath` | `true` / `Fitzobit_WhatsApp_Catalogue.xlsx` | Catalogue sync at startup |
| `Import__MaxFileSizeBytes` / `Import__MaxRows` | 10 MB / 20,000 | Upload limits |
| `Export__MaxRows` / `Export__TimeZone` | 100,000 / `Asia/Kolkata` | Export limit and date time zone |
| `Auth__SessionHours` | `10` | Sliding session length |
| `Auth__SetupCode` | empty | Secret that allows first-run setup from another machine (Render generates it) |
| `Auth__AllowRemoteSetup` | `false` | Allow first-run setup from anywhere without a code (use only briefly) |
| `ForwardedHeaders__TrustAllProxies` | `false` | Trust `X-Forwarded-*` from any proxy; set on platforms reachable only through their proxy (Render) |
| `Auth__RequireHttpsCookies` | `true` outside Development | Mark cookies `Secure` |
| `WhatsApp__PhoneNumberId`, `WhatsApp__BusinessAccountId`, `WhatsApp__AccessToken` | empty | WhatsApp Cloud API (token: environment/user secrets only) |
| `FITZOBIT_DB_CONNECTION` / `FITZOBIT_PG_CONNECTION` | | Connection strings used by `dotnet ef` (SQLite / PostgreSQL) |
| `FITZOBIT_TEST_PG` | | Run the test suite against PostgreSQL (a login allowed to create databases) |

## Testing

```powershell
dotnet test
```

145 tests, all against real SQLite databases built from the real migrations and seeded from the real catalogue workbook:

- **Unit:** phone, name and location validation, pincode extraction, location classification, plan matching,
  catalogue price warnings, password policy.
- **Integration (SQLite):** catalogue seeding, import upload/validation/commit and rollback, group assignment and the
  one-active-group rule, delivery group management, View Data filters/search/sort/paging, customer edits with audit
  old/new values and concurrency conflicts, removal from groups with history, Excel export read back with ClosedXML
  (row count, text cells, real dates).
- **HTTP (the real web host, `WebApplicationFactory`):** anonymous → 401, Manager → 403 on admin endpoints, Admin
  access, first-run setup only once and only locally, CSRF required, lockout after five failures, sessions ended by
  password change or deactivation, security headers, no stack traces in errors, no secrets in settings.

The end-to-end flow (setup, sign-in, upload, validate, classify, groups, assign, review, save, search, filters, sort,
edit, reassign, history, filtered export, import history, dashboard) was exercised in a browser against a scratch
database. With 10,000 customers: upload + validation about 2.6 s, save about 1.6 s, View Data pages/filters/sorts
under 30 ms, filtered export about 0.1 s and a full export about 0.8 s.

## Deployment

**Free cloud hosting, independent of your PC: Render + Neon.** See [deploy/RENDER.md](deploy/RENDER.md). Render runs the
`Dockerfile` (free web service, `render.yaml`) and Neon provides free PostgreSQL. The app sleeps after 15 idle minutes.

**Free hosting from this PC (no card):** see [deploy/TAILSCALE.md](deploy/TAILSCALE.md). `deploy\local-server.ps1` runs the
production server on http://localhost:5180 and Tailscale Funnel publishes it at a permanent `https://….ts.net` address.

**Azure App Service (Free F1, needs a card for sign-up):** see [deploy/AZURE.md](deploy/AZURE.md); `deploy\publish-azure.ps1` builds the upload package.

For a single office machine or small server:

```powershell
cd src\Fitzobit.UI; npm ci; npm run build; cd ..\..
dotnet publish src\Fitzobit.Web -c Release -o C:\Fitzobit\app
```

Copy `Fitzobit_WhatsApp_Catalogue.xlsx` next to the app (or point `CatalogueSeed__FilePath` to it), then run with
`ASPNETCORE_ENVIRONMENT=Production`, `Fitzobit__BasePath=C:\Fitzobit`, `Database__ConnectionString=Data Source=C:\Fitzobit\data\fitzobit.db`
and `ASPNETCORE_URLS`. Put it behind HTTPS (IIS, a reverse proxy, or Kestrel with a certificate): cookies are
`Secure` in Production. Run it as a Windows service or under IIS so it restarts automatically, and back up the
`data` folder regularly. Keep the ASP.NET Core data-protection keys (the default per-user key store) on the same
machine so sign-in cookies stay valid across restarts.

## PostgreSQL

SQLite is the default for local use; PostgreSQL is used for hosting (Neon on Render). Switch with configuration only:

```powershell
$env:Database__Provider = 'PostgreSQL'
$env:Database__ConnectionString = 'postgresql://user:password@host/dbname?sslmode=require'   # or Host=…;Username=…
```

- The provider is chosen in one place, `DatabaseProviderConfigurator` (Infrastructure). The Domain and Application
  layers have no provider-specific code; the only provider-aware model line is the filter of the active-assignment
  index (`"IsActive" = 1` for SQLite, `"IsActive" = TRUE` for PostgreSQL).
- PostgreSQL migrations live in `Fitzobit.Infrastructure.PostgreSql` and are applied at startup like SQLite's.
- Text enums, `numeric(12,2)` money and `timestamp with time zone` (UTC) are used on PostgreSQL.
- Tests: set `FITZOBIT_TEST_PG` to a PostgreSQL connection string allowed to create databases, then run `dotnet test`.
  Every test then runs against its own temporary PostgreSQL database.

Moving existing SQLite data to PostgreSQL: the simplest route is re-uploading the customer Excel files into the hosted
app. A direct copy means exporting each table in dependency order (users, catalogue, plans, groups, customers,
assignments, history, imports, audit) and resetting each identity sequence to `MAX(Id)+1`.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Build fails with “file is locked by Fitzobit.Web” | Stop the running app: `Get-Process Fitzobit.Web \| Stop-Process` |
| UI changes don't show | `.\start.ps1 -RebuildUI` or `cd src\Fitzobit.UI; npm run build` |
| “Setup must be completed on the server itself” | Open the app at `http://localhost:5080` on the server, or set `Auth__AllowRemoteSetup=true` temporarily |
| Signed out right after signing in (Production over plain HTTP) | Use HTTPS, or set `Auth__RequireHttpsCookies=false` (cookies are then Secure only on HTTPS requests) |
| “Too many failed attempts” | Wait 15 minutes, or an Admin resets the password (which also unlocks) |
| “Your session token is missing or has expired” | Reload the page; the app fetches a fresh CSRF token |
| Catalogue is empty | Check `CatalogueSeed:FilePath`, then **Settings → Catalogue → Re-sync now** (Admin) |
| Export is refused as too large | Narrow the filters or raise `Export__MaxRows` |
| Need to start over with an empty database | Stop the app, delete `data\fitzobit.db*`, start again (first-run setup appears) |
| Errors in the browser | The message includes a reference id; find it in the server log for details |
