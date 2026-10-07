import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { ArrowDown, ArrowUp, ChevronDown, Columns3, Download, Eye, FilterX, Search, Shuffle, Table2, Upload, X } from 'lucide-react';
import { api, type CustomerFilter, type CustomerListItem, type ExportScope } from '../api';
import { useApi, useDebounced } from '../hooks';
import { formatDate, formatDateTime } from '../format';
import { Badge, Card, EmptyState, ErrorPanel, Loading, PageHeader } from '../components/ui';
import { useToast } from '../components/feedback';
import CustomerDrawer from '../components/CustomerDrawer';
import ChangeGroupDialog from '../components/ChangeGroupDialog';

interface Column {
  key: string;
  label: string;
  sort?: string;
  className?: string;
  render: (c: CustomerListItem) => ReactNode;
}

const COLUMNS: Column[] = [
  { key: 'customerId', label: 'CustomerID', sort: 'customerId', render: (c) => <span className="strong">{c.customerId}</span> },
  { key: 'customerName', label: 'Name', sort: 'customerName', render: (c) => c.customerName },
  { key: 'phone', label: 'Phone', className: 'nowrap', render: (c) => c.phoneNumber },
  { key: 'location', label: 'Location', sort: 'location', className: 'cell-wide', render: (c) => c.location ?? '—' },
  { key: 'pincode', label: 'Pincode', sort: 'pincode', render: (c) => c.pincode ?? <span className="text-warning">—</span> },
  { key: 'plan', label: 'Plan', sort: 'plan', className: 'cell-wide', render: (c) => c.planName ?? <span className="text-warning">No plan</span> },
  {
    key: 'group',
    label: 'Delivery Group',
    sort: 'group',
    render: (c) => (c.groupName ? <span className="group-pill">{c.groupName}</span> : <Badge tone="warning">Unassigned</Badge>),
  },
  { key: 'status', label: 'Status', render: (c) => <Badge tone={c.status === 'Active' ? 'success' : 'neutral'}>{c.status}</Badge> },
  { key: 'createdAt', label: 'Created', sort: 'createdAt', className: 'nowrap', render: (c) => <span title={formatDateTime(c.createdAt)}>{formatDate(c.createdAt)}</span> },
  { key: 'updatedAt', label: 'Updated', sort: 'updatedAt', className: 'nowrap', render: (c) => <span title={formatDateTime(c.updatedAt)}>{formatDate(c.updatedAt)}</span> },
];
const ALWAYS_VISIBLE = new Set(['customerId']);
const COLUMNS_KEY = 'fitzobit.viewData.hiddenColumns';
const PAGE_SIZE_KEY = 'fitzobit.viewData.pageSize';

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
    planId: num('plan'),
    pincode: params.get('pincode') ?? undefined,
    location: params.get('location') ?? undefined,
    status: params.get('status') ?? undefined,
    importBatchId: num('import'),
    sort: params.get('sort') ?? undefined,
    desc: params.get('desc') === '1' || undefined,
  };
}

