// Typed client for the Fitzobit API. Shapes mirror the C# DTOs in Fitzobit.Application.

export interface CatalogueWarning {
  code: string;
  title: string;
  message: string;
}

export interface CataloguePricingTier {
  id: number;
  tierName: string;
  price: number;
  currency: string;
  billingPeriod: string;
  mealCount: number | null;
  description: string | null;
  source: string | null;
}

export interface CatalogueItem {
  id: number;
  name: string;
  category: string | null;
  term: string | null;
  description: string | null;
  displayPrice: number;
  currency: string;
  billingPeriod: string;
  durationText: string | null;
  durationDays: number | null;
  mealCount: number | null;
  includedItems: string | null;
  availability: string | null;
  displayOrder: number;
  source: string | null;
  sourceNotes: string | null;
  planCode: string | null;
  pricingTiers: CataloguePricingTier[];
  warnings: CatalogueWarning[];
}

export interface CategorySummary {
  name: string;
  itemCount: number;
  warningCount: number;
}

export interface CatalogueOverview {
  items: CatalogueItem[];
  categories: CategorySummary[];
  totalItems: number;
  totalPricingTiers: number;
  itemsWithWarnings: number;
  source: string | null;
  lastUpdatedAt: string | null;
}

export interface Plan {
  id: number;
  name: string;
  code: string;
  category: string | null;
  description: string | null;
  status: string;
  catalogueItemId: number | null;
  customerCount: number;
}

export interface ImportBatch {
  id: number;
  fileName: string;
  uploadedAt: string;
  uploadedBy: string | null;
  totalRows: number;
  validRows: number;
  invalidRows: number;
  warningRows: number;
  importedCount: number;
  customersCreated: number;
  groupsCreated: number;
  assignmentsChanged: number;
  status: string;
  failureReason: string | null;
  validationDurationMs: number;
  saveDurationMs: number;
  completedAt: string | null;
}

export interface DashboardSummary {
  totalCustomers: number;
  totalDeliveryGroups: number;
  uniqueLocations: number;
  uniquePincodes: number;
  activePlans: number;
  unassignedCustomers: number;
  recentImports: number;
  recentImportWindowDays: number;
  catalogueItems: number;
  cataloguePricingTiers: number;
  catalogueItemsWithWarnings: number;
  latestImports: ImportBatch[];
  catalogueCategories: CategorySummary[];
  customersByGroup: ChartItem[];
  customersByPlan: ChartItem[];
  customersByPincode: ChartItem[];
  customersByCity: ChartItem[];
}

export interface ChartItem {
  key: string;
  label: string;
  count: number;
}

export interface DeliveryGroup {
  id: number;
  name: string;
  description: string | null;
  status: string;
  activeCustomerCount: number;
  locationCount: number;
  pincodeCount: number;
  pincodes: string[];
  localities: string[];
  planCount: number;
  plans: string[];
  createdAt: string;
  createdBy: string | null;
  updatedAt: string;
}

export interface DeliveryGroupOverview {
  groups: DeliveryGroup[];
  unassignedCustomers: number;
  totalCustomers: number;
}

export interface GroupCustomer {
  id: number;
  customerId: string;
  customerName: string;
  phoneNumber: string;
  location: string | null;
  pincode: string | null;
  locality: string | null;
  city: string | null;
  planName: string | null;
  assignedAt: string | null;
}

export interface MovePreview {
  selected: number;
  willMove: number;
  alreadyInTarget: number;
  targetName: string;
  messages: string[];
}

// ---------- Import workflow ----------

export interface ImportIssue {
  code: string;
  severity: 'Warning' | 'Error';
  message: string;
  field: string | null;
}

export interface GroupRef {
  key: string;
  name: string;
  isNew: boolean;
}

