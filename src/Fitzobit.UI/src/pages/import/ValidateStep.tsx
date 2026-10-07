import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ArrowRight, Info, RotateCcw, Search } from 'lucide-react';
import { api, type ImportDetail, type RowFilter } from '../../api';
import { useApi, useDebounced } from '../../hooks';
import { Card, EmptyState, ErrorPanel, Loading } from '../../components/ui';
import { ConfirmDialog, Pagination, useToast } from '../../components/feedback';
import { IssueList, MiniStat, RowStatusBadge, StepFooter, stepPath } from './shared';

const tabs = [
  { key: '', label: 'All' },
  { key: 'Valid', label: 'Valid' },
  { key: 'Warning', label: 'Warnings' },
  { key: 'Error', label: 'Errors' },
  { key: 'Excluded', label: 'Not imported' },
];

export default function ValidateStep({ detail, onChanged }: { detail: ImportDetail; onChanged: () => void }) {
  const navigate = useNavigate();
  const toast = useToast();
  const s = detail.summary!;
  const [status, setStatus] = useState(s.errorRows > 0 ? 'Error' : '');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [confirmCancel, setConfirmCancel] = useState(false);
  const [busy, setBusy] = useState(false);
  const debounced = useDebounced(search);
  const filter: RowFilter = { status: status || undefined, search: debounced || undefined };
  const rows = useApi((signal) => api.importRows(detail.id, filter, page, 50, signal), [detail.id, status, debounced, page]);
  const editable = detail.status === 'Validated';

  const toggle = (id: number) =>
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  const setExcluded = async (excluded: boolean) => {
    const { updated } = await api.setExcluded(detail.id, [...selected], excluded);
    toast(`${updated} row(s) ${excluded ? 'excluded from' : 'included in'} the import`);
    setSelected(new Set());
    rows.reload();
    onChanged();
  };

  const cancel = async () => {
    setBusy(true);
    try {
      await api.cancelImport(detail.id);
      navigate('/upload');
    } finally {
      setBusy(false);
    }
  };

  const skipped = s.errorRows + s.excludedByManager;

  return (
    <>
      <div className="mini-stat-grid">
        <MiniStat label="Total rows" value={s.totalRows} />
        <MiniStat label="Valid rows" value={s.validRows} tone="success" />
        <MiniStat label="Errors" value={s.errorRows} tone={s.errorRows ? 'danger' : undefined} />
        <MiniStat label="Warnings" value={s.warningRows} tone={s.warningRows ? 'warning' : undefined} />
        <MiniStat label="New customers" value={s.newCustomers} tone="info" />
        <MiniStat label="Existing customers" value={s.existingCustomers} />
        <MiniStat label="Duplicates" value={s.duplicates} tone={s.duplicates ? 'warning' : undefined} />
        <MiniStat label="Locations" value={s.locations} />
        <MiniStat label="Pincodes" value={s.pincodes} />
        <MiniStat label="Plans" value={s.plans} />
      </div>

      {detail.fileMessages.length > 0 && (
        <div className="alert alert-neutral">
          <Info size={18} />
          <div>
            {detail.fileMessages.map((m) => (
              <p key={m}>{m}</p>
            ))}
          </div>
        </div>
      )}

      {s.errorRows > 0 && (
        <div className="alert alert-warning">
          <Info size={18} />
          <div>
            <strong>
              {s.errorRows.toLocaleString('en-IN')} row{s.errorRows === 1 ? ' has' : 's have'} errors and will not be imported.
            </strong>{' '}
            Fix them in Excel and re-upload, or continue with the {s.toImport.toLocaleString('en-IN')} valid record
            {s.toImport === 1 ? '' : 's'}. Unknown or ambiguous plans can also be resolved in the next step.
          </div>
        </div>
      )}

      <Card title="Row validation" subtitle="Every row with its status and the reasons behind it">
        <div className="toolbar">
          <div className="tabs tabs-inline" role="tablist">
            {tabs.map((t) => (
              <button
                key={t.key}
                role="tab"
                aria-selected={status === t.key}
                className={status === t.key ? 'active' : ''}
                onClick={() => {
                  setStatus(t.key);
                  setPage(1);
                  setSelected(new Set());
                }}
              >
                {t.label}
                <span className="tab-count">
                  {
                    {
                      '': s.totalRows,
                      Valid: s.validRows,
                      Warning: s.warningRows,
                      Error: s.errorRows,
                      Excluded: skipped,
                    }[t.key]
                  }
                </span>
              </button>
            ))}
          </div>
          <div className="search">
            <Search size={16} />
            <input
              type="search"
              placeholder="Search ID, name, phone, location…"
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                setPage(1);
              }}
              aria-label="Search rows"
            />
          </div>
        </div>

        {editable && selected.size > 0 && (
          <div className="selection-bar">
            <span>
              <strong>{selected.size}</strong> selected
            </span>
            <button className="btn btn-secondary btn-sm" onClick={() => setExcluded(true)}>
              Exclude from import
            </button>
            <button className="btn btn-secondary btn-sm" onClick={() => setExcluded(false)}>
              Include again
            </button>
            <button className="link-button" onClick={() => setSelected(new Set())}>
              Clear
            </button>
          </div>
        )}

        {rows.error && <ErrorPanel message={rows.error} onRetry={rows.reload} />}
        {rows.loading && !rows.data && <Loading />}
        {rows.data && rows.data.items.length === 0 && <EmptyState title="No rows match" message="Try another filter or search." />}
        {rows.data && rows.data.items.length > 0 && (
          <>
            <div className="table-wrap">
              <table className="table rows-table">
                <thead>
                  <tr>
                    {editable && <th aria-label="Select" />}
                    <th className="num">Row</th>
                    <th>CustomerID</th>
                    <th>CustomerName</th>
                    <th>Phone</th>
                    <th>Location</th>
                    <th>Pincode</th>
                    <th>Plan</th>
                    <th>Status</th>
                    <th>Issues</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.data.items.map((r) => (
                    <tr key={r.id} className={r.status === 'Error' ? 'row-error' : r.status === 'Warning' ? 'row-warn' : ''}>
                      {editable && (
                        <td>
                          <input
                            type="checkbox"
                            checked={selected.has(r.id)}
                            disabled={r.status === 'Error'}
                            onChange={() => toggle(r.id)}
                            aria-label={`Select row ${r.rowNumber}`}
                          />
                        </td>
                      )}
                      <td className="num muted">{r.rowNumber}</td>
                      <td className="strong">{r.customerId ?? <span className="muted">—</span>}</td>
                      <td>{r.customerName ?? <span className="muted">—</span>}</td>
                      <td>
                        <div>{r.phoneNumber ?? <span className="muted">—</span>}</div>
                        {r.normalizedPhoneNumber && r.normalizedPhoneNumber !== r.phoneNumber && <div className="item-sub">→ {r.normalizedPhoneNumber}</div>}
                      </td>
                      <td className="cell-wide">
                        <div>{r.location ?? <span className="muted">—</span>}</div>
                        {(r.locality || r.city) && <div className="item-sub">{[r.locality, r.city].filter(Boolean).join(' · ')}</div>}
                      </td>
                      <td>{r.pincode ?? <span className="muted">—</span>}</td>
                      <td className="cell-wide">
                        <div>{r.plan ?? <span className="muted">—</span>}</div>
                        {r.planName && r.planName !== r.plan && (
                          <div className="item-sub">
                            → {r.planName}
                            {r.tierName ? ` · ${r.tierName}` : ''}
                          </div>
                        )}
                      </td>
                      <td>
                        <RowStatusBadge row={r} />
                      </td>
                      <td className="cell-issues">
                        <IssueList issues={r.issues} />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Pagination page={page} pageSize={50} total={rows.data.total} onPage={setPage} />
          </>
        )}
      </Card>

      <StepFooter
        note={
          <>
            <strong>{s.toImport.toLocaleString('en-IN')}</strong> of {s.totalRows.toLocaleString('en-IN')} rows will be imported
            {s.warningRows > 0 && ` (warnings included)`}.
            {skipped > 0 && ` ${skipped.toLocaleString('en-IN')} row${skipped === 1 ? '' : 's'} will be skipped.`}
          </>
        }
      >
        <button className="btn btn-secondary" onClick={() => setConfirmCancel(true)} disabled={!editable}>
          <RotateCcw size={16} /> Fix &amp; re-upload
        </button>
        <button className="btn btn-primary" disabled={s.toImport === 0 && s.unresolvedPlanMappings === 0} onClick={() => navigate(stepPath(detail.id, 'classify'))}>
          Continue with {s.toImport.toLocaleString('en-IN')} valid record{s.toImport === 1 ? '' : 's'} <ArrowRight size={16} />
        </button>
      </StepFooter>

      {confirmCancel && (
        <ConfirmDialog
          title="Discard this upload?"
          messages={['This draft import will be cancelled. Nothing has been saved.', 'Fix the file in Excel, then upload it again.']}
          confirmLabel="Discard & re-upload"
          busy={busy}
          danger
          onCancel={() => setConfirmCancel(false)}
          onConfirm={cancel}
        />
      )}
    </>
  );
}
