# Fitzobit Delivery Management — Handoff (continue Part 3)

> **For the next chat:** read this whole file first. Parts 1, 2 and 3 are complete and verified (see §4 "Part 3 status").
> The only open item is the user's decision on resetting the real database. The full Part 3 brief is at the end.

---

## 1. Where things are

| Item | Value |
|---|---|
| Project root | `E:\ClaudeWorkspace\Fitzobit` (not a git repo; the user commits — **do not run git**) |
| Database | `E:\ClaudeWorkspace\Fitzobit\data\fitzobit.db` (SQLite, created/migrated on startup) |
| Catalogue source | `E:\ClaudeWorkspace\Fitzobit\Fitzobit_WhatsApp_Catalogue.xlsx` (10 items, 18 tiers) |
| App URL | `http://localhost:5080` (API + built UI). UI dev server `:5173` proxies `/api` |
| Start | `powershell -ExecutionPolicy Bypass -File E:\ClaudeWorkspace\Fitzobit\start.ps1` (builds UI if needed) |
| Build | `dotnet build Fitzobit.sln` · UI: `cd src\Fitzobit.UI; npm run build` (outputs to `src\Fitzobit.Web\wwwroot`) |
| Tests | `dotnet test` (114 passing at the end of Part 2; xUnit, real SQLite in-memory via migrations) |
| Migrations | `dotnet ef migrations add <Name> -p src\Fitzobit.Infrastructure -s src\Fitzobit.Infrastructure -o Persistence\Migrations` (local tool manifest in `.config`) |
| Samples | `samples\Sample_Customers_Test.xlsx` (22 rows, every validation path), `samples\Sample_Customers_10000.xlsx` (load test). Fake customers only. |
| Toolchain | .NET SDK 9 targeting **net8.0**, EF Core **8.0.11**, ClosedXML 0.104.2, React 18 + TS + Vite 5, lucide-react, @fontsource-variable/inter, Python 3 (openpyxl) for checks |

**Stop the running app before building** (`Fitzobit.Web.exe` locks DLLs): `Get-Process Fitzobit.Web | Stop-Process -Force`.