export interface ImportRow {
  id: number;
  rowNumber: number;
  customerId: string | null;
  customerName: string | null;
  phoneNumber: string | null;
  normalizedPhoneNumber: string | null;
  location: string | null;
  normalizedLocation: string | null;
  pincode: string | null;
  locality: string | null;
  city: string | null;
  plan: string | null;
  planId: number | null;
  planName: string | null;
  tierName: string | null;
  status: 'Valid' | 'Warning' | 'Error';
  issues: ImportIssue[];
  isExistingCustomer: boolean;
  isPotentialDuplicate: boolean;
  included: boolean;
  excludedByManager: boolean;
  group: GroupRef | null;
  existingGroupName: string | null;
  suggestedGroup: GroupRef | null;
  suggestionReason: string | null;
}

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface ImportSummary {
  totalRows: number;
  validRows: number;
  warningRows: number;
  errorRows: number;
  newCustomers: number;
  existingCustomers: number;
  duplicates: number;
  locations: number;
  pincodes: number;
  plans: number;
  toImport: number;
  excludedByManager: number;
  unassigned: number;
  keepingCurrentGroup: number;
  missingPincode: number;
  unresolvedPlanMappings: number;
}

export interface ImportDetail {
  id: number;
  fileName: string;
  fileSizeBytes: number;
  sheetName: string | null;
  uploadedAt: string;
  uploadedBy: string | null;
  status: 'Pending' | 'Validated' | 'Completed' | 'Failed' | 'Cancelled';
  failureReason: string | null;
  fileMessages: string[];
  emptyRowsSkipped: number;
  validationDurationMs: number;
  saveDurationMs: number;
  completedAt: string | null;
  importedCount: number;
  customersCreated: number;
  existingCustomersMatched: number;
  groupsCreated: number;
  assignmentsChanged: number;
  summary: ImportSummary | null;
}

export interface CountItem {
  key: string;
  label: string;
  count: number;
}

export interface ImportClassification {
  includedRows: number;
  newCustomers: number;
  existingCustomers: number;
  withoutPincode: number;
  withoutGroup: number;
  potentialDuplicates: number;
  byLocation: CountItem[];
  byLocality: CountItem[];
  byCity: CountItem[];
  byPincode: CountItem[];
  byPlan: CountItem[];
  byCategory: CountItem[];
}

export interface PlanOption {
  id: number;
  name: string;
  code: string;
  category: string | null;
  tiers: { id: number; name: string; price: number; billingPeriod: string }[];
}

export interface PlanMapping {
  id: number;
  planKey: string;
  exampleText: string;
  rowCount: number;
  status: 'Mapped' | 'Ambiguous' | 'Unknown';
  method: 'None' | 'SavedAlias' | 'ExactName' | 'Keywords' | 'Manual';
  planId: number | null;
  planName: string | null;
  pricingTierId: number | null;
  tierName: string | null;
  candidatePlanIds: number[];
  note: string | null;
}

export interface ImportGroupSummary {
  key: string;
  name: string;
  description: string | null;
  isNew: boolean;
  status: string;
  customerCount: number;
  newCustomerCount: number;
  existingMembersOutsideImport: number;
  pincodeCount: number;
  pincodes: string[];
  locationCount: number;
  localities: string[];
  planCount: number;
  plans: string[];
}

export interface IssueCount {
  code: string;
  label: string;
  count: number;
}

export interface ImportReview {
  summary: ImportSummary;
  groups: ImportGroupSummary[];
  unassigned: number;
  unknownPlan: number;
  missingPincode: number;
  groupChangesForExistingCustomers: number;
  warnings: IssueCount[];
  excludedErrors: IssueCount[];
  canSave: boolean;
  blockers: string[];
}

export interface AssignmentPreview {
  selectedRows: number;
  willChange: number;
  alreadyInTarget: number;
  targetName: string | null;
  moves: { fromKey: string | null; fromName: string; count: number }[];
  messages: string[];
}

export interface SuggestionResult {
  rowsWithSuggestion: number;
  differentFromCurrent: number;
  byRule: CountItem[];
}

export interface CommitResult {
  batchId: number;
  customersCreated: number;
  existingCustomersMatched: number;
  groupsCreated: number;
  assignmentsChanged: number;
  importedCount: number;
  skippedRows: number;
  durationMs: number;
}

export interface UploadResult {
  batchId: number;
  status: string;
  errors: string[];
}

export interface RowFilter {
  status?: string;
  search?: string;
  pincode?: string;
  locality?: string;
  city?: string;
  location?: string;
  group?: string;
  planId?: number;
  issueCode?: string;
  existing?: boolean;
  hasSuggestion?: boolean;
}

