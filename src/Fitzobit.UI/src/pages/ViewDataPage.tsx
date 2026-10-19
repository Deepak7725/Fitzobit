import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { ArrowDown, ArrowUp, Columns3, Download, Eye, FileSpreadsheet, FileText, FilterX, LocateFixed, Map as MapIcon, Search, Shuffle, Table2, UserPlus, X } from 'lucide-react';
import { api, type CustomerFilter, type CustomerListItem, type ExportFormat, type ExportScope, type NoteSummary, type PackageSummary } from '../api';
import { useApi, useDebounced } from '../hooks';
import { formatDate, formatDateTime, formatDay, formatTime } from '../format';
import { Badge, Card, EmptyState, ErrorPanel, Loading, PageHeader } from '../components/ui';
import { Modal, useToast } from '../components/feedback';
import CustomerDrawer, { statusTone } from '../components/CustomerDrawer';
import ChangeGroupDialog from '../components/ChangeGroupDialog';
import { GroupPill, LocationBadge, formatCoordinate } from '../components/groups';

interface Column {
  key: string;
  label: string;
  sort?: string;
  className?: string;
  render: (c: CustomerListItem) => ReactNode;
}

const notSet = <span className="text-warning">—</span>;

/** One line per package: "Veg Medium ×1 · Monthly" with its dates underneath. */
function PackagesCell({ packages }: { packages: PackageSummary[] }) {
  if (packages.length === 0) return <span className="text-warning">No package</span>;
  return (
    <ul className="packages-cell">
      {packages.map((p) => (
        <li key={p.id}>
          <span className="nowrap">
            {p.preference} {p.mealType} ×{p.quantity} · {p.frequency}
          </span>
          <span className="item-sub nowrap">
            {formatDay(p.startDate)} – {formatDay(p.endDate)}
            {p.pausedDays > 0 ? ` · ${p.pausedDays} paused` : ''}
          </span>
        </li>
      ))}
    </ul>
  );
}

/** Every note, newest first, each with its date and writer. */
function NotesCell({ notes }: { notes: NoteSummary[] }) {
  if (notes.length === 0) return <span className="muted">—</span>;
  return (
    <ul className="notes-cell">
      {notes.map((n) => (
        <li key={n.id}>
          <span className="notes-cell-text">{n.text}</span>
          <span className="item-sub nowrap">
            {formatDate(n.createdAt)}
            {n.createdBy ? ` · ${n.createdBy}` : ''}
          </span>
        </li>
      ))}
    </ul>
  );
}

const GEO_LABELS: Record<string, string> = { located: 'Located', approximate: 'Approximate (pincode)', missing: 'Location unavailable' };