function filtersToUrl(f: CustomerFilter): Record<string, string> {
  const out: Record<string, string> = {};
  if (f.search) out.q = f.search;
  if (f.group) out.group = f.group;
  if (f.planId) out.plan = String(f.planId);
  if (f.pincode) out.pincode = f.pincode;
  if (f.location) out.location = f.location;
  if (f.status) out.status = f.status;
  if (f.importBatchId) out.import = String(f.importBatchId);
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
  const [planId, setPlanId] = useState<number | undefined>(initial.planId);
  const [pincode, setPincode] = useState(initial.pincode ?? '');
  const [location, setLocation] = useState(initial.location ?? '');
  const [status, setStatus] = useState(initial.status ?? '');
  const [importBatchId, setImportBatchId] = useState<number | undefined>(initial.importBatchId);
  const [sort, setSort] = useState(initial.sort ?? 'customerId');
  const [desc, setDesc] = useState(!!initial.desc);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState<number>(() => readStored(PAGE_SIZE_KEY, 25));
  const [hidden, setHidden] = useState<string[]>(() => readStored<string[]>(COLUMNS_KEY, []));
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [openId, setOpenId] = useState<number | null>(null);
  const [changingGroup, setChangingGroup] = useState(false);
  const [exporting, setExporting] = useState<ExportScope | null>(null);

  const debouncedSearch = useDebounced(search.trim());
  const debouncedPincode = useDebounced(pincode.trim());
  const debouncedLocation = useDebounced(location.trim());

  const filter: CustomerFilter = useMemo(
    () => ({
      search: debouncedSearch || undefined,
      group: group || undefined,
      planId,
      pincode: debouncedPincode || undefined,
      location: debouncedLocation || undefined,
      status: status || undefined,
      importBatchId,
      sort,
      desc: desc || undefined,
    }),
    [debouncedSearch, group, planId, debouncedPincode, debouncedLocation, status, importBatchId, sort, desc],
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
  const plans = options.data?.plans ?? [];

  const chips: { label: string; clear: () => void }[] = [];
  if (debouncedSearch) chips.push({ label: `Search: “${debouncedSearch}”`, clear: () => setSearch('') });
  if (group) chips.push({ label: `Group: ${group === 'none' ? 'No group' : (groups.find((g) => String(g.id) === group)?.name ?? group)}`, clear: () => setGroup('') });
  if (planId) chips.push({ label: `Plan: ${plans.find((p) => p.id === planId)?.name ?? planId}`, clear: () => setPlanId(undefined) });
  if (debouncedPincode) chips.push({ label: `Pincode: ${debouncedPincode}`, clear: () => setPincode('') });
  if (debouncedLocation) chips.push({ label: `Location: “${debouncedLocation}”`, clear: () => setLocation('') });
  if (status) chips.push({ label: `Status: ${status}`, clear: () => setStatus('') });
  if (importBatchId)
    chips.push({
      label: `Import: ${options.data?.imports.find((i) => i.id === importBatchId)?.fileName ?? `#${importBatchId}`}`,
      clear: () => setImportBatchId(undefined),
    });
  const filtered = chips.length > 0;

  const clearFilters = () => {
    setSearch('');
    setGroup('');
    setPlanId(undefined);
    setPincode('');
    setLocation('');
    setStatus('');
    setImportBatchId(undefined);
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

  const runExport = async (scope: ExportScope) => {
    setExporting(scope);
    try {
      const result = await api.exportCustomers(scope, filter, scope === 'Selected' ? [...selected] : null);
      toast(`Exported ${result.rows?.toLocaleString('en-IN') ?? ''} customer(s) to ${result.fileName}.`);
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Export failed.', 'error');
    } finally {
      setExporting(null);
    }
  };

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
        title="View & Manage Data"
        subtitle="Search, filter and manage customers and their delivery groups. Filtering, sorting and paging run on the server."
        actions={<ExportMenu busy={exporting} filtered={filtered} total={total} selected={selected.size} onExport={runExport} />}
      />

      {options.error && <ErrorPanel message={options.error} onRetry={options.reload} />}

      <Card className="data-card">
        <div className="filter-bar">
          <label className="search search-wide">
            <Search size={16} />
            <input
              placeholder="Search CustomerID, name, phone, location, pincode, plan or group"
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
          <select className="select" value={planId ?? ''} onChange={(e) => setPlanId(e.target.value ? Number(e.target.value) : undefined)} aria-label="Plan">
            <option value="">All plans</option>
            {plans.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}
                {p.isActive ? '' : ' (inactive)'}
              </option>
            ))}
          </select>
          <input
            className="input input-pincode"
            placeholder="Pincode"
            inputMode="numeric"
            maxLength={6}
            value={pincode}
            onChange={(e) => setPincode(e.target.value.replace(/\D/g, ''))}
            aria-label="Exact pincode"
          />
          <input className="input input-location" placeholder="Location contains…" value={location} onChange={(e) => setLocation(e.target.value)} aria-label="Location" />
          <select className="select" value={status} onChange={(e) => setStatus(e.target.value)} aria-label="Status">
            <option value="">All statuses</option>
            <option value="Active">Active</option>
            <option value="Inactive">Inactive</option>
            <option value="Unassigned">Unassigned</option>
          </select>
          <select
            className="select"
            value={importBatchId ?? ''}
            onChange={(e) => setImportBatchId(e.target.value ? Number(e.target.value) : undefined)}
            aria-label="Import batch"
          >
            <option value="">All imports</option>
            {options.data?.imports.map((i) => (
              <option key={i.id} value={i.id}>
                #{i.id} {i.fileName}
              </option>
            ))}
          </select>
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
            <button className="btn btn-secondary btn-sm" onClick={() => runExport('Selected')} disabled={exporting !== null}>
              <Download size={14} /> {exporting === 'Selected' ? 'Exporting…' : 'Export selected'}
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
            message="Upload your first Fitzobit customer Excel file to begin."
            action={
              <Link to="/upload" className="btn btn-primary">
                <Upload size={16} /> Upload customers
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
                disabled={ALWAYS_VISIBLE.has(c.key)}
                checked={!hidden.includes(c.key)}
                onChange={() => onChange(hidden.includes(c.key) ? hidden.filter((k) => k !== c.key) : [...hidden, c.key])}
              />
              {c.label}
            </label>
          ))}
          <div className="menu-divider" />
          <button onClick={() => onChange([])} disabled={hidden.length === 0}>
            Reset columns
          </button>
        </div>
      )}
    </div>
  );
}

function ExportMenu({ busy, filtered, total, selected, onExport }: {
  busy: ExportScope | null;
  filtered: boolean;
  total: number;
  selected: number;
  onExport: (scope: ExportScope) => void;
}) {
  const [open, setOpen] = useState(false);
  const ref = useDismiss(open, () => setOpen(false));
  const run = (scope: ExportScope) => {
    setOpen(false);
    onExport(scope);
  };
  return (
    <div className="dropdown" ref={ref}>
      <button className="btn btn-primary" onClick={() => setOpen((o) => !o)} disabled={busy !== null} aria-expanded={open}>
        {busy ? <span className="spinner spinner-light" aria-hidden /> : <Download size={16} />}
        {busy ? 'Exporting…' : 'Export to Excel'}
        <ChevronDown size={15} />
      </button>
      {open && (
        <div className="menu menu-right menu-wide">
          <button onClick={() => run('All')}>
            <strong>All customers</strong>
            <small className="muted">Every customer, ignoring filters</small>
          </button>
          <button onClick={() => run('Filtered')} disabled={!filtered}>
            <strong>Filtered customers ({total.toLocaleString('en-IN')})</strong>
            <small className="muted">{filtered ? 'Only the customers matching the current filters' : 'Set a filter first'}</small>
          </button>
          <button onClick={() => run('Selected')} disabled={selected === 0}>
            <strong>Selected customers ({selected.toLocaleString('en-IN')})</strong>
            <small className="muted">{selected > 0 ? 'Only the rows you ticked' : 'Tick rows in the table first'}</small>
          </button>
        </div>
      )}
    </div>
  );
}