**Current DB contents (real DB):** Part 2 test data — 16 customers `TST001…TST022`, groups *Kumbakonam-Trichy Route*, *Thanjavur North*, *Thanjavur South*, 4 import batches (#1 cancelled, #2 completed, #3 cancelled, #4 cancelled), 5 plan aliases. No users yet (auth is new in Part 3). The user has not decided whether to reset it; spec says "no fake production data" → **ask the user** before deleting; offer reset (delete `data\fitzobit.db*`, restart).

---

## 2. Architecture (modular monolith)

```
src/Fitzobit.Domain          entities + pure rules (no EF/provider deps)
src/Fitzobit.Application     use cases; talks to DB only via IFitzobitDbContext (EF Core + EF Relational, provider-neutral)
src/Fitzobit.Infrastructure  FitzobitDbContext, configurations, migrations, provider switch, Excel I/O (ClosedXML),
                             password hashing, WhatsApp Cloud API client, SQLite connection tuning
src/Fitzobit.Web             ASP.NET Core host, thin controllers, auth/CSRF/security headers, serves wwwroot
src/Fitzobit.UI              React + TS (Vite)
tests/Fitzobit.Tests         xUnit (references Application, Infrastructure, Domain, and now Web for integration tests)
```

Key conventions:
- Provider choice only in `Infrastructure/Persistence/DatabaseOptions.cs` (`DatabaseProviderConfigurator`). PostgreSQL case throws NotSupported (reserved). SQLite-only tuning (`SqliteConnectionTuning`: WAL, synchronous=NORMAL, 64 MB cache, FK on) is added there.
- The only provider-aware model line: filtered unique index `UX_CustomerDeliveryGroupAssignments_ActiveCustomer` (one active group per customer) in `FitzobitDbContext.OnModelCreating`.
- Enums stored as strings; money `decimal(12,2)`; all DateTimes UTC (converter in DbContext).
- `SaveChanges` stamps CreatedAt/UpdatedAt and CreatedBy/UpdatedBy from `ICurrentUser`.
- Business errors: throw `BusinessRuleException` (→ 409 with message) / `NotFoundException` (→ 404). Web `ApiExceptionHandler` maps them; unexpected → 500 generic message + reference id, logged.
- Audit: `IAuditLogger.Record(...)` and (new) `RecordChange(action, type, key, summary, oldValues, newValues, details)`; `ChangeSet.Track(field, old, new)` helper. Audit rows are written in the same SaveChanges as the change.
- Code style: match existing (primary constructors, records for DTOs, XML doc summaries on classes, minimal comments).
- User preferences (from memory): edits only, no git; deliver test cases as .xlsx outside the repo if asked; keep changes minimal and verified.

---

## 3. Parts 1 & 2 — done and verified

**Part 1:** schema, catalogue seeding from the workbook (idempotent, preserves source prices), price warnings
(`CatalogueWarningEvaluator`: PRICE_MISMATCH for Medium monthly ₹699 vs ₹2,699/₹2,899; PERIOD_PRICE_CONFLICT for salads weekly=monthly ₹2,299),
plans (one per catalogue item), dashboard, catalogue screen, navy-sidebar UI.

**Part 2:** Excel template (`GET /api/imports/template`), secure upload (.xlsx only, 10 MB, zip inspection, no macros, formulas not evaluated),
validation (CustomerID missing/duplicate/existing, name, Indian phone, location + pincode via `ILocationClassificationService`, plan mapping via
`PlanMatcher` — saved alias → exact name/code → keywords; ambiguous never auto-chosen; manual choices saved as `PlanAlias`),
7-step wizard (Upload, Validate, Classify, Create Groups, Assign Customers, Review, Save), draft groups, assignment (individual/bulk/by
pincode/area/city with preview+confirm), rule-based suggestions (existing assignment → pincode → location → area → city; only unique evidence),
transactional Confirm & Save with rollback, existing customers never overwritten (only group may change; "keep current group" counted separately),
Delivery Groups page (add/rename/deactivate/delete/view customers/bulk move with reason), Import History list.
Performance (10k rows): upload+validate ≈3 s, save ≈3 s, list/filter/assign < 0.1 s. Indexes added in migration `AssignmentLookupIndexes`.

Migrations so far: `InitialCreate`, `Part2CustomerImport`, `AssignmentLookupIndexes` (all applied to the real DB).

---

## 4. Part 3 — progress so far (this chat)

### Written and compiling (not yet tested, no migration yet)
Packages added: `Microsoft.Extensions.Identity.Core 8.0.11` + `Microsoft.Extensions.Http 8.0.1` (Infrastructure),
`Microsoft.AspNetCore.Mvc.Testing 8.0.11` + project ref to Web (tests).

- **Domain**
  - `Domain/Users/AppUser.cs` — UserName (lower-case, unique), DisplayName, Role (`UserRole` Admin|Manager), PasswordHash, IsActive, SecurityStamp (rotated on password/role/active change), FailedLoginCount, LockoutEndsAt, LastLoginAt. `Roles` constants.
  - `Domain/Messaging/WhatsAppMessage.cs` — message history (purpose, status Sent/Delivered/Read/Failed, provider id, timestamps). Only written when the API actually answered.
  - `Domain/Auditing/AuditLog.cs` — added `OldValues`, `NewValues` (JSON) + new action names (CustomerUnassigned, PlanUpdated, PlanAliasDeleted, User*, ExportPerformed…).
- **Application**
  - `Abstractions.cs` — `IFitzobitDbContext` now has `Users`, `WhatsAppMessages`.
  - `Auditing/AuditLogger.cs` — `RecordChange` + `ChangeSet`.
  - `Users/UserServices.cs` — `IPasswordHashing`, `PasswordPolicy` (≥10 chars, letters+digits, not containing user name), `AuthService` (SignInAsync with 5-attempt/15-min lockout and generic errors, first-run `CreateFirstAdminAsync` only when no users, `ValidateSessionAsync` by security stamp), `UserService` (list/create/update role+active/reset password/change own password; cannot demote/deactivate last active admin or yourself).
  - `Customers/CustomerQueryService.cs` — composable `CustomerRow` projection (plan name + current group via indexed per-row subqueries), `CustomerFilter` (Search across ID/name/phone/location/pincode/plan/group; Group id or "none"; PlanId; exact Pincode; Location contains; Status Active|Inactive|Unassigned; ImportBatchId; Sort customerId|customerName|location|pincode|plan|group|createdAt|updatedAt; Desc), page sizes 10/25/50/100, `GetDetailAsync` (current group, assignment date, history, import batch, last 50 audit entries).
  - `Customers/CustomerCommandService.cs` — `UpdateAsync` (CustomerID immutable; validates name/phone/location/plan/tier/status; optimistic concurrency via `ExpectedUpdatedAt`; re-classifies location; audit with old/new values; warnings for landline/duplicate phone/no pincode).
  - `Customers/CustomerExportService.cs` — `ExportScope` All|Filtered|Selected; filtered uses the exact same `ApplyFilter` + `ApplySort`; `ExportOptions` (MaxRows 100k, TimeZone Asia/Kolkata); audit ExportPerformed with filters.
  - `DeliveryGroups/DeliveryGroupAssignmentService.cs` — added `UnassignManyAsync` (history with NewGroupId null + audit).
  - `Messaging/WhatsApp.cs` — `WhatsAppOptions` (token only via env `WhatsApp__AccessToken`/user-secrets; `IsConfigured`), `IWhatsAppService`, `WhatsAppTemplateMessage`, `WhatsAppStatusService` ("WhatsApp integration not configured").
  - `Administration/AdminServices.cs` — `PlanAdminService` (activate/deactivate plan, list/delete plan aliases, audited), `AuditQueryService` (paged audit log + distinct actions).
  - `Dashboard/DashboardService.cs` — adds `CustomersByGroup` (+ "No group"), `CustomersByPlan`, `CustomersByPincode`, `CustomersByCity`; TotalDeliveryGroups counts active groups.
  - `DependencyInjection.cs` — registers all of the above.
- **Infrastructure**
  - `FitzobitDbContext` — DbSets `Users`, `WhatsAppMessages`; enum conversions.
  - `Configurations/EntityConfigurations.cs` — `AppUserConfiguration` (table `Users`, unique UserName), `WhatsAppMessageConfiguration`, AuditLog index on Action.
  - `Identity/IdentityPasswordHashing.cs` (PBKDF2 via `PasswordHasher<AppUser>`). **Deleted** `Identity/ConfiguredCurrentUser.cs` (config user replaced by real sign-in).
  - `Exports/CustomerExcelWriter.cs` — formatted export (navy header, autofilter, frozen row, widths, CustomerID/Phone/Pincode as text, real dates `dd-mmm-yyyy hh:mm` in IST, "Export info" sheet).
  - `Messaging/WhatsAppCloudApiService.cs` — official Cloud API (`POST {base}/{version}/{phoneNumberId}/messages`, template); not configured → returns error, writes nothing.
  - `DependencyInjection.cs` — options for Import/Export/WhatsApp; registers hashing, writer, typed HttpClient for WhatsApp. **ICurrentUser is now registered by the Web host.**
- **Web**
  - `Infrastructure/Security.cs` — `Policies.Operate` (Admin or Manager), `Policies.Admin`; `FitzobitClaims.For(user)` + `UserId()`; `HttpContextCurrentUser` ("system" at startup, "anonymous" when not signed in); `SecurityHeadersMiddleware` (CSP, nosniff, DENY framing, no-referrer, permissions-policy, `Cache-Control: no-store` on /api).
  - `Infrastructure/ApiExceptionHandler.cs` — safe errors (404/409/413/500 generic with reference id; never stack traces).
  - `Program.cs` — cookie auth (`fitzobit.auth`, HttpOnly, SameSite=Strict, Secure in non-dev or `Auth:RequireHttpsCookies`, 10 h sliding, 401/403 instead of redirects, `OnValidatePrincipal` re-checks security stamp); authorization policies with **fallback = authenticated**; antiforgery (`X-XSRF-TOKEN` header, `AutoValidateAntiforgeryToken` global filter); rate limiter policy `"auth"` (10/min/IP); HSTS outside dev; Swagger only in dev; SPA fallback + `/api/*` 404 are `.AllowAnonymous()`.

### Done in chat 2 (steps 1–3 below are COMPLETE)
- Controllers: `AuthController` (state/setup/login/logout/change-password; issues `XSRF-TOKEN` cookie), `CustomersController`
  (list, filter-options, detail, PUT, group/preview, group, export → file + `X-Export-Rows` header), `AdminControllers.cs`
  (`UsersController`, `AuditController`, `SettingsController` [Operate; `GET api/settings`, `GET api/settings/whatsapp`],
  `CatalogueAdminController` [Admin; plan status, plan-aliases, resync]). Existing controllers now `[Authorize]` (System → Admin).
- Service additions: `UserService.ChangeOwnPasswordAsync` returns the user (session re-issued); `DeliveryGroupService.PreviewUnassignAsync`;
  `CustomerQueryService.GetFilterOptionsAsync`.
- **Bug fixed:** `AutoValidateAntiforgeryTokenAttribute` needs Razor view services (500 on every request) → replaced by
  `ApiAntiforgeryFilter` (Web/Infrastructure/Security.cs): unsafe methods need `X-XSRF-TOKEN`, else 400 problem.
- appsettings: `CurrentUser` removed; `Export`, `Auth` (SessionHours, AllowRemoteSetup=false), `WhatsApp` (no token) added.
- Migration `Part3AuthAuditMessaging` created and applied to the real DB (backup of the pre-migration DB was taken in the chat-2 scratchpad).
- Tests: 133 passing. New `CustomerViewDataTests` (9) and `Web/WebSecurityTests` (10, via `Web/WebTestHost` = real host, Production env,
  https base address, temp SQLite file, test-only throwing controller).

- **Frontend (step 4) written, `npm run build` passes (not yet browser-tested):** `api.ts` (CSRF header from `XSRF-TOKEN` cookie,
  one retry after refreshing the token, 401 → `fitzobit:unauthorized` event, `download()` for export), `auth.tsx` (AuthProvider gate:
  setup / login / app), `components/UserMenu.tsx`, `ChangeGroupDialog.tsx`, `CustomerDrawer.tsx`, `BarChart.tsx`;
  pages `ViewDataPage` (URL-synced filters), `ImportDetailPage` (`/imports/:id`), Dashboard charts, Delivery Groups distribution,
  `SettingsPage` + `settings/UsersTab|PlansTab|AuditTab`. NextPhaseNotice removed.
- Shell gotcha: the Bash tool breaks on `''` inside heredocs — put such edits in a scratchpad .py file.

### Part 3 status: COMPLETE (chat 2) — steps 1–6 done, final report given to the user
- Step 5 README rewritten per spec §34. Step 6 verified: backend build 0 warnings, `npm run build` (tsc) OK, `dotnet test` 133/133,
  migration applied to the real DB, browser E2E against a scratch instance (accounts in git-ignored `samples/e2e-accounts.local.json`),
  DB verified with Python sqlite3, console/server logs checked.
- Fixes made during E2E: sign-in/setup audit attributed to the user (`IAuditLogger.RecordAs`), first admin gets LastLoginAt,
  export file name in export time zone, DB size includes `-wal`, `ExceptionHandlerMiddleware` log category set to None
  (ApiExceptionHandler logs real 500s), **import performance**: `ImportBatch.Rows` and `DeliveryGroup.Assignments` are HashSets,
  upload inserts the batch first then rows by FK inside one transaction, commit reads rows AsNoTracking
  (10k rows: upload 13 s → 2.6 s, save 19 s → 1.6 s).
- New sample: `samples/Sample_Customers_E2E_Coimbatore.xlsx` (32 fake rows incl. warning/error cases).
- **Open question for the user:** the real DB still holds the Part 2 test data (16 TST customers, 3 groups, 4 batches) and has
  no users yet. Offer reset (stop app, delete `dataitzobit.db*`, restart → first-run admin setup).

### Useful facts / gotchas
- `curl` writes `/tmp/...` that Windows Python can't read — use the session scratchpad path for temp files.
- EF + SQLite: avoid correlated `Any(...)` over large tables without a usable index; `EXPLAIN QUERY PLAN` via Python `sqlite3` helped. Nested collection projections with conditionals can need SQL APPLY (not supported by SQLite) — use joins.
- React selects/inputs can be driven in browser tests via native value setter + `dispatchEvent(new Event('change'|'input', {bubbles:true}))`; file upload via `DataTransfer`.
- Screenshot in the built-in browser sometimes times out — retry, or use `get_page_text` / JS.
- `start.ps1` builds the UI only if `wwwroot/index.html` is missing; use `-RebuildUI` after UI changes or run `npm run build`.

---

## 5. Full Part 3 brief (from the user)

```
FITZOBIT DELIVERY MANAGEMENT
PART 3 — VIEW DATA, FILTERING, EXPORT, SECURITY & FINAL PRODUCTION POLISH
Continue from Parts 1 and 2. Do NOT rebuild working functionality.
Work in E:\ClaudeWorkspace\Fitzobit. Actually run and verify the application. Do not modify unrelated projects.

2. VIEW DATA MODULE — "View & Manage Data" enterprise table. Columns: CustomerID, CustomerName, PhoneNumber, Location,
   Pincode, Plan, Delivery Group, Status, Created Date, Updated Date, Actions.
3. GLOBAL SEARCH — CustomerID, CustomerName, Phone, Location, Pincode, Plan, Delivery Group. Fast; server-side.
4. FILTERS — Group (All/Specific), Plan (All/Specific), Pincode (exact), Location (search), Status (Active/Inactive/
   Unassigned), Import Batch (specific). All filters work together.
5. SORTING — CustomerID, CustomerName, Location, Pincode, Plan, Group, Created Date, Updated Date; asc/desc; server-side.
6. PAGINATION — 10/25/50/100 rows/page; "Showing 1–25 of 2,500 customers"; never load all customers into the browser.
7. TABLE UX — column visibility, reset columns, row selection, sticky header, clear filters, filter summary, responsive
   horizontal scrolling. Keep default interface clean.
8. CUSTOMER DETAILS — Customer (CustomerID, Name, Phone, Location, Pincode, Plan), Delivery (Current Group, Assignment
   Date, Assignment History), Import (Import Batch, Created, Updated). Actions: Edit, Change Group, View History.
9. EDIT CUSTOMER — authorized users; validate; record audit; do not silently overwrite important fields.
10. EXPORT TO EXCEL — Export All / Filtered / Selected. CRITICAL: export must respect active filters (e.g. Group Coimbatore
    North + Plan Medium Fit Box + Pincode 641002 → only matching records).
11. EXPORT FORMAT — CustomerID, CustomerName, PhoneNumber, Location, Pincode, Plan, DeliveryGroup, Status, CreatedAt,
    UpdatedAt; header formatting, autofilter, frozen header, column widths, phone & pincode as text, correct dates,
    no scientific notation.
12. IMPORT HISTORY — File Name, Uploaded Date, Uploaded By, Total Rows, Valid, Warnings, Errors, Imported, Status.
    Open an import: Summary, Records, Errors, Warnings.
13. DELIVERY GROUP MODULE — Total Groups, Assigned Customers, Unassigned Customers, Group distribution. Each group: Name,
    Description, Customer Count, Pincodes, Locations, Plans, Status, Actions.
14. DASHBOARD — real SQLite queries. Cards: Total Customers, Total Delivery Groups, Unassigned Customers, Unique
    Locations, Unique Pincodes, Active Plans, Recent Imports, Catalogue Items. Charts: Customers by Delivery Group,
    Customers by Plan, Customers by Location/Pincode, Recent Imports. No meaningless charts.
15. CATALOGUE / PLANS — from Fitzobit_WhatsApp_Catalogue.xlsx: item, category, display price, pricing tiers, billing
    period, meal count, description, availability, warning.
16. CATALOGUE PRICE WARNINGS — preserve and display inconsistencies (Medium monthly ₹699 vs Veg ₹2,699 / Egg ₹2,899;
    salads weekly vs monthly). Do NOT silently correct.
17. AUTHENTICATION — Roles: Admin (everything); Manager (upload, customers, groups, export, view, catalogue).
    Backend authorization mandatory (not only hidden buttons).
18. SECURITY — secure uploads, size limits, file validation, input validation, authorization, secure configuration,
    CSRF where applicable, secure headers, safe error handling, SQL injection protection via EF Core, no exposed secrets.
    Never expose passwords, connection strings, tokens, stack traces.
19. SQLITE — keep SQLite (data\fitzobit.db); no PostgreSQL now; keep provider switchable; no SQLite-specific business
    logic in the domain.
20. FUTURE POSTGRESQL — document SQLite → PostgreSQL migration in README; do not perform it.
21. WHATSAPP ARCHITECTURE — official WhatsApp Business API abstraction (IWhatsAppService). Do NOT scrape/automate
    WhatsApp Web, store WhatsApp passwords, hard-code tokens, or pretend a message was sent. If not configured show
    "WhatsApp integration not configured". App works normally without WhatsApp.
22. FUTURE WHATSAPP FEATURES — architecture allows customer notifications, delivery/plan/subscription reminders,
    delivery-group notifications, message templates, message history, delivery status, read status. No fake messaging.
23. SETTINGS — General, Users, Plans, Catalogue, Delivery Groups, WhatsApp, Import Settings, Export Settings.
    Protect administrative settings.
24. AUDIT — who, what, when, old value, new value, relevant entity for important changes.
25. UI POLISH — match the attached reference image's visual language (navy sidebar, blue primary, light workspace,
    rounded cards): premium, modern, professional, fast, clean, operational. Improve spacing, typography, tables,
    responsive behaviour, empty states.
26. LOADING STATES — uploading, validating, classifying, saving, loading customers, exporting, loading groups, loading history.
27. EMPTY STATES — "No customers yet" / "Upload your first Fitzobit customer Excel file to begin."; "No delivery groups
    created" / "Create a group to organize deliveries."; "No customers match your current filters."
28. ERROR HANDLING — never expose internals; useful user messages; technical details to logs.
29. PERFORMANCE — server-side pagination/filtering/sorting, efficient EF queries, indexes, no N+1, efficient Excel
    import/export; test with realistic volumes.
30. TESTING — unit: validation, classification, pincode extraction, plan mapping, group assignment, price warnings,
    export, authorization. Integration: SQLite persistence, import, assignment, customer updates, export. End-to-end:
    upload, validate, classify, create group, assign, save, search, filter, sort, export.
31. FINAL E2E (actually perform): start app; verify SQLite DB; login; dashboard; download template; create sample Excel;
    upload; validate; review classification; create 2–3 groups; assign; review; confirm; save; verify data in SQLite;
    open View Data; search; filter by group; filter by pincode; filter by plan; sort; open customer; edit customer;
    reassign group; verify assignment history; export filtered records; open exported Excel; verify Import History;
    verify dashboard statistics. Fix problems found. Do not claim it works without testing.
32. DATABASE VERIFICATION — tables, relationships, constraints, indexes, customers, groups, assignments, history,
    import batches, audit logs, catalogue.
33. BUILD VERIFICATION — backend build, frontend build, tests, lint where applicable, migration verification; check
    browser console, server logs, runtime errors; fix obvious issues.
34. README — overview, architecture, requirements, SQLite setup, DB location, EF migrations, running, Excel template,
    import workflow, delivery groups, View Data, filtering, export, authentication, WhatsApp architecture, future
    PostgreSQL migration, testing, deployment, troubleshooting.
35. DEFINITION OF DONE — real app (not mockup/fake): real SQLite persistence, Excel upload, validation, classification,
    customers, groups, assignments, history, search, filters, sorting, pagination, Excel export, import history, audit
    history, authentication, actual catalogue data, professional UI, no exposed secrets, no fake production data.
36. FINAL REPORT — 1 Architecture, 2 SQLite path, 3 Tables, 4 Migrations, 5 APIs, 6 Frontend modules, 7 Excel
    functionality, 8 Group functionality, 9 Filtering, 10 Export, 11 Authentication, 12 WhatsApp status, 13 Tests
    performed, 14 How to run, 15 Environment variables, 16 Known limitations, 17 Future PostgreSQL approach.
```

---

## 6. Prompt to paste into the new chat

```
Continue building the Fitzobit Delivery Management app (Part 3). First read
E:\ClaudeWorkspace\Fitzobit\HANDOFF.md completely — it has the project state, architecture, what is already written
for Part 3, the remaining work in order, gotchas, and the full Part 3 brief. Then continue from
"Still to do for Part 3", step 1 (controllers). Do not rebuild working functionality, do not run git.
```
