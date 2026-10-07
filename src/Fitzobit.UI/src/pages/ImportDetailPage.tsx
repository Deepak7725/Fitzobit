import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ArrowLeft, ArrowRight, FileSpreadsheet, Search, Table2 } from 'lucide-react';
import { api, type ImportDetail } from '../api';
import { useApi, useDebounced } from '../hooks';
import { formatBytes, formatDateTime } from '../format';
import { Badge, Card, EmptyState, ErrorPanel, Loading, PageHeader } from '../components/ui';
import { Pagination } from '../components/feedback';
import { IssueList, MiniStat, RowStatusBadge } from './import/shared';
import { statusLabel, statusTone } from './ImportHistoryPage';

type Tab = 'summary' | 'records' | 'errors' | 'warnings';

const seconds = (ms: number) => (ms > 0 ? `${(ms / 1000).toFixed(1)} s` : '—');

/** One import: what was uploaded, how it validated, what was saved — with the rows, errors and warnings. */
export default function ImportDetailPage() {
  const id = Number(useParams().id);
  const detail = useApi((signal) => api.importDetail(id, signal), [id]);
  const [tab, setTab] = useState<Tab>('summary');

  const d = detail.data;
  return (
    <>
      <PageHeader
        title={d ? d.fileName : 'Import'}
        subtitle={d ? `Import #${d.id} · uploaded ${formatDateTime(d.uploadedAt)}${d.uploadedBy ? ` by ${d.uploadedBy}` : ''}` : undefined}
        actions={
          <>
            <Link to="/imports" className="btn btn-secondary">
              <ArrowLeft size={16} /> Import history
            </Link>
            {d?.status === 'Validated' && (
              <Link to={`/upload/${d.id}/validate`} className="btn btn-primary">
                Continue import <ArrowRight size={16} />
              </Link>
            )}
            {d?.status === 'Completed' && (
              <Link to={`/data?import=${d.id}`} className="btn btn-primary">
                <Table2 size={16} /> View imported customers
              </Link>
            )}
          </>
        }
      />

      {detail.loading && !d && <Loading label="Loading import…" />}
      {detail.error && <ErrorPanel message={detail.error} onRetry={detail.reload} />}

      {d && (
        <>
          <div className="tabs">
            {(
              [
                ['summary', 'Summary', null],
                ['records', 'Records', d.summary?.totalRows ?? null],
                ['errors', 'Errors', d.summary?.errorRows ?? null],
                ['warnings', 'Warnings', d.summary?.warningRows ?? null],
              ] as [Tab, string, number | null][]
            ).map(([key, label, count]) => (
              <button key={key} className={tab === key ? 'active' : ''} onClick={() => setTab(key)}>
                {label}
                {count !== null && <span className="tab-count">{count.toLocaleString('en-IN')}</span>}
              </button>
            ))}
          </div>
          {tab === 'summary' && <Summary d={d} />}
          {tab !== 'summary' && <Rows batchId={d.id} status={tab === 'errors' ? 'Error' : tab === 'warnings' ? 'Warning' : undefined} key={tab} />}
        </>
      )}
    </>
  );
}

