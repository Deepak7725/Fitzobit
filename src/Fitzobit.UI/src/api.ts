// Typed client for the Fitzobit API. Shapes mirror the C# DTOs in Fitzobit.Application.
// Dates without a time (start/end/pause dates) are "yyyy-mm-dd" strings; timestamps are ISO strings in UTC.

export interface ChartItem {
  key: string;
  label: string;
  count: number;
}

export interface StatusCount {
  id: number;
  /** Stable dictionary code: ACTIVE, PAUSED, INACTIVE, … */
  code: string;
  name: string;
  count: number;
}

export interface RecentChange {
  id: number;
  occurredAt: string;
  action: string;
  customerId: string | null;
  /** Internal id for opening the customer (null if it no longer exists). */
  customerKey: number | null;
  summary: string;
  performedBy: string | null;
}

export interface DashboardSummary {
  totalCustomers: number;
  totalPackages: number;
  activeCustomers: number;
  pausedCustomers: number;
  inactiveCustomers: number;
  totalDeliveryGroups: number;
  unassignedCustomers: number;
  unassignedActiveCustomers: number;
  withCoordinates: number;
  withoutCoordinates: number;
  approximateCoordinates: number;
  endingSoon: number;
  endingSoonWindowDays: number;
  incompleteCustomers: number;
  customersByStatus: StatusCount[];
  customersByGroup: ChartItem[];
  customersByMealType: ChartItem[];
  customersByFrequency: ChartItem[];
  customersByPincode: ChartItem[];
  customersByCity: ChartItem[];
  recentChanges: RecentChange[];
}

export interface CountItem {
  label: string;
  count: number;
}

export interface DeliveryGroup {
  id: number;
  groupNumber: number;
  /** "Group 3" */
  name: string;
  notes: string | null;
  status: string;
  activeCustomerCount: number;
  locationCount: number;
  pincodeCount: number;
  pincodes: string[];
  localities: string[];
  mealTypes: CountItem[];
  frequencies: CountItem[];
  statusCounts: CountItem[];
  locatedCount: number;
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
  address: string | null;
  pincode: string | null;
  locality: string | null;
  city: string | null;
  mealType: string | null;
  status: string;
  hasLocation: boolean;
  assignedAt: string | null;
}

export interface MovePreview {
  selected: number;
  willMove: number;
  alreadyInTarget: number;
  targetName: string;
  messages: string[];
}

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface SystemInfo {
  applicationName: string;
  version: string;
  databaseProvider: string;
  database: string;
  databaseSizeBytes: number | null;
  appliedMigrations: string[];
  pendingMigrations: string[];
  tables: string[];
  userName: string;
  userDisplayName: string;
  userRole: string;
}

// ---------- Drop-down values (DictionaryEntries) ----------

export type DictionaryType = 'Preference' | 'MealType' | 'Frequency' | 'CustomerStatus';

export interface DictionaryOption {
  id: number;
  code: string;
  displayName: string;
  sortOrder: number;
  /** Frequency: delivery days per package. */
  numericValue: number | null;
  /** Frequency: packages of this frequency may pause delivery days. */
  canPause: boolean;
}

export interface DictionaryOptions {
  preference: DictionaryOption[];
  mealType: DictionaryOption[];
  frequency: DictionaryOption[];
  customerStatus: DictionaryOption[];
}

export interface DictionaryEntry {
  id: number;
  dictionaryType: DictionaryType;
  code: string;
  displayName: string;
  sortOrder: number;
  isActive: boolean;
  isSystem: boolean;
  numericValue: number | null;
  canPause: boolean;
  customerCount: number;
  createdAt: string;
  updatedAt: string;
  updatedBy: string | null;
}

export interface SaveDictionaryEntry {
  displayName: string;
  numericValue: number | null;
  /** Frequency only; omitted keeps the current setting. */
  canPause?: boolean;
  isActive: boolean;
}

// ---------- Auth & customers ----------

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
  preferenceId?: number;
  mealTypeId?: number;
  frequencyId?: number;
  statusId?: number;
  pincode?: string;
  address?: string;
  incomplete?: boolean;
  /** located | approximate | missing */
  geo?: string;
  sort?: string;
  desc?: boolean;
}