export interface RowSelection {
  rowIds?: number[];
  filter?: RowFilter;
}

export interface SystemInfo {
  applicationName: string;
  version: string;
  databaseProvider: string;
  databaseFile: string | null;
  databaseSizeBytes: number | null;
  appliedMigrations: string[];
  pendingMigrations: string[];
  tables: string[];
  catalogueSourceFile: string;
  catalogueSourceExists: boolean;
  userName: string;
  userDisplayName: string;
  userRole: string;
}

// ---------- Auth & View Data ----------

export interface SignedInUser {
  id: number;
  userName: string;
  displayName: string;
  role: string;
}

export interface AuthState {
  setupRequired: boolean;
  setupAllowedHere: boolean;
  setupCodeRequired: boolean;
  authenticated: boolean;
  user: SignedInUser | null;
}

export interface CustomerFilter {
  search?: string;
  group?: string;
  planId?: number;
  pincode?: string;
  location?: string;
  status?: string;
  importBatchId?: number;
  sort?: string;
  desc?: boolean;
}

export interface CustomerListItem {
  id: number;
  customerId: string;
  customerName: string;
  phoneNumber: string;
  location: string | null;
  pincode: string | null;
  planId: number | null;
  planName: string | null;
  groupId: number | null;
  groupName: string | null;
  status: string;
  createdAt: string;
  updatedAt: string;
}

export interface FilterOption {
  id: number;
  name: string;
  isActive: boolean;
}

export interface CustomerFilterOptions {
  groups: FilterOption[];
  plans: FilterOption[];
  imports: { id: number; fileName: string; uploadedAt: string; importedCount: number }[];
  pageSizes: number[];
}

export interface AssignmentHistoryEntry {
  changedAt: string;
  changedBy: string | null;
  previousGroupId: number | null;
  previousGroupName: string | null;
  newGroupId: number | null;
  newGroupName: string | null;
  reason: string | null;
}

export type JsonValue = string | number | boolean | null | JsonValue[] | { [key: string]: JsonValue };

export interface AuditEntry {
  id: number;
  occurredAt: string;
  action: string;
  entityType: string;
  entityKey: string | null;
  summary: string;
  performedBy: string | null;
  oldValues: Record<string, JsonValue> | null;
  newValues: Record<string, JsonValue> | null;
  details: JsonValue;
}

export interface CustomerDetail {
  id: number;
  customerId: string;
  customerName: string;
  phoneNumber: string;
  normalizedPhoneNumber: string | null;
  location: string | null;
  normalizedLocation: string | null;
  pincode: string | null;
  locality: string | null;
  city: string | null;
  planId: number | null;
  planName: string | null;
  planCategory: string | null;
  pricingTierId: number | null;
  tierName: string | null;
  originalPlan: string | null;
  status: string;
  groupId: number | null;
  groupName: string | null;
  assignedAt: string | null;
  assignedBy: string | null;
  history: AssignmentHistoryEntry[];
  importBatch: { id: number; fileName: string; uploadedAt: string; uploadedBy: string | null } | null;
  audit: AuditEntry[];
  createdAt: string;
  createdBy: string | null;
  updatedAt: string;
  updatedBy: string | null;
}

export interface UpdateCustomerRequest {
  customerName: string;
  phoneNumber: string;
  location: string | null;
  planId: number | null;
  pricingTierId: number | null;
  status: string;
  expectedUpdatedAt: string;
}

export interface UpdateCustomerResult {
  changed: boolean;
  changedFields: string[];
  warnings: string[];
}

export interface BulkResult {
  requested: number;
  changed: number;
  alreadyInGroup: number;
}

export type ExportScope = 'All' | 'Filtered' | 'Selected';

export interface AppUser {
  id: number;
  userName: string;
  displayName: string;
  role: string;
  isActive: boolean;
  lastLoginAt: string | null;
  createdAt: string;
  isLockedOut: boolean;
}

export interface WhatsAppStatus {
  configured: boolean;
  message: string;
  apiVersion: string;
  phoneNumberIdMasked: string | null;
  plannedFeatures: string[];
  messagesRecorded: number;
  messagesByStatus: Record<string, number>;
}