const COLUMNS: Column[] = [
  { key: 'customerId', label: 'Customer ID', sort: 'customerId', render: (c) => <span className="strong">{c.customerId}</span> },
  { key: 'customerName', label: 'Customer Name', sort: 'customerName', render: (c) => c.customerName },
  { key: 'phone', label: 'Phone Number', className: 'nowrap', render: (c) => c.phoneNumber },
  { key: 'alternatePhone', label: 'Alternative Phone', className: 'nowrap', render: (c) => c.alternatePhoneNumber ?? <span className="muted">—</span> },
  { key: 'address', label: 'Address', sort: 'address', className: 'cell-wide', render: (c) => c.address ?? '—' },
  { key: 'pincode', label: 'Pincode', sort: 'pincode', render: (c) => c.pincode ?? notSet },
  {
    key: 'deliveryTime',
    label: 'Delivery Time',
    sort: 'deliveryTime',
    className: 'nowrap',
    render: (c) => (c.preferredDeliveryTime ? formatTime(c.preferredDeliveryTime) : notSet),
  },
  { key: 'packages', label: 'Packages', className: 'cell-packages', render: (c) => <PackagesCell packages={c.packages} /> },
  { key: 'notes', label: 'Notes', className: 'cell-notes', render: (c) => <NotesCell notes={c.notes} /> },
  { key: 'startDate', label: 'Start Date', sort: 'startDate', className: 'nowrap', render: (c) => (c.startDate ? formatDay(c.startDate) : notSet) },
  { key: 'endDate', label: 'End Date', sort: 'endDate', className: 'nowrap', render: (c) => (c.endDate ? formatDay(c.endDate) : '—') },
  { key: 'group', label: 'Group Number', sort: 'group', className: 'nowrap', render: (c) => <GroupPill groupNumber={c.groupNumber} /> },
  { key: 'status', label: 'Status', sort: 'status', render: (c) => <Badge tone={statusTone(c.statusCode)}>{c.status}</Badge> },
  { key: 'location', label: 'Location', className: 'nowrap', render: (c) => <LocationBadge status={c.geocodeStatus} level={c.geocodeMatchLevel} /> },
  { key: 'latitude', label: 'Latitude', sort: 'latitude', className: 'num mono', render: (c) => formatCoordinate(c.latitude) },
  { key: 'longitude', label: 'Longitude', sort: 'longitude', className: 'num mono', render: (c) => formatCoordinate(c.longitude) },
  { key: 'createdAt', label: 'Created At', sort: 'createdAt', className: 'nowrap', render: (c) => <span title={formatDateTime(c.createdAt)}>{formatDate(c.createdAt)}</span> },
  { key: 'updatedAt', label: 'Updated At', sort: 'updatedAt', className: 'nowrap', render: (c) => <span title={formatDateTime(c.updatedAt)}>{formatDate(c.updatedAt)}</span> },
];
const COLUMNS_KEY = 'fitzobit.viewData.hiddenColumns.v4';
const DEFAULT_HIDDEN = ['latitude', 'longitude', 'createdAt', 'updatedAt'];
const PAGE_SIZE_KEY = 'fitzobit.viewData.pageSize';
const EXPORT_FORMAT_KEY = 'fitzobit.viewData.exportFormat';

function readStored<T>(key: string, fallback: T): T {
  try {
    const raw = localStorage.getItem(key);
    return raw ? (JSON.parse(raw) as T) : fallback;
  } catch {
    return fallback;
  }
}

/** URL query ⇄ filters, so a filtered view can be bookmarked or linked (e.g. from an import). */
function filtersFromUrl(params: URLSearchParams): CustomerFilter {
  const num = (k: string) => (params.get(k) && !Number.isNaN(Number(params.get(k))) ? Number(params.get(k)) : undefined);
  return {
    search: params.get('q') ?? undefined,
    group: params.get('group') ?? undefined,
    preferenceId: num('preference'),
    mealTypeId: num('meal'),
    frequencyId: num('frequency'),
    statusId: num('status'),
    pincode: params.get('pincode') ?? undefined,
    address: params.get('address') ?? undefined,
    incomplete: params.get('incomplete') === '1' || undefined,
    geo: params.get('geo') ?? undefined,
    sort: params.get('sort') ?? undefined,
    desc: params.get('desc') === '1' || undefined,
  };
}

function filtersToUrl(f: CustomerFilter): Record<string, string> {
  const out: Record<string, string> = {};
  if (f.search) out.q = f.search;
  if (f.group) out.group = f.group;
  if (f.preferenceId) out.preference = String(f.preferenceId);
  if (f.mealTypeId) out.meal = String(f.mealTypeId);
  if (f.frequencyId) out.frequency = String(f.frequencyId);
  if (f.statusId) out.status = String(f.statusId);
  if (f.pincode) out.pincode = f.pincode;
  if (f.address) out.address = f.address;
  if (f.incomplete) out.incomplete = '1';
  if (f.geo) out.geo = f.geo;
  if (f.sort && f.sort !== 'customerId') out.sort = f.sort;
  if (f.desc) out.desc = '1';
  return out;
}

