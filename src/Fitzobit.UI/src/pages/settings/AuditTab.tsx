import { Fragment, useState } from 'react';
import { ChevronDown, ChevronRight, ScrollText, Search } from 'lucide-react';
import { api } from '../../api';
import { useApi, useDebounced } from '../../hooks';
import { formatDateTime } from '../../format';
import { Card, EmptyState, ErrorPanel, Loading } from '../../components/ui';
import { Pagination } from '../../components/feedback';
import { AuditChanges } from '../../components/CustomerDrawer';

const PAGE_SIZE = 50;

/** Who changed what, when — with old and new values (Admin only). */
export default function AuditTab() {
  const [action, setAction] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [open, setOpen] = useState<number | null>(null);
  const q = useDebounced(search.trim());
  const actions = useApi(api.auditActions);
  const log = useApi((signal) => api.audit({ action: action || undefined, search: q || undefined }, page, PAGE_SIZE, signal), [action, q, page]);

  return (
    <Card title={<><ScrollText size={18} /> Audit log</>} subtitle="Important changes: who, what, when, and the old and new values.">
      <div className="toolbar">
        <label className="search">
          <Search size={16} />
          <input
            placeholder="Search summary, record or user"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
            aria-label="Search audit log"
          />
        </label>
        <select
          className="select"
          value={action}
          onChange={(e) => {
            setAction(e.target.value);
            setPage(1);
          }}
          aria-label="Action"
        >
          <option value="">All actions</option>
          {actions.data?.map((a) => (
            <option key={a}>{a}</option>
          ))}
        </select>
      </div>
      {log.error && <ErrorPanel message={log.error} onRetry={log.reload} />}
      {log.loading && !log.data && <Loading label="Loading audit log…" />}
      {log.data && log.data.total === 0 && <EmptyState icon={<ScrollText size={22} />} title="No entries" message="No audit entries match." />}
      {log.data && log.data.total > 0 && (
        <>
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th aria-label="Expand" />
                  <th>When</th>
                  <th>Who</th>
                  <th>Action</th>
                  <th>Record</th>
                  <th>Summary</th>
                </tr>
              </thead>
              <tbody>
                {log.data.items.map((a) => {
                  const expandable = !!(a.oldValues || a.newValues || a.details);
                  return (
                    <Fragment key={a.id}>
                      <tr className={expandable ? 'row-click' : ''} onClick={() => expandable && setOpen(open === a.id ? null : a.id)}>
                        <td>{expandable && (open === a.id ? <ChevronDown size={15} /> : <ChevronRight size={15} />)}</td>
                        <td className="nowrap">{formatDateTime(a.occurredAt)}</td>
                        <td>{a.performedBy ?? '—'}</td>
                        <td>
                          <span className="tag">{a.action}</span>
                        </td>
                        <td>
                          <div>{a.entityType}</div>
                          {a.entityKey && <div className="item-sub">{a.entityKey}</div>}
                        </td>
                        <td>{a.summary}</td>
                      </tr>
                      {open === a.id && (
                        <tr className="detail-row">
                          <td />
                          <td colSpan={5}>
                            <AuditChanges entry={a} />
                            {a.details !== null && a.details !== undefined && <pre className="pre small">{JSON.stringify(a.details, null, 2)}</pre>}
                          </td>
                        </tr>
                      )}
                    </Fragment>
                  );
                })}
              </tbody>
            </table>
          </div>
          <Pagination page={page} pageSize={PAGE_SIZE} total={log.data.total} onPage={setPage} />
        </>
      )}
    </Card>
  );
}