export interface NoteSummary {
  id: number;
  text: string;
  createdAt: string;
  /** The writer's display name. */
  createdBy: string | null;
}

export interface CustomerListItem {
  id: number;
  customerId: string;
  customerName: string;
  phoneNumber: string;
  alternatePhoneNumber: string | null;
  address: string | null;
  pincode: string | null;
  /** "HH:mm:ss" */
  preferredDeliveryTime: string | null;
  packages: PackageSummary[];
  /** All notes, newest first. */
  notes: NoteSummary[];
  /** Earliest start / latest end of the customer's packages. */
  startDate: string | null;
  endDate: string | null;
  status: string;
  statusCode: string;
  groupId: number | null;
  groupNumber: number | null;
  latitude: number | null;
  longitude: number | null;
  geocodeStatus: GeocodeStatus;
  geocodeMatchLevel: GeoMatchLevel | null;
  createdAt: string;
  updatedAt: string;
}

export type GeocodeStatus = 'Pending' | 'Located' | 'NotFound' | 'Failed';
export type GeoMatchLevel = 'Address' | 'Locality' | 'Pincode';

export interface FilterOption {
  id: number;
  name: string;
  isActive: boolean;
}

export interface CustomerFilterOptions {
  groups: FilterOption[];
  preferences: FilterOption[];
  mealTypes: FilterOption[];
  frequencies: FilterOption[];
  statuses: FilterOption[];
  pageSizes: number[];
}

