import { Link } from 'react-router-dom';
import { History } from 'lucide-react';
import { api } from '../api';
import { useApi } from '../hooks';
import { formatDateTime } from '../format';
import { Badge, Card, EmptyState, ErrorPanel, Loading, PageHeader } from '../components/ui';

export const statusTone = (status: string) =>
  status === 'Completed' ? 'success' : status === 'Failed' ? 'danger' : status === 'Validated' ? 'info' : 'neutral';

export const statusLabel = (status: string) => (status === 'Validated' ? 'Draft' : status);

const seconds = (ms: number) => (ms > 0 ? `${(ms / 1000).toFixed(1)} s` : '—');

export default function ImportHistoryPage() {
  const { data, error, loading, reload } = useApi(api.imports);

  return (
    <>
      <PageHeader title="Import History" subtitle="Every customer Excel upload, with its validation results and what was saved." />

      {loading && !data && <Loading />}
      {error && <ErrorPanel message={error} onRetry={reload} />}
      {data && (
        <Card title={`Imports (${data.length})`}>
          {data.length === 0 ? (
            <EmptyState icon={<History size={22} />} title="No imports yet" message="Uploads will be recorded here with row counts and status." />
          ) : (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th>File Name</th>
                    <th>Uploaded / By</th>
                    <th className="num">Total Rows</th>
                    <th className="num">Valid</th>
                    <th className="num">Warnings</th>
                    <th className="num">Errors</th>
                    <th className="num">Imported</th>
                    <th>Status</th>
                    <th>Duration</th>
                    <th aria-label="Actions" />
                  </tr>
                </thead>
                <tbody>
                  {data.map((b) => (
                    <tr key={b.id}>
                      <td>
                        <Link to={`/imports/${b.id}`} className="item-name item-link">{b.fileName}</Link>
                        {b.status === 'Completed' && (
                          <div className="item-sub">
                            {b.customersCreated} created · {b.groupsCreated} groups · {b.assignmentsChanged} assignments
                          </div>
                        )}
                        {b.failureReason && <div className="item-sub text-danger">{b.failureReason.split('\n')[0]}</div>}
                      </td>
                      <td>
                        {formatDateTime(b.uploadedAt)}
                        {b.uploadedBy && <div className="item-sub">by {b.uploadedBy}</div>}
                      </td>
                      <td className="num">{b.totalRows.toLocaleString('en-IN')}</td>
                      <td className="num">{b.validRows.toLocaleString('en-IN')}</td>
                      <td className="num">{b.warningRows.toLocaleString('en-IN')}</td>
                      <td className="num">{b.invalidRows.toLocaleString('en-IN')}</td>
                      <td className="num strong">{b.status === 'Completed' ? b.importedCount.toLocaleString('en-IN') : '—'}</td>
                      <td>
                        <Badge tone={statusTone(b.status)}>{statusLabel(b.status)}</Badge>
                        {b.completedAt && <div className="item-sub">{formatDateTime(b.completedAt)}</div>}
                      </td>
                      <td className="small">
                        <div>Validate {seconds(b.validationDurationMs)}</div>
                        {b.status === 'Completed' && <div className="item-sub">Save {seconds(b.saveDurationMs)}</div>}
                      </td>
                      <td>
                        {b.status === 'Validated' && (
                          <Link className="btn btn-secondary btn-sm" to={`/upload/${b.id}/validate`}>
                            Continue
                          </Link>
                        )}
                        {b.status !== 'Validated' && (
                          <Link className="btn btn-secondary btn-sm" to={`/imports/${b.id}`}>
                            Open
                          </Link>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Card>
      )}
    </>
  );
}