function Summary({ d }: { d: ImportDetail }) {
  const s = d.summary;
  return (
    <>
      <div className="mini-stat-grid mini-stat-grid-5">
        <MiniStat label="Rows in file" value={(s?.totalRows ?? 0).toLocaleString('en-IN')} tone="info" />
        <MiniStat label="Valid" value={(s?.validRows ?? 0).toLocaleString('en-IN')} tone="success" />
        <MiniStat label="With warnings" value={(s?.warningRows ?? 0).toLocaleString('en-IN')} tone="warning" />
        <MiniStat label="Errors" value={(s?.errorRows ?? 0).toLocaleString('en-IN')} tone="danger" />
        <MiniStat label="Imported" value={d.status === 'Completed' ? d.importedCount.toLocaleString('en-IN') : '—'} tone="success" />
      </div>
      <div className="grid-2">
        <Card title={<><FileSpreadsheet size={18} /> File</>}>
          <dl className="facts facts-wide">
            <dt>Status</dt>
            <dd>
              <Badge tone={statusTone(d.status)}>{statusLabel(d.status)}</Badge>
              {d.failureReason && <div className="item-sub text-danger">{d.failureReason}</div>}
            </dd>
            <dt>File</dt>
            <dd>
              {d.fileName} · {formatBytes(d.fileSizeBytes)}
              {d.sheetName && <div className="item-sub">Sheet “{d.sheetName}”</div>}
            </dd>
            <dt>Uploaded</dt>
            <dd>
              {formatDateTime(d.uploadedAt)}
              {d.uploadedBy && <div className="item-sub">by {d.uploadedBy}</div>}
            </dd>
            <dt>Completed</dt>
            <dd>{d.completedAt ? formatDateTime(d.completedAt) : '—'}</dd>
            <dt>Durations</dt>
            <dd>
              Validation {seconds(d.validationDurationMs)} · Save {seconds(d.saveDurationMs)}
            </dd>
            {d.emptyRowsSkipped > 0 && (
              <>
                <dt>Empty rows</dt>
                <dd>{d.emptyRowsSkipped} skipped</dd>
              </>
            )}
          </dl>
          {d.fileMessages.length > 0 && (
            <ul className="confirm-list" style={{ marginTop: 14 }}>
              {d.fileMessages.map((m) => (
                <li key={m}>{m}</li>
              ))}
            </ul>
          )}
        </Card>
        <Card title="Result">
          {d.status === 'Completed' ? (
            <dl className="facts facts-wide">
              <dt>Customers created</dt>
              <dd className="strong">{d.customersCreated.toLocaleString('en-IN')}</dd>
              <dt>Existing matched</dt>
              <dd>{d.existingCustomersMatched.toLocaleString('en-IN')} (never overwritten)</dd>
              <dt>Groups created</dt>
              <dd>{d.groupsCreated.toLocaleString('en-IN')}</dd>
              <dt>Assignments</dt>
              <dd>{d.assignmentsChanged.toLocaleString('en-IN')} changed</dd>
              {s && (
                <>
                  <dt>Locations</dt>
                  <dd>
                    {s.locations.toLocaleString('en-IN')} locations · {s.pincodes.toLocaleString('en-IN')} pincodes · {s.plans} plans
                  </dd>
                </>
              )}
            </dl>
          ) : (
            <p className="muted">
              {d.status === 'Validated'
                ? 'This import was validated but not saved yet. Continue it to classify, group and save the customers.'
                : d.status === 'Cancelled'
                  ? 'This import was cancelled. Nothing from it was saved.'
                  : d.status === 'Failed'
                    ? 'This import failed. Nothing from it was saved.'
                    : 'Nothing has been saved from this import.'}
            </p>
          )}
        </Card>
      </div>
    </>
  );
}

function Rows({ batchId, status }: { batchId: number; status?: string }) {
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const q = useDebounced(search.trim());
  const rows = useApi((signal) => api.importRows(batchId, { status, search: q || undefined }, page, 50, signal), [batchId, status, q, page]);

  return (
    <Card>
      <div className="toolbar">
        <label className="search">
          <Search size={16} />
          <input
            placeholder="Search rows"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
            aria-label="Search rows"
          />
        </label>
      </div>
      {rows.error && <ErrorPanel message={rows.error} onRetry={rows.reload} />}
      {rows.loading && !rows.data && <Loading label="Loading rows…" />}
      {rows.data && rows.data.total === 0 && (
        <EmptyState
          title={status === 'Error' ? 'No errors' : status === 'Warning' ? 'No warnings' : 'No rows'}
          message={q ? 'No rows match your search.' : status ? `This import has no rows with ${status.toLowerCase()}s.` : 'This import has no rows.'}
        />
      )}
      {rows.data && rows.data.total > 0 && (
        <>
          <div className="table-wrap">
            <table className="table rows-table">
              <thead>
                <tr>
                  <th className="num">Row</th>
                  <th>CustomerID</th>
                  <th>Name</th>
                  <th>Phone</th>
                  <th>Location</th>
                  <th>Pincode</th>
                  <th>Plan</th>
                  <th>Group</th>
                  <th>Status</th>
                  <th>Issues</th>
                </tr>
              </thead>
              <tbody>
                {rows.data.items.map((r) => (
                  <tr key={r.id} className={r.status === 'Error' ? 'row-error' : r.status === 'Warning' ? 'row-warn' : ''}>
                    <td className="num">{r.rowNumber}</td>
                    <td className="strong">{r.customerId ?? '—'}</td>
                    <td>{r.customerName ?? '—'}</td>
                    <td className="nowrap">{r.phoneNumber ?? '—'}</td>
                    <td className="cell-wide">{r.location ?? '—'}</td>
                    <td>{r.pincode ?? '—'}</td>
                    <td className="cell-wide">
                      {r.planName ?? r.plan ?? '—'}
                      {r.planName && r.plan && r.plan !== r.planName && <div className="item-sub">“{r.plan}”</div>}
                    </td>
                    <td>{r.group?.name ?? r.existingGroupName ?? '—'}</td>
                    <td>
                      <RowStatusBadge row={r} />
                    </td>
                    <td className="cell-issues">{r.issues.length > 0 ? <IssueList issues={r.issues} /> : <span className="muted">—</span>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination page={page} pageSize={50} total={rows.data.total} onPage={setPage} />
        </>
      )}
    </Card>
  );
}