export interface AssignmentHistoryEntry {
  changedAt: string;
  changedBy: string | null;
  previousGroupId: number | null;
  previousGroupNumber: number | null;
  newGroupId: number | null;
  newGroupNumber: number | null;
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

export interface PackageSummary {
  id: number;
  preference: string;
  mealType: string;
  quantity: number;
  frequency: string;
  startDate: string;
  endDate: string;
  pausedCount: number;
  /** Every paused day (pausedCount counts only those that extended the end date). */
  pausedDays: number;
  /** "Veg Medium ×1 · Monthly (12 Oct 2026 – 10 Nov 2026)" */
  text: string;
}

export interface CustomerPackage {
  id: number;
  preferenceId: number;
  preference: string;
  mealTypeId: number;
  mealType: string;
  quantity: number;
  frequencyId: number;
  frequency: string;
  frequencyDeliveryDays: number | null;
  /** The package's frequency allows pausing. */
  canPause: boolean;
  startDate: string;
  endDate: string;
  deliveryDaysLeft: number | null;
  /** Paused days that extended the end date (at most maxPauses); pauses lists every paused day. */
  pausedCount: number;
  maxPauses: number;
  pauses: CustomerPause[];
  /** The first delivery day has not come yet. */
  canDelete: boolean;
  createdAt: string;
  createdBy: string | null;
  updatedAt: string;
  updatedBy: string | null;
}

export interface CustomerNote {
  id: number;
  text: string;
  createdAt: string;
  createdBy: string | null;
  createdByName: string | null;
  updatedAt: string;
  updatedBy: string | null;
  edited: boolean;
  /** The signed-in user wrote it or is an Admin. */
  canEdit: boolean;
}

export interface CustomerPause {
  pausedDate: string;
  previousEndDate: string;
  newEndDate: string;
  /** This pause moved the end date (only a package's first paused days do). */
  extendedEndDate: boolean;
  reason: string | null;
  createdAt: string;
  createdBy: string | null;
}

export interface CustomerDetail {
  id: number;
  customerId: string;
  customerName: string;
  phoneNumber: string;
  normalizedPhoneNumber: string | null;
  alternatePhoneNumber: string | null;
  address: string | null;
  pincode: string | null;
  locality: string | null;
  city: string | null;
  /** "HH:mm:ss" */
  preferredDeliveryTime: string | null;
  packages: CustomerPackage[];
  /** Finished packages moved aside by the daily 12 pm run (newest first); read-only. */
  pastPackages: CustomerPackage[];
  notes: CustomerNote[];
  statusId: number;
  status: string;
  statusCode: string;
  latitude: number | null;
  longitude: number | null;
  geocodeStatus: GeocodeStatus;
  geocodeMatchLevel: GeoMatchLevel | null;
  geocodeMessage: string | null;
  geocodeSource: string | null;
  geocodedAt: string | null;
  geocodeNextAttemptAt: string | null;
  legacyPlan: string | null;
  groupId: number | null;
  groupNumber: number | null;
  assignedAt: string | null;
  assignedBy: string | null;
  history: AssignmentHistoryEntry[];
  audit: AuditEntry[];
  createdAt: string;
  createdBy: string | null;
  updatedAt: string;
  updatedBy: string | null;
}

/** Fields of the Add/Edit customer screens. CustomerID is always decided by the server. */
export interface SaveCustomerRequest {
  customerName: string;
  phoneNumber: string;
  /** Optional second number. */
  alternatePhoneNumber: string | null;
  address: string;
  pincode: string | null;
  /** "HH:mm:ss" */
  preferredDeliveryTime: string | null;
  statusId: number | null;
}

/** A package as entered. A new package's end date and the paused count are always decided by the server. */
export interface SavePackageRequest {
  preferenceId: number | null;
  mealTypeId: number | null;
  quantity: number | null;
  frequencyId: number | null;
  startDate: string | null;
}

export interface CreateCustomerRequest extends SaveCustomerRequest {
  packages: SavePackageRequest[];
  note: string | null;
}

export interface UpdateCustomerRequest extends SaveCustomerRequest {
  expectedUpdatedAt: string;
}

export interface UpdatePackageRequest extends SavePackageRequest {
  expectedUpdatedAt: string;
  confirmScheduleChange: boolean;
  /** The end date as typed; the same as the calculated one keeps the calculation. */
  endDate: string | null;
}

export interface CreateCustomerResult {
  id: number;
  customerId: string;
  packages: { id: number; startDate: string; endDate: string }[];
  warnings: string[];
}

export interface UpdateCustomerResult {
  changed: boolean;
  changedFields: string[];
  warnings: string[];
}

export interface PackageSaved {
  id: number;
  startDate: string;
  endDate: string;
  pausedCount: number;
}

export interface SchedulePreview {
  startDate: string;
  endDate: string;
  deliveryDays: number;
  calendarDays: number;
  frequency: string;
}

export interface PausedPackage {
  packageId: number;
  package: string;
  previousEndDate: string;
  newEndDate: string;
  days: { date: string; extendedEndDate: boolean }[];
  pausedCount: number;
  extendingPausesLeft: number;
}

export interface PauseResult {
  dates: string[];
  packages: PausedPackage[];
  /** Packages running on those days that cannot pause; they are still delivered. */
  notPaused: string[];
}

export interface BulkResult {
  requested: number;
  changed: number;
  alreadyInGroup: number;
}

export type ExportScope = 'All' | 'Filtered' | 'Selected' | 'Page';
export type ExportFormat = 'Excel' | 'Pdf';

export interface ExportRequest {
  scope: ExportScope;
  format: ExportFormat;
  filter: CustomerFilter;
  ids?: number[];
  /** Column keys in display order; empty = every column. */
  columns: string[];
  page?: number;
  pageSize?: number;
}

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

// ---------- Delivery area & classification ----------

export interface ServiceArea {
  city: string | null;
  state: string | null;
  country: string;
  isConfigured: boolean;
  areaFound: boolean;
  centerLatitude: number | null;
  centerLongitude: number | null;
  areaMessage: string | null;
  geocodingProvider: string;
  geocodingConfigured: boolean;
  updatedAt: string | null;
  updatedBy: string | null;
}

export interface MapPoint {
  id: number;
  customerId: string;
  name: string;
  address: string | null;
  pincode: string | null;
  /** Locality from the address, or "PIN 641002". */
  area: string | null;
  lat: number;
  lng: number;
  approximate: boolean;
  status: string;
  statusCode: string;
  mealType: string | null;
  groupId: number | null;
  groupNumber: number | null;
}

export interface UnlocatedCustomer {
  id: number;
  customerId: string;
  name: string;
  address: string | null;
  pincode: string | null;
  geocodeStatus: GeocodeStatus;
  message: string | null;
  groupId: number | null;
  groupNumber: number | null;
}

export interface Boundary {
  kind: 'none' | 'point' | 'line' | 'polygon';
  /** [latitude, longitude] pairs */
  points: [number, number][];
}

export interface MapGroup {
  id: number | null;
  number: number;
  isNew: boolean;
  isActive: boolean;
  customerCount: number;
  locatedCount: number;
  boundary: Boundary;
  centerLat: number | null;
  centerLng: number | null;
  /** The group's areas, most customers first. */
  areas: { name: string; count: number }[];
}

export interface MapData {
  points: MapPoint[];
  unlocated: UnlocatedCustomer[];
  unlocatedTotal: number;
  groups: MapGroup[];
  inScope: number;
  located: number;
  approximate: number;
  unassigned: number;
  statuses: string[];
  area: ServiceArea;
}

export interface ChangeSummary {
  assigned: number;
  moved: number;
  removed: number;
  unchanged: number;
  newGroupNumbers: number[];
  messages: string[];
}

export interface Suggestion {
  requestedGroups: number;
  groupsUsed: number;
  assignments: { id: number; groupNumber: number | null; basis: 'location' | 'pincode' | 'unchanged' }[];
  groups: MapGroup[];
  changes: ChangeSummary;
  notes: string[];
}

export interface DraftAssignment {
  customerId: number;
  groupNumber: number | null;
}

export interface DraftEvaluation {
  groups: MapGroup[];
  changes: ChangeSummary;
}

export interface ApplyResult {
  assigned: number;
  moved: number;
  removed: number;
  unchanged: number;
  createdGroupNumbers: number[];
}

export interface AppSettings {
  export: { maxRows: number; pdfMaxRows: number; timeZone: string; columns: string[] };
  locations: { knownCities: string[]; cityAliases: Record<string, string>; regionWords: string[] };
  security: { sessionHours: number; passwordMinLength: number; maxFailedAttempts: number; lockoutMinutes: number };
  whatsApp: WhatsAppStatus;
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

export const api = {
  dashboard: (signal?: AbortSignal) => get<DashboardSummary>('/api/dashboard', signal),
  system: (signal?: AbortSignal) => get<SystemInfo>('/api/system', signal),

  deliveryGroups: (signal?: AbortSignal) => get<DeliveryGroupOverview>('/api/delivery-groups', signal),
  createGroups: (count: number, notes: string | null) => send<{ id: number; groupNumber: number }[]>('POST', '/api/delivery-groups', { count, notes }),
  updateGroupNotes: (id: number, notes: string | null) => send('PUT', `/api/delivery-groups/${id}`, { notes }),
  setGroupActive: (id: number, active: boolean) => send('POST', `/api/delivery-groups/${id}/${active ? 'activate' : 'deactivate'}`),
  deleteGroup: (id: number) => send('DELETE', `/api/delivery-groups/${id}`),
  groupCustomers: (group: string, search: string, page: number, pageSize: number, signal?: AbortSignal) =>
    get<Paged<GroupCustomer>>(`/api/delivery-groups/${group}/customers${query({ search, page, pageSize })}`, signal),
  previewMove: (customerIds: number[], targetGroupId: number) =>
    send<MovePreview>('POST', '/api/delivery-groups/move/preview', { customerIds, targetGroupId }),
  move: (customerIds: number[], targetGroupId: number, reason: string | null) =>
    send<{ requested: number; changed: number; alreadyInGroup: number }>('POST', '/api/delivery-groups/move', { customerIds, targetGroupId, reason }),

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
  createCustomer: (request: CreateCustomerRequest) => send<CreateCustomerResult>('POST', '/api/customers', request),
  updateCustomer: (id: number, request: UpdateCustomerRequest) => send<UpdateCustomerResult>('PUT', `/api/customers/${id}`, request),
  schedule: (frequencyId: number, startDate: string, signal?: AbortSignal) =>
    get<SchedulePreview>(`/api/customers/schedule${query({ frequencyId, startDate })}`, signal),
  /** Pauses the days for every pausable package running on them. */
  pauseCustomer: (id: number, dates: string[], reason: string | null) => send<PauseResult>('POST', `/api/customers/${id}/pauses`, { dates, reason }),
  /** What pauseCustomer would do, without saving. */
  previewPause: (id: number, dates: string[]) => send<PauseResult>('POST', `/api/customers/${id}/pauses/preview`, { dates, reason: null }),
  addPackage: (customerId: number, request: SavePackageRequest) => send<PackageSaved>('POST', `/api/customers/${customerId}/packages`, request),
  updatePackage: (packageId: number, request: UpdatePackageRequest) => send<PackageSaved>('PUT', `/api/customers/packages/${packageId}`, request),
  deletePackage: (packageId: number) => send('DELETE', `/api/customers/packages/${packageId}`),
  addNote: (customerId: number, text: string) => send<{ id: number }>('POST', `/api/customers/${customerId}/notes`, { text }),
  updateNote: (noteId: number, text: string) => send('PUT', `/api/customers/notes/${noteId}`, { text }),
  deleteNote: (noteId: number) => send('DELETE', `/api/customers/notes/${noteId}`),
  previewCustomerGroup: (customerIds: number[], groupId: number | null) =>
    send<MovePreview>('POST', '/api/customers/group/preview', { customerIds, groupId }),
  changeCustomerGroup: (customerIds: number[], groupId: number | null, reason: string | null) =>
    send<BulkResult>('POST', '/api/customers/group', { customerIds, groupId, reason }),
  locateCustomers: (ids: number[] | null) => send<{ queued: number }>('POST', '/api/customers/locate', { ids }),
  exportCustomers: (request: ExportRequest) => download('/api/customers/export', request),

  dictionaries: (signal?: AbortSignal) => get<DictionaryOptions>('/api/dictionaries', signal),
  dictionaryEntries: (type: DictionaryType, signal?: AbortSignal) => get<DictionaryEntry[]>(`/api/dictionaries/${type}`, signal),
  createDictionaryEntry: (type: DictionaryType, entry: SaveDictionaryEntry) => send<DictionaryEntry>('POST', `/api/dictionaries/${type}`, entry),
  updateDictionaryEntry: (id: number, entry: SaveDictionaryEntry) => send<DictionaryEntry>('PUT', `/api/dictionaries/entries/${id}`, entry),
  deleteDictionaryEntry: (id: number) => send('DELETE', `/api/dictionaries/entries/${id}`),
  reorderDictionary: (type: DictionaryType, ids: number[]) => send('PUT', `/api/dictionaries/${type}/order`, { ids }),

  settings: (signal?: AbortSignal) => get<AppSettings>('/api/settings', signal),
  deliveryArea: (signal?: AbortSignal) => get<ServiceArea>('/api/settings/delivery-area', signal),
  saveDeliveryArea: (city: string, state: string | null) => send<ServiceArea>('PUT', '/api/settings/delivery-area', { city, state }),

  classificationMap: (statuses: string[], signal?: AbortSignal) =>
    get<MapData>(`/api/classification/map${query({ statuses: statuses.join(',') })}`, signal),
  /** auto: find the natural number of location clusters (groupCount is then ignored). */
  suggestGroups: (groupCount: number, auto: boolean, statuses: string[], balance: boolean, usePincodeForUnlocated: boolean) =>
    send<Suggestion>('POST', '/api/classification/suggest', { groupCount, auto, statuses, balance, usePincodeForUnlocated }),
  evaluateDraft: (assignments: DraftAssignment[]) => send<DraftEvaluation>('POST', '/api/classification/evaluate', { assignments }),
  applyDraft: (assignments: DraftAssignment[], reason: string | null) =>
    send<ApplyResult>('POST', '/api/classification/apply', { assignments, reason }),
  users: (signal?: AbortSignal) => get<AppUser[]>('/api/users', signal),
  createUser: (userName: string, displayName: string, role: string, password: string) =>
    send<AppUser>('POST', '/api/users', { userName, displayName, role, password }),
  updateUser: (id: number, displayName: string, role: string, isActive: boolean) => send('PUT', `/api/users/${id}`, { displayName, role, isActive }),
  resetPassword: (id: number, newPassword: string) => send('POST', `/api/users/${id}/reset-password`, { newPassword }),
  audit: (params: { entityType?: string; action?: string; search?: string }, page: number, pageSize: number, signal?: AbortSignal) =>
    get<Paged<AuditEntry>>(`/api/audit${query({ ...params, page, pageSize })}`, signal),
  auditActions: (signal?: AbortSignal) => get<string[]>('/api/audit/actions', signal),
};