export interface AppSettings {
  import: { maxFileSizeBytes: number; maxRows: number; acceptedFileType: string };
  export: { maxRows: number; timeZone: string; columns: string[] };
  locations: { knownCities: string[]; cityAliases: Record<string, string>; regionWords: string[] };
  catalogue: { fileName: string; exists: boolean; syncOnStartup: boolean };
  security: { sessionHours: number; passwordMinLength: number; maxFailedAttempts: number; lockoutMinutes: number };
  whatsApp: WhatsAppStatus;
}

export interface PlanAlias {
  id: number;
  originalText: string;
  aliasKey: string;
  planId: number;
  planName: string;
  tierName: string | null;
  createdAt: string;
  createdBy: string | null;
}

export interface CatalogueSeedResult {
  skipped: boolean;
  skipReason: string | null;
  itemsCreated: number;
  itemsUpdated: number;
  itemsUnchanged: number;
  tiersCreated: number;
  tiersUpdated: number;
  tiersRemoved: number;
  plansCreated: number;
  itemsNotInSource: number;
}

// ---------- HTTP ----------

/** Fired when the server says the session is missing or expired; the auth provider then shows the sign-in page. */
export const UNAUTHORIZED_EVENT = 'fitzobit:unauthorized';

/** The CSRF request token, issued by GET /api/auth/state in a JS-readable cookie. */
function csrfToken(): string {
  const match = document.cookie.split('; ').find((c) => c.startsWith('XSRF-TOKEN='));
  return match ? decodeURIComponent(match.slice('XSRF-TOKEN='.length)) : '';
}

async function readError(response: Response): Promise<Error> {
  let detail = `${response.status} ${response.statusText}`;
  try {
    const problem = await response.json();
    if (problem?.detail) detail = problem.detail;
    else if (problem?.title) detail = problem.title;
  } catch {
    /* body was not JSON */
  }
  if (response.status === 403) detail = 'You do not have permission to do this.';
  return new Error(detail);
}

/** Every API call: same-origin cookies, CSRF header on state-changing requests, 401 → sign-in page. */
async function call(path: string, init: RequestInit = {}, retried = false): Promise<Response> {
  const unsafe = !!init.method && init.method !== 'GET';
  const headers = new Headers(init.headers);
  headers.set('Accept', 'application/json');
  if (unsafe) headers.set('X-XSRF-TOKEN', csrfToken());
  const response = await fetch(path, { ...init, headers, credentials: 'same-origin' });
  // A missing/stale CSRF token (e.g. cookies cleared): fetch a fresh one and try once more.
  if (response.status === 400 && unsafe && !retried) {
    const problem = await response.clone().json().catch(() => null);
    if (problem?.title === 'Request could not be verified') {
      await fetch('/api/auth/state', { credentials: 'same-origin' });
      return call(path, init, true);
    }
  }
  if (response.status === 401 && !path.startsWith('/api/auth/')) window.dispatchEvent(new Event(UNAUTHORIZED_EVENT));
  if (!response.ok) throw await readError(response);
  return response;
}

async function get<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await call(path, { signal });
  return (await response.json()) as T;
}

