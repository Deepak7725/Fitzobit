import { useNavigate } from 'react-router-dom';
import { AlertTriangle, ArrowLeft, ArrowRight, Info, XCircle } from 'lucide-react';
import { api, type ImportDetail, type ImportReview } from '../../api';
import { useApi } from '../../hooks';
import { Badge, Card, ErrorPanel, Loading } from '../../components/ui';
import { MiniStat, StepFooter, stepPath } from './shared';
import { Chips } from './GroupsStep';

export function ImportScopeSentence({ review }: { review: ImportReview }) {
  const s = review.summary;
  const skipped = s.errorRows + s.excludedByManager;
  return (
    <p className="scope-sentence">
      <strong>{s.toImport.toLocaleString('en-IN')}</strong> record{s.toImport === 1 ? '' : 's'} will be imported:{' '}
      <strong>{s.newCustomers.toLocaleString('en-IN')}</strong> new customer{s.newCustomers === 1 ? '' : 's'} will be created
      {s.existingCustomers > 0 && (
        <>
          {' '}and <strong>{(s.toImport - s.newCustomers).toLocaleString('en-IN')}</strong> existing customer(s) matched (their saved details are not
          changed; only their group can change)
        </>
      )}
      .{' '}
      {skipped > 0 && (
        <>
          <strong>{skipped.toLocaleString('en-IN')}</strong> row{skipped === 1 ? '' : 's'} will be skipped ({s.errorRows} with errors
          {s.excludedByManager > 0 && `, ${s.excludedByManager} excluded by you`}).
        </>
      )}
    </p>
  );
}

export default function ReviewStep({ detail }: { detail: ImportDetail }) {
  const navigate = useNavigate();
  const review = useApi((signal) => api.review(detail.id, signal), [detail.id]);
  const r = review.data;

  return (
    <>
      {review.loading && !r && <Loading />}
      {review.error && <ErrorPanel message={review.error} onRetry={review.reload} />}
      {r && (
        <>
          <Card title="What will be saved">
            <ImportScopeSentence review={r} />
            <div className="mini-stat-grid mini-stat-grid-6">
              <MiniStat label="Customers to import" value={r.summary.toImport} tone="info" />
              <MiniStat label="New" value={r.summary.newCustomers} tone="success" />
              <MiniStat label="Existing" value={r.summary.toImport - r.summary.newCustomers} />
              <MiniStat label="With warnings" value={r.summary.warningRows} tone={r.summary.warningRows ? 'warning' : undefined} />
              <MiniStat label="Errors (skipped)" value={r.summary.errorRows} tone={r.summary.errorRows ? 'danger' : undefined} />
              <MiniStat label="Group changes for existing" value={r.groupChangesForExistingCustomers} />
            </div>
          </Card>

          <Card title={`Groups (${r.groups.length})`} subtitle="New groups are created; customers are added to these groups">
            {r.groups.length === 0 ? (
              <p className="muted">No customers are assigned to a group in this import.</p>
            ) : (
              <div className="table-wrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Group</th>
                      <th className="num">Customers</th>
                      <th className="num">New customers</th>
                      <th>Pincodes</th>
                      <th className="num">Locations</th>
                      <th>Plans</th>
                    </tr>
                  </thead>
                  <tbody>
                    {r.groups.map((g) => (
                      <tr key={g.key}>
                        <td>
                          <div className="item-name">
                            {g.name} {g.isNew ? <Badge tone="info">Will be created</Badge> : <Badge tone="neutral">Existing</Badge>}
                          </div>
                          {g.description && <div className="item-sub">{g.description}</div>}
                        </td>
                        <td className="num strong">{g.customerCount}</td>
                        <td className="num">{g.newCustomerCount}</td>
                        <td>
                          <Chips values={g.pincodes} more={g.pincodeCount - g.pincodes.length} />
                        </td>
                        <td className="num">{g.locationCount}</td>
                        <td>
                          <Chips values={g.plans} more={g.planCount - g.plans.length} />
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>

          <div className="grid-2">
            <Card title="Exceptions">
              <ul className="exception-list">
                <ExceptionItem
                  label="Unassigned customers"
                  count={r.unassigned}
                  hint="Saved without a delivery group"
                  action={() => navigate(stepPath(detail.id, 'assign'))}
                />
                <ExceptionItem label="Unknown or ambiguous plan" count={r.unknownPlan} hint="These rows are skipped" severity="error" action={() => navigate(stepPath(detail.id, 'classify'))} />
                <ExceptionItem label="Missing pincode" count={r.missingPincode} hint="Imported; the original location is kept" />
              </ul>
            </Card>
            <Card title="Validation findings">
              <h4 className="list-heading">Warnings on imported rows</h4>
              {r.warnings.length === 0 ? (
                <p className="muted small">None</p>
              ) : (
                <ul className="count-list">
                  {r.warnings.map((w) => (
                    <li key={w.code}>
                      <AlertTriangle size={14} className="text-warning" /> {w.label} <strong>{w.count}</strong>
                    </li>
                  ))}
                </ul>
              )}
              <h4 className="list-heading">Errors (rows skipped)</h4>
              {r.excludedErrors.length === 0 ? (
                <p className="muted small">None</p>
              ) : (
                <ul className="count-list">
                  {r.excludedErrors.map((e) => (
                    <li key={e.code}>
                      <XCircle size={14} className="text-danger" /> {e.label} <strong>{e.count}</strong>
                    </li>
                  ))}
                </ul>
              )}
            </Card>
          </div>

          {r.blockers.length > 0 && (
            <div className="error-panel error-block">
              <XCircle size={18} />
              <div>
                {r.blockers.map((b) => (
                  <p key={b}>{b}</p>
                ))}
              </div>
            </div>
          )}

          <StepFooter
            note={
              <>
                <Info size={14} /> Nothing has been saved yet.
              </>
            }
          >
            <button className="btn btn-secondary" onClick={() => navigate(stepPath(detail.id, 'assign'))}>
              <ArrowLeft size={16} /> Back
            </button>
            <button className="btn btn-primary" disabled={!r.canSave} onClick={() => navigate(stepPath(detail.id, 'save'))}>
              Next: Save <ArrowRight size={16} />
            </button>
          </StepFooter>
        </>
      )}
    </>
  );
}

function ExceptionItem({ label, count, hint, action, severity = 'warning' }: {
  label: string;
  count: number;
  hint: string;
  action?: () => void;
  severity?: 'warning' | 'error';
}) {
  return (
    <li className={count > 0 ? `exception-${severity}` : ''}>
      <div>
        <strong>{label}</strong>
        <small className="muted">{hint}</small>
      </div>
      <span className="exception-count">{count.toLocaleString('en-IN')}</span>
      {action && count > 0 && (
        <button className="link-button" onClick={action}>
          Fix
        </button>
      )}
    </li>
  );
}