export default function ViewDataPage() {
  const toast = useToast();
  const [params, setParams] = useSearchParams();
  const initial = useMemo(() => filtersFromUrl(params), []); // eslint-disable-line react-hooks/exhaustive-deps

  const [search, setSearch] = useState(initial.search ?? '');
  const [group, setGroup] = useState(initial.group ?? '');
  const [preferenceId, setPreferenceId] = useState<number | undefined>(initial.preferenceId);
  const [mealTypeId, setMealTypeId] = useState<number | undefined>(initial.mealTypeId);
  const [frequencyId, setFrequencyId] = useState<number | undefined>(initial.frequencyId);
  const [statusId, setStatusId] = useState<number | undefined>(initial.statusId);
  const [pincode, setPincode] = useState(initial.pincode ?? '');
  const [address, setAddress] = useState(initial.address ?? '');
  const [incomplete, setIncomplete] = useState(!!initial.incomplete);
  const [geo, setGeo] = useState(initial.geo ?? '');
  const [locating, setLocating] = useState(false);
  const [sort, setSort] = useState(initial.sort ?? 'customerId');
  const [desc, setDesc] = useState(!!initial.desc);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState<number>(() => readStored(PAGE_SIZE_KEY, 25));
  const [hidden, setHidden] = useState<string[]>(() => readStored<string[]>(COLUMNS_KEY, DEFAULT_HIDDEN));
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [openId, setOpenId] = useState<number | null>(null);
  const [changingGroup, setChangingGroup] = useState(false);
  const [exportScope, setExportScope] = useState<ExportScope | null>(null);

  const debouncedSearch = useDebounced(search.trim());
  const debouncedPincode = useDebounced(pincode.trim());
  const debouncedAddress = useDebounced(address.trim());

  const filter: CustomerFilter = useMemo(
    () => ({
      search: debouncedSearch || undefined,
      group: group || undefined,
      preferenceId,
      mealTypeId,
      frequencyId,
      statusId,
      pincode: debouncedPincode || undefined,
      address: debouncedAddress || undefined,
      incomplete: incomplete || undefined,
      geo: geo || undefined,
      sort,
      desc: desc || undefined,
    }),
    [debouncedSearch, group, preferenceId, mealTypeId, frequencyId, statusId, debouncedPincode, debouncedAddress, incomplete, geo, sort, desc],
  );
  const filterKey = JSON.stringify(filter);

  // New filters start at page 1 and keep the URL in step.
  const firstRender = useRef(true);
  useEffect(() => {
    if (firstRender.current) {
      firstRender.current = false;
      return;
    }
    setPage(1);
    setParams(filtersToUrl(filter), { replace: true });
  }, [filterKey]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    try {
      localStorage.setItem(COLUMNS_KEY, JSON.stringify(hidden));
      localStorage.setItem(PAGE_SIZE_KEY, JSON.stringify(pageSize));
    } catch {
      /* storage unavailable: preferences are just not remembered */
    }
  }, [hidden, pageSize]);

  const options = useApi(api.customerFilterOptions);
  const list = useApi((signal) => api.customers(filter, page, pageSize, signal), [filterKey, page, pageSize]);

  const rows = list.data?.items ?? [];
  const total = list.data?.total ?? 0;
  const visible = COLUMNS.filter((c) => !hidden.includes(c.key));
  const groups = options.data?.groups ?? [];
  const nameOf = (list: { id: number; name: string }[] | undefined, id: number) => list?.find((o) => o.id === id)?.name ?? `#${id}`;

  const chips: { label: string; clear: () => void }[] = [];
  if (debouncedSearch) chips.push({ label: `Search: “${debouncedSearch}”`, clear: () => setSearch('') });
  if (group) chips.push({ label: `Group: ${group === 'none' ? 'No group' : (groups.find((g) => String(g.id) === group)?.name ?? group)}`, clear: () => setGroup('') });
  if (preferenceId) chips.push({ label: `Preference: ${nameOf(options.data?.preferences, preferenceId)}`, clear: () => setPreferenceId(undefined) });
  if (mealTypeId) chips.push({ label: `Meal type: ${nameOf(options.data?.mealTypes, mealTypeId)}`, clear: () => setMealTypeId(undefined) });
  if (frequencyId) chips.push({ label: `Frequency: ${nameOf(options.data?.frequencies, frequencyId)}`, clear: () => setFrequencyId(undefined) });
  if (statusId) chips.push({ label: `Status: ${nameOf(options.data?.statuses, statusId)}`, clear: () => setStatusId(undefined) });
  if (debouncedPincode) chips.push({ label: `Pincode: ${debouncedPincode}`, clear: () => setPincode('') });
  if (debouncedAddress) chips.push({ label: `Address: “${debouncedAddress}”`, clear: () => setAddress('') });
  if (incomplete) chips.push({ label: 'Incomplete details', clear: () => setIncomplete(false) });
  if (geo) chips.push({ label: `Location: ${GEO_LABELS[geo] ?? geo}`, clear: () => setGeo('') });
  const filtered = chips.length > 0;

  const clearFilters = () => {
    setSearch('');
    setGroup('');
    setPreferenceId(undefined);
    setMealTypeId(undefined);
    setFrequencyId(undefined);
    setStatusId(undefined);
    setPincode('');
    setAddress('');
    setIncomplete(false);
    setGeo('');
  };

  const retryLocations = async () => {
    setLocating(true);
    try {
      const result = await api.locateCustomers([...selected]);
      toast(`${result.queued.toLocaleString('en-IN')} customer(s) queued for a location lookup.`, 'info');
      list.reload();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'The lookup could not be queued.', 'error');
    } finally {
      setLocating(false);
    }
  };

  const toggleSort = (key: string) => {
    if (sort === key) setDesc((d) => !d);
    else {
      setSort(key);
      setDesc(false);
    }
  };

  const pageIds = rows.map((r) => r.id);
  const allOnPage = pageIds.length > 0 && pageIds.every((id) => selected.has(id));
  const toggleAll = () =>
    setSelected((s) => {
      const next = new Set(s);
      if (allOnPage) pageIds.forEach((id) => next.delete(id));
      else pageIds.forEach((id) => next.add(id));
      return next;
    });
  const toggleOne = (id: number) =>
    setSelected((s) => {
      const next = new Set(s);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });


  const refresh = () => {
    list.reload();
    options.reload();
  };

  const from = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(total, page * pageSize);
  const pages = Math.max(1, Math.ceil(total / pageSize));
  const noCustomersAtAll = list.data && total === 0 && !filtered;

  return (
    <>
      <PageHeader
        title="Customers"
        subtitle="Customer management and data classification: search, filter and manage customers, their location and delivery group."
        actions={
          <>
            <Link to="/map" className="btn btn-secondary">
              <MapIcon size={16} /> Delivery classification
            </Link>
            <Link to="/customers/new" className="btn btn-secondary">
              <UserPlus size={16} /> Add customer
            </Link>
            <button className="btn btn-primary" onClick={() => setExportScope(selected.size > 0 ? 'Selected' : 'Filtered')} disabled={!list.data}>
              <Download size={16} /> Export
            </button>
          </>
        }
      />

      {options.error && <ErrorPanel message={options.error} onRetry={options.reload} />}

      <Card className="data-card">
        <div className="filter-bar">
          <label className="search search-wide">
            <Search size={16} />
            <input
              placeholder="Search CustomerID, name, phone, address, pincode, meal type or group number"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              aria-label="Search customers"
            />
            {search && (
              <button className="icon-btn icon-btn-xs" onClick={() => setSearch('')} aria-label="Clear search">
                <X size={14} />
              </button>
            )}
          </label>
          <select className="select" value={group} onChange={(e) => setGroup(e.target.value)} aria-label="Delivery group">
            <option value="">All groups</option>
            <option value="none">No group</option>
            {groups.map((g) => (
              <option key={g.id} value={g.id}>
                {g.name}
                {g.isActive ? '' : ' (inactive)'}
              </option>
            ))}
          </select>
          <OptionSelect label="Preference" all="All preferences" value={preferenceId} options={options.data?.preferences} onChange={setPreferenceId} />
          <OptionSelect label="Meal type" all="All meal types" value={mealTypeId} options={options.data?.mealTypes} onChange={setMealTypeId} />
          <OptionSelect label="Frequency" all="All frequencies" value={frequencyId} options={options.data?.frequencies} onChange={setFrequencyId} />
          <OptionSelect label="Status" all="All statuses" value={statusId} options={options.data?.statuses} onChange={setStatusId} />
          <input
            className="input input-pincode"
            placeholder="Pincode"
            inputMode="numeric"
            maxLength={6}
            value={pincode}
            onChange={(e) => setPincode(e.target.value.replace(/\D/g, ''))}
            aria-label="Exact pincode"
          />
          <input className="input input-location" placeholder="Address contains…" value={address} onChange={(e) => setAddress(e.target.value)} aria-label="Address" />
          <select className="select" value={geo} onChange={(e) => setGeo(e.target.value)} aria-label="Location">
            <option value="">Any location</option>
            {Object.entries(GEO_LABELS).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
          <label className="toggle" title="Customers carried over from the old system that still miss a preference, meal type, frequency, start date or pincode">
            <input type="checkbox" checked={incomplete} onChange={(e) => setIncomplete(e.target.checked)} />
            Incomplete only
          </label>
        </div>

        <div className="filter-summary">
          <div className="chips">
            {chips.map((c) => (
              <span key={c.label} className="filter-chip">
                {c.label}
                <button onClick={c.clear} aria-label={`Remove filter ${c.label}`}>
                  <X size={12} />
                </button>
              </span>
            ))}
            {filtered && (
              <button className="link-button small clear-filters" onClick={clearFilters}>
                <FilterX size={13} /> Clear filters
              </button>
            )}
          </div>
          <ColumnMenu hidden={hidden} onChange={setHidden} />
        </div>

        {selected.size > 0 && (
          <div className="selection-bar">
            <strong>{selected.size.toLocaleString('en-IN')} selected</strong>
            <button className="btn btn-secondary btn-sm" onClick={() => setChangingGroup(true)}>
              <Shuffle size={14} /> Change group
            </button>
            <button className="btn btn-secondary btn-sm" onClick={retryLocations} disabled={locating}>
              <LocateFixed size={14} /> {locating ? 'Queuing…' : 'Retry location'}
            </button>
            <button className="btn btn-secondary btn-sm" onClick={() => setExportScope('Selected')}>
              <Download size={14} /> Export selected
            </button>
            <button className="link-button" onClick={() => setSelected(new Set())}>
              Clear selection
            </button>
          </div>
        )}

        {list.error && <ErrorPanel message={list.error} onRetry={list.reload} />}
        {list.loading && !list.data && <Loading label="Loading customers…" />}

        {noCustomersAtAll && (
          <EmptyState
            icon={<Table2 size={22} />}
            title="No customers yet"
            message="Add your first Fitzobit customer to begin."
            action={
              <Link to="/customers/new" className="btn btn-primary">
                <UserPlus size={16} /> Add customer
              </Link>
            }
          />
        )}
        {list.data && total === 0 && filtered && (
          <EmptyState
            icon={<FilterX size={22} />}
            title="No customers match your current filters."
            message="Try a different search, or clear the filters to see every customer."
            action={
              <button className="btn btn-secondary" onClick={clearFilters}>
                Clear filters
              </button>
            }
          />
        )}

        {list.data && total > 0 && (
          <div className={`table-scroll ${list.loading ? 'is-loading' : ''}`} aria-busy={list.loading}>
            <table className="table data-table">
              <thead>
                <tr>
                  <th className="col-check">
                    <input type="checkbox" checked={allOnPage} onChange={toggleAll} aria-label="Select all customers on this page" />
                  </th>
                  {visible.map((col) => (
                    <th key={col.key} aria-sort={sort === col.sort ? (desc ? 'descending' : 'ascending') : undefined}>
                      {col.sort ? (
                        <button className={`sort-btn ${sort === col.sort ? 'sorted' : ''}`} onClick={() => toggleSort(col.sort!)}>
                          {col.label}
                          {sort === col.sort && desc ? <ArrowDown size={13} /> : <ArrowUp size={13} className={sort === col.sort ? '' : 'sort-idle'} />}
                        </button>
                      ) : (
                        col.label
                      )}
                    </th>
                  ))}
                  <th className="actions-cell">Actions</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((c) => (
                  <tr key={c.id} className={`row-click ${selected.has(c.id) ? 'row-selected' : ''}`} onClick={() => setOpenId(c.id)}>
                    <td className="col-check" onClick={(e) => e.stopPropagation()}>
                      <input type="checkbox" checked={selected.has(c.id)} onChange={() => toggleOne(c.id)} aria-label={`Select ${c.customerId}`} />
                    </td>
                    {visible.map((col) => (
                      <td key={col.key} className={col.className}>
                        {col.render(c)}
                      </td>
                    ))}
                    <td className="actions-cell">
                      <button
                        className="icon-btn"
                        title="Open customer"
                        aria-label={`Open ${c.customerId}`}
                        onClick={(e) => {
                          e.stopPropagation();
                          setOpenId(c.id);
                        }}
                      >
                        <Eye size={16} />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {list.data && total > 0 && (
          <div className="pagination">
            <span className="muted">
              Showing {from.toLocaleString('en-IN')}–{to.toLocaleString('en-IN')} of {total.toLocaleString('en-IN')} customer{total === 1 ? '' : 's'}
              {list.loading && <span className="spinner spinner-inline" aria-label="Loading" />}
            </span>
            <div className="pagination-buttons">
              <label className="muted small page-size">
                Rows per page
                <select
                  className="select select-sm"
                  value={pageSize}
                  onChange={(e) => {
                    setPageSize(Number(e.target.value));
                    setPage(1);
                  }}
                >
                  {(options.data?.pageSizes ?? [10, 25, 50, 100]).map((s) => (
                    <option key={s}>{s}</option>
                  ))}
                </select>
              </label>
              <button className="btn btn-secondary btn-sm" disabled={page <= 1} onClick={() => setPage(1)}>
                First
              </button>
              <button className="btn btn-secondary btn-sm" disabled={page <= 1} onClick={() => setPage(page - 1)}>
                Previous
              </button>
              <span className="page-indicator">
                Page {page.toLocaleString('en-IN')} of {pages.toLocaleString('en-IN')}
              </span>
              <button className="btn btn-secondary btn-sm" disabled={page >= pages} onClick={() => setPage(page + 1)}>
                Next
              </button>
            </div>
          </div>
        )}
      </Card>

      {exportScope && (
        <ExportDialog
          initialScope={exportScope}
          filter={filter}
          filtered={filtered}
          total={total}
          page={page}
          pageSize={pageSize}
          pageRows={rows.length}
          selected={[...selected]}
          columns={visible}
          onClose={() => setExportScope(null)}
        />
      )}
      {openId !== null && <CustomerDrawer id={openId} groups={groups} onClose={() => setOpenId(null)} onChanged={refresh} />}
      {changingGroup && (
        <ChangeGroupDialog
          customerIds={[...selected]}
          groups={groups}
          onClose={() => setChangingGroup(false)}
          onDone={() => {
            setChangingGroup(false);
            setSelected(new Set());
            refresh();
          }}
        />
      )}
    </>
  );
}

/** A filter drop-down whose values come from the database (inactive values stay selectable for filtering). */
function OptionSelect({ label, all, value, options, onChange }: {
  label: string;
  all: string;
  value: number | undefined;
  options: { id: number; name: string; isActive: boolean }[] | undefined;
  onChange: (value: number | undefined) => void;
}) {
  return (
    <select className="select" value={value ?? ''} onChange={(e) => onChange(e.target.value ? Number(e.target.value) : undefined)} aria-label={label}>
      <option value="">{all}</option>
      {options?.map((o) => (
        <option key={o.id} value={o.id}>
          {o.name}
          {o.isActive ? '' : ' (inactive)'}
        </option>
      ))}
    </select>
  );
}

function useDismiss(open: boolean, close: () => void) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => ref.current && !ref.current.contains(e.target as Node) && close();
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && close();
    document.addEventListener('mousedown', onDown);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDown);
      document.removeEventListener('keydown', onKey);
    };
  }, [open, close]);
  return ref;
}