async function send<T = void>(method: 'POST' | 'PUT' | 'DELETE', path: string, body?: unknown): Promise<T> {
  const response = await call(path, {
    method,
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (response.status === 204) return undefined as T;
  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

/** POSTs and saves the returned file through a temporary link. */
async function download(path: string, body: unknown): Promise<{ fileName: string; rows: number | null }> {
  const response = await call(path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  const disposition = response.headers.get('Content-Disposition') ?? '';
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(disposition)?.[1];
  const fileName = encoded ? decodeURIComponent(encoded) : (/filename="?([^";]+)"?/i.exec(disposition)?.[1] ?? 'export.xlsx');
  const rows = response.headers.get('X-Export-Rows');
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
  return { fileName, rows: rows === null ? null : Number(rows) };
}

function query(params: Record<string, string | number | boolean | undefined | null>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) if (value !== undefined && value !== null && value !== '') search.set(key, String(value));
  const s = search.toString();
  return s ? `?${s}` : '';
}

export const templateUrl = '/api/imports/template';

export const api = {
  dashboard: (signal?: AbortSignal) => get<DashboardSummary>('/api/dashboard', signal),
  catalogue: (signal?: AbortSignal) => get<CatalogueOverview>('/api/catalogue', signal),
  plans: (signal?: AbortSignal) => get<Plan[]>('/api/catalogue/plans', signal),
  planOptions: (signal?: AbortSignal) => get<PlanOption[]>('/api/catalogue/plan-options', signal),
  system: (signal?: AbortSignal) => get<SystemInfo>('/api/system', signal),

  deliveryGroups: (signal?: AbortSignal) => get<DeliveryGroupOverview>('/api/delivery-groups', signal),
  createGroup: (name: string, description: string | null) => send<{ id: number; name: string }>('POST', '/api/delivery-groups', { name, description }),
  updateGroup: (id: number, name: string, description: string | null) => send('PUT', `/api/delivery-groups/${id}`, { name, description }),
  setGroupActive: (id: number, active: boolean) => send('POST', `/api/delivery-groups/${id}/${active ? 'activate' : 'deactivate'}`),
  deleteGroup: (id: number) => send('DELETE', `/api/delivery-groups/${id}`),
  groupCustomers: (group: string, search: string, page: number, pageSize: number, signal?: AbortSignal) =>
    get<Paged<GroupCustomer>>(`/api/delivery-groups/${group}/customers${query({ search, page, pageSize })}`, signal),
  previewMove: (customerIds: number[], targetGroupId: number) =>
    send<MovePreview>('POST', '/api/delivery-groups/move/preview', { customerIds, targetGroupId }),
  move: (customerIds: number[], targetGroupId: number, reason: string | null) =>
    send<{ requested: number; changed: number; alreadyInGroup: number }>('POST', '/api/delivery-groups/move', { customerIds, targetGroupId, reason }),

  imports: (signal?: AbortSignal) => get<ImportBatch[]>('/api/imports', signal),
  upload: async (file: File): Promise<UploadResult> => {
    const form = new FormData();
    form.append('file', file);
    const response = await call('/api/imports', { method: 'POST', body: form });
    return (await response.json()) as UploadResult;
  },
  importDetail: (id: number, signal?: AbortSignal) => get<ImportDetail>(`/api/imports/${id}`, signal),
  importRows: (id: number, filter: RowFilter, page: number, pageSize: number, signal?: AbortSignal) =>
    get<Paged<ImportRow>>(`/api/imports/${id}/rows${query({ ...filter, page, pageSize })}`, signal),
  classification: (id: number, signal?: AbortSignal) => get<ImportClassification>(`/api/imports/${id}/classification`, signal),
  planMappings: (id: number, signal?: AbortSignal) => get<PlanMapping[]>(`/api/imports/${id}/plan-mappings`, signal),
  updatePlanMapping: (id: number, mappingId: number, planId: number | null, pricingTierId: number | null) =>
    send('PUT', `/api/imports/${id}/plan-mappings/${mappingId}`, { planId, pricingTierId }),
  setExcluded: (id: number, rowIds: number[], excluded: boolean) =>
    send<{ updated: number }>('POST', `/api/imports/${id}/rows/exclusion`, { rowIds, excluded }),
  importGroups: (id: number, signal?: AbortSignal) => get<ImportGroupSummary[]>(`/api/imports/${id}/groups`, signal),
  createDraftGroup: (id: number, name: string, description: string | null) =>
    send<{ key: string; name: string }>('POST', `/api/imports/${id}/groups`, { name, description }),
  updateDraftGroup: (id: number, draftId: number, name: string, description: string | null) =>
    send('PUT', `/api/imports/${id}/groups/${draftId}`, { name, description }),
  deleteDraftGroup: (id: number, draftId: number) => send<{ unassigned: number }>('DELETE', `/api/imports/${id}/groups/${draftId}`),
  previewAssignment: (id: number, selection: RowSelection, target: string | null) =>
    send<AssignmentPreview>('POST', `/api/imports/${id}/assignments/preview`, { selection, target }),
  assign: (id: number, selection: RowSelection, target: string | null) =>
    send<{ updated: number }>('POST', `/api/imports/${id}/assignments`, { selection, target }),
  suggest: (id: number) => send<SuggestionResult>('POST', `/api/imports/${id}/suggestions`),
  previewSuggestions: (id: number, rowIds: number[] | null, onlyUnassigned: boolean) =>
    send<AssignmentPreview>('POST', `/api/imports/${id}/suggestions/preview`, { rowIds, onlyUnassigned }),
  applySuggestions: (id: number, rowIds: number[] | null, onlyUnassigned: boolean) =>
    send<{ updated: number }>('POST', `/api/imports/${id}/suggestions/apply`, { rowIds, onlyUnassigned }),
  review: (id: number, signal?: AbortSignal) => get<ImportReview>(`/api/imports/${id}/review`, signal),
  commit: (id: number) => send<CommitResult>('POST', `/api/imports/${id}/commit`),
  cancelImport: (id: number) => send('POST', `/api/imports/${id}/cancel`),

  authState: () => get<AuthState>('/api/auth/state'),
  login: (userName: string, password: string) => send<SignedInUser>('POST', '/api/auth/login', { userName, password }),
  setup: (userName: string, displayName: string, password: string, setupCode?: string) =>
    send<SignedInUser>('POST', '/api/auth/setup', { userName, displayName, password, setupCode }),
  logout: () => send('POST', '/api/auth/logout'),
  changePassword: (currentPassword: string, newPassword: string) => send('POST', '/api/auth/change-password', { currentPassword, newPassword }),

  customers: (filter: CustomerFilter, page: number, pageSize: number, signal?: AbortSignal) =>
    get<Paged<CustomerListItem>>(`/api/customers${query({ ...filter, page, pageSize })}`, signal),
  customerFilterOptions: (signal?: AbortSignal) => get<CustomerFilterOptions>('/api/customers/filter-options', signal),
  customer: (id: number, signal?: AbortSignal) => get<CustomerDetail>(`/api/customers/${id}`, signal),
  updateCustomer: (id: number, request: UpdateCustomerRequest) => send<UpdateCustomerResult>('PUT', `/api/customers/${id}`, request),
  previewCustomerGroup: (customerIds: number[], groupId: number | null) =>
    send<MovePreview>('POST', '/api/customers/group/preview', { customerIds, groupId }),
  changeCustomerGroup: (customerIds: number[], groupId: number | null, reason: string | null) =>
    send<BulkResult>('POST', '/api/customers/group', { customerIds, groupId, reason }),
  exportCustomers: (scope: ExportScope, filter: CustomerFilter, ids: number[] | null) =>
    download('/api/customers/export', { scope, filter, ids }),

  settings: (signal?: AbortSignal) => get<AppSettings>('/api/settings', signal),
  users: (signal?: AbortSignal) => get<AppUser[]>('/api/users', signal),
  createUser: (userName: string, displayName: string, role: string, password: string) =>
    send<AppUser>('POST', '/api/users', { userName, displayName, role, password }),
  updateUser: (id: number, displayName: string, role: string, isActive: boolean) => send('PUT', `/api/users/${id}`, { displayName, role, isActive }),
  resetPassword: (id: number, newPassword: string) => send('POST', `/api/users/${id}/reset-password`, { newPassword }),
  audit: (params: { entityType?: string; action?: string; search?: string }, page: number, pageSize: number, signal?: AbortSignal) =>
    get<Paged<AuditEntry>>(`/api/audit${query({ ...params, page, pageSize })}`, signal),
  auditActions: (signal?: AbortSignal) => get<string[]>('/api/audit/actions', signal),
  setPlanStatus: (id: number, active: boolean) => send('PUT', `/api/catalogue/plans/${id}/status`, { active }),
  planAliases: (signal?: AbortSignal) => get<PlanAlias[]>('/api/catalogue/plan-aliases', signal),
  deletePlanAlias: (id: number) => send('DELETE', `/api/catalogue/plan-aliases/${id}`),
  resyncCatalogue: () => send<CatalogueSeedResult>('POST', '/api/catalogue/resync'),
};