function ColumnMenu({ hidden, onChange }: { hidden: string[]; onChange: (hidden: string[]) => void }) {
  const [open, setOpen] = useState(false);
  const ref = useDismiss(open, () => setOpen(false));
  return (
    <div className="dropdown" ref={ref}>
      <button className="btn btn-secondary btn-sm" onClick={() => setOpen((o) => !o)} aria-expanded={open}>
        <Columns3 size={14} /> Columns{hidden.length > 0 ? ` (${COLUMNS.length - hidden.length}/${COLUMNS.length})` : ''}
      </button>
      {open && (
        <div className="menu menu-right">
          {COLUMNS.map((c) => (
            <label key={c.key} className="menu-check">
              <input
                type="checkbox"
                disabled={!hidden.includes(c.key) && hidden.length >= COLUMNS.length - 1}
                title={!hidden.includes(c.key) && hidden.length >= COLUMNS.length - 1 ? 'At least one column stays visible' : undefined}
                checked={!hidden.includes(c.key)}
                onChange={() => onChange(hidden.includes(c.key) ? hidden.filter((k) => k !== c.key) : [...hidden, c.key])}
              />
              {c.label}
            </label>
          ))}
          <div className="menu-divider" />
          <button onClick={() => onChange(DEFAULT_HIDDEN)} disabled={sameSet(hidden, DEFAULT_HIDDEN)} title="The standard set of columns">
            Reset columns
          </button>
          <button onClick={() => onChange([])} disabled={hidden.length === 0}>
            Show all columns
          </button>
        </div>
      )}
    </div>
  );
}

function sameSet(a: string[], b: string[]) {
  return a.length === b.length && a.every((x) => b.includes(x));
}

/**
 * Export to Excel or PDF. Rows are chosen on the server with the same filters, sort and paging as the table; the file has
 * exactly the visible columns (in table order) unless "All columns" is chosen.
 */
function ExportDialog({ initialScope, filter, filtered, total, page, pageSize, pageRows, selected, columns, onClose }: {
  initialScope: ExportScope;
  filter: CustomerFilter;
  filtered: boolean;
  total: number;
  page: number;
  pageSize: number;
  pageRows: number;
  selected: number[];
  columns: Column[];
  onClose: () => void;
}) {
  const toast = useToast();
  const [format, setFormat] = useState<ExportFormat>(() => readStored<ExportFormat>(EXPORT_FORMAT_KEY, 'Excel'));
  const [scope, setScope] = useState<ExportScope>(initialScope);
  const [allColumns, setAllColumns] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const scopes: { value: ExportScope; label: string; hint: string; disabled?: boolean }[] = [
    {
      value: 'Filtered',
      label: filtered ? `Filtered customers (${total.toLocaleString('en-IN')})` : `All customers in the table (${total.toLocaleString('en-IN')})`,
      hint: filtered ? 'Every page of the current filters and search, in the current sort order' : 'No filters are set; every page, in the current sort order',
      disabled: total === 0,
    },
    { value: 'Page', label: `This page (${pageRows.toLocaleString('en-IN')})`, hint: `Page ${page}, ${pageSize} rows per page`, disabled: pageRows === 0 },
    {
      value: 'Selected',
      label: `Selected rows (${selected.length.toLocaleString('en-IN')})`,
      hint: selected.length > 0 ? 'Only the rows you ticked (on any page)' : 'Tick rows in the table first',
      disabled: selected.length === 0,
    },
    ...(filtered ? [{ value: 'All' as ExportScope, label: 'All customers, ignoring filters', hint: 'Every customer in the database' }] : []),
  ];
  const current = scopes.find((s) => s.value === scope);

  const run = async () => {
    setBusy(true);
    setError(null);
    try {
      localStorage.setItem(EXPORT_FORMAT_KEY, JSON.stringify(format));
    } catch {
      /* the format is just not remembered */
    }
    try {
      const result = await api.exportCustomers({
        scope,
        format,
        filter,
        ids: scope === 'Selected' ? selected : undefined,
        columns: allColumns ? [] : columns.map((c) => c.key),
        page: scope === 'Page' ? page : undefined,
        pageSize: scope === 'Page' ? pageSize : undefined,
      });
      toast(`Exported ${result.rows?.toLocaleString('en-IN') ?? ''} customer(s) to ${result.fileName}.`);
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The export failed.');
      setBusy(false);
    }
  };

  const formats: { value: ExportFormat; icon: ReactNode; label: string; hint: string }[] = [
    { value: 'Excel', icon: <FileSpreadsheet size={20} />, label: 'Excel (.xlsx)', hint: 'To work with the data: filter, sort, calculate' },
    { value: 'Pdf', icon: <FileText size={20} />, label: 'PDF', hint: 'To print or share: A4 pages with page numbers' },
  ];

  return (
    <Modal
      title="Export customers"
      onClose={() => !busy && onClose()}
      footer={
        <>
          <button className="btn btn-secondary" onClick={onClose} disabled={busy}>
            Cancel
          </button>
          <button className="btn btn-primary" onClick={run} disabled={busy || !current || current.disabled}>
            {busy ? <span className="spinner spinner-light" aria-hidden /> : <Download size={16} />}
            {busy ? 'Preparing file…' : `Download ${format === 'Pdf' ? 'PDF' : 'Excel'}`}
          </button>
        </>
      }
    >
      <fieldset className="export-section">
        <legend>Format</legend>
        <div className="choice-cards">
          {formats.map((f) => (
            <label key={f.value} className={`choice-card ${format === f.value ? 'active' : ''}`}>
              <input type="radio" name="export-format" checked={format === f.value} onChange={() => setFormat(f.value)} />
              {f.icon}
              <span>
                <strong>{f.label}</strong>
                <small className="muted">{f.hint}</small>
              </span>
            </label>
          ))}
        </div>
      </fieldset>

      <fieldset className="export-section">
        <legend>Rows</legend>
        {scopes.map((s) => (
          <label key={s.value} className={`radio-row ${s.disabled ? 'is-disabled' : ''}`}>
            <input type="radio" name="export-scope" checked={scope === s.value} disabled={s.disabled} onChange={() => setScope(s.value)} />
            <span>
              <strong>{s.label}</strong>
              <small className="muted">{s.hint}</small>
            </span>
          </label>
        ))}
      </fieldset>

      <fieldset className="export-section">
        <legend>Columns</legend>
        <label className="radio-row">
          <input type="radio" name="export-columns" checked={!allColumns} onChange={() => setAllColumns(false)} />
          <span>
            <strong>Visible columns ({columns.length})</strong>
            <small className="muted">{columns.map((c) => c.label).join(' · ')}</small>
          </span>
        </label>
        <label className="radio-row">
          <input type="radio" name="export-columns" checked={allColumns} onChange={() => setAllColumns(true)} />
          <span>
            <strong>All columns ({COLUMNS.length})</strong>
            <small className="muted">Full export, including the columns hidden in the table</small>
          </span>
        </label>
      </fieldset>

      {error && (
        <div className="auth-error" role="alert">
          {error}
        </div>
      )}
    </Modal>
  );
}
