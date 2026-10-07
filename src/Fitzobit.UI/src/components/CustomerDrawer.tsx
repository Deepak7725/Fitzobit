import { useEffect, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { ArrowRight, History, Pencil, RefreshCw, Shuffle, X } from 'lucide-react';
import { api, type AuditEntry, type CustomerDetail, type FilterOption, type JsonValue, type PlanOption } from '../api';
import { useApi } from '../hooks';
import { formatDateTime } from '../format';
import { Badge, ErrorPanel, Loading } from './ui';
import { useToast } from './feedback';
import ChangeGroupDialog from './ChangeGroupDialog';

type Tab = 'details' | 'history' | 'audit';

/** Side panel for one customer: details, delivery group and its history, import, audit trail; edit and change group. */
export default function CustomerDrawer({ id, groups, onClose, onChanged }: {
  id: number;
  groups: FilterOption[];
  onClose: () => void;
  onChanged: () => void;
}) {
  const customer = useApi((signal) => api.customer(id, signal), [id]);
  const [tab, setTab] = useState<Tab>('details');
  const [editing, setEditing] = useState(false);
  const [changingGroup, setChangingGroup] = useState(false);

  useEffect(() => {
    setTab('details');
    setEditing(false);
  }, [id]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && !changingGroup && onClose();
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose, changingGroup]);

  const c = customer.data;
  const changed = () => {
    customer.reload();
    onChanged();
  };

  return (
    <>
      <div className="drawer-backdrop" onMouseDown={onClose} />
      <aside className="drawer" role="dialog" aria-modal="true" aria-label="Customer details">
        <header className="drawer-header">
          <div>
            <span className="muted small">{c?.customerId ?? 'Customer'}</span>
            <h2>{c?.customerName ?? 'Loading…'}</h2>
            {c && (
              <div className="drawer-badges">
                <Badge tone={c.status === 'Active' ? 'success' : 'neutral'}>{c.status}</Badge>
                {c.groupName ? <Badge tone="info">{c.groupName}</Badge> : <Badge tone="warning">No delivery group</Badge>}
              </div>
            )}
          </div>
          <button className="icon-btn" onClick={onClose} aria-label="Close">
            <X size={18} />
          </button>
        </header>

        {c && !editing && (
          <div className="drawer-actions">
            <button className="btn btn-secondary btn-sm" onClick={() => setEditing(true)}>
              <Pencil size={14} /> Edit
            </button>
            <button className="btn btn-secondary btn-sm" onClick={() => setChangingGroup(true)}>
              <Shuffle size={14} /> Change group
            </button>
            <button className="btn btn-secondary btn-sm" onClick={() => setTab('history')}>
              <History size={14} /> View history
            </button>
          </div>
        )}

        <div className="drawer-body">
          {customer.loading && !c && <Loading label="Loading customer…" />}
          {customer.error && <ErrorPanel message={customer.error} onRetry={customer.reload} />}
          {c && editing && (
            <EditCustomerForm
              key={c.updatedAt}
              customer={c}
              onCancel={() => setEditing(false)}
              onSaved={() => {
                setEditing(false);
                changed();
              }}
              onReload={customer.reload}
            />
          )}
          {c && !editing && (
            <>
              <div className="tabs tabs-inline drawer-tabs">
                {(['details', 'history', 'audit'] as Tab[]).map((t) => (
                  <button key={t} className={tab === t ? 'active' : ''} onClick={() => setTab(t)}>
                    {t === 'details' ? 'Details' : t === 'history' ? `Group history (${c.history.length})` : `Audit (${c.audit.length})`}
                  </button>
                ))}
              </div>
              {tab === 'details' && <Details c={c} />}
              {tab === 'history' && <GroupHistory c={c} />}
              {tab === 'audit' && <AuditTrail entries={c.audit} />}
            </>
          )}
        </div>
      </aside>
      {changingGroup && c && (
        <ChangeGroupDialog
          customerIds={[c.id]}
          groups={groups}
          currentGroupId={c.groupId}
          onClose={() => setChangingGroup(false)}
          onDone={() => {
            setChangingGroup(false);
            changed();
          }}
        />
      )}
    </>
  );
}

function Details({ c }: { c: CustomerDetail }) {
  return (
    <>
      <h4 className="list-heading">Customer</h4>
      <dl className="facts facts-wide">
        <dt>CustomerID</dt>
        <dd className="strong">{c.customerId}</dd>
        <dt>Name</dt>
        <dd>{c.customerName}</dd>
        <dt>Phone</dt>
        <dd>
          {c.phoneNumber}
          {c.normalizedPhoneNumber && c.normalizedPhoneNumber !== c.phoneNumber && <div className="item-sub">Normalized {c.normalizedPhoneNumber}</div>}
        </dd>
        <dt>Location</dt>
        <dd>
          {c.location ?? '—'}
          {(c.locality || c.city) && <div className="item-sub">{[c.locality, c.city].filter(Boolean).join(' · ')}</div>}
        </dd>
        <dt>Pincode</dt>
        <dd>{c.pincode ?? <span className="text-warning">Not detected</span>}</dd>
        <dt>Plan</dt>
        <dd>
          {c.planName ?? <span className="text-warning">No plan</span>}
          {c.tierName && <div className="item-sub">{c.tierName}</div>}
          {c.originalPlan && c.originalPlan !== c.planName && <div className="item-sub">From file: “{c.originalPlan}”</div>}
        </dd>
      </dl>

      <h4 className="list-heading drawer-section">Delivery</h4>
      <dl className="facts facts-wide">
        <dt>Current group</dt>
        <dd>{c.groupName ?? <span className="text-warning">No delivery group</span>}</dd>
        <dt>Assigned</dt>
        <dd>
          {c.assignedAt ? formatDateTime(c.assignedAt) : '—'}
          {c.assignedBy && <div className="item-sub">by {c.assignedBy}</div>}
        </dd>
        <dt>Changes</dt>
        <dd>{c.history.length} group change(s)</dd>
      </dl>

      <h4 className="list-heading drawer-section">Import</h4>
      <dl className="facts facts-wide">
        <dt>Import batch</dt>
        <dd>
          {c.importBatch ? (
            <Link to={`/imports/${c.importBatch.id}`} className="link">
              #{c.importBatch.id} {c.importBatch.fileName} <ArrowRight size={13} />
            </Link>
          ) : (
            '—'
          )}
        </dd>
        <dt>Created</dt>
        <dd>
          {formatDateTime(c.createdAt)}
          {c.createdBy && <div className="item-sub">by {c.createdBy}</div>}
        </dd>
        <dt>Updated</dt>
        <dd>
          {formatDateTime(c.updatedAt)}
          {c.updatedBy && <div className="item-sub">by {c.updatedBy}</div>}
        </dd>
      </dl>
    </>
  );
}

function GroupHistory({ c }: { c: CustomerDetail }) {
  if (c.history.length === 0) return <p className="muted">This customer has never been assigned to a delivery group.</p>;
  return (
    <ol className="timeline">
      {c.history.map((h, i) => (
        <li key={i}>
          <div className="timeline-title">
            {h.previousGroupName && h.newGroupName
              ? `${h.previousGroupName} → ${h.newGroupName}`
              : h.newGroupName
                ? `Assigned to ${h.newGroupName}`
                : `Removed from ${h.previousGroupName ?? 'group'}`}
          </div>
          <div className="item-sub">
            {formatDateTime(h.changedAt)}
            {h.changedBy ? ` · ${h.changedBy}` : ''}
          </div>
          {h.reason && <div className="timeline-note">{h.reason}</div>}
        </li>
      ))}
    </ol>
  );
}

function show(value: JsonValue | undefined): string {
  if (value === null || value === undefined || value === '') return '—';
  return typeof value === 'object' ? JSON.stringify(value) : String(value);
}

export function AuditChanges({ entry }: { entry: AuditEntry }) {
  const fields = Object.keys(entry.newValues ?? entry.oldValues ?? {});
  if (fields.length === 0) return null;
  return (
    <table className="changes">
      <tbody>
        {fields.map((f) => (
          <tr key={f}>
            <th>{f}</th>
            <td className="old">{show(entry.oldValues?.[f])}</td>
            <td className="new">{show(entry.newValues?.[f])}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function AuditTrail({ entries }: { entries: AuditEntry[] }) {
  if (entries.length === 0) return <p className="muted">No recorded changes.</p>;
  return (
    <ol className="timeline">
      {entries.map((a) => (
        <li key={a.id}>
          <div className="timeline-title">{a.summary}</div>
          <div className="item-sub">
            {formatDateTime(a.occurredAt)}
            {a.performedBy ? ` · ${a.performedBy}` : ''} · {a.action}
          </div>
          <AuditChanges entry={a} />
        </li>
      ))}
    </ol>
  );
}

function EditCustomerForm({ customer, onCancel, onSaved, onReload }: {
  customer: CustomerDetail;
  onCancel: () => void;
  onSaved: () => void;
  onReload: () => void;
}) {
  const toast = useToast();
  const plans = useApi(api.planOptions);
  const [name, setName] = useState(customer.customerName);
  const [phone, setPhone] = useState(customer.phoneNumber);
  const [location, setLocation] = useState(customer.location ?? '');
  const [planId, setPlanId] = useState<number | null>(customer.planId);
  const [tierId, setTierId] = useState<number | null>(customer.pricingTierId);
  const [status, setStatus] = useState(customer.status);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);

  const options: PlanOption[] = plans.data ?? [];
  const selectedPlan = options.find((p) => p.id === planId);
  const currentPlanInactive = customer.planId !== null && plans.data !== null && !options.some((p) => p.id === customer.planId);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    setConflict(false);
    try {
      const result = await api.updateCustomer(customer.id, {
        customerName: name,
        phoneNumber: phone,
        location,
        planId,
        pricingTierId: tierId,
        status,
        expectedUpdatedAt: customer.updatedAt,
      });
      if (!result.changed) toast('Nothing changed.', 'info');
      else toast(`Saved: ${result.changedFields.join(', ')}.`);
      result.warnings.forEach((w) => toast(w, 'info'));
      onSaved();
    } catch (err) {
      const message = err instanceof Error ? err.message : 'The customer could not be saved.';
      setError(message);
      setConflict(message.includes('after you opened it'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={submit} className="drawer-form">
      <div className="auth-note">CustomerID {customer.customerId} is the business key and cannot be changed.</div>
      <label className="field">
        Customer name
        <input className="input" value={name} maxLength={150} onChange={(e) => setName(e.target.value)} required />
      </label>
      <label className="field">
        Phone number
        <input className="input" value={phone} inputMode="tel" maxLength={30} onChange={(e) => setPhone(e.target.value)} required />
        <small className="field-hint">10-digit Indian mobile number; +91 or a leading 0 is accepted.</small>
      </label>
      <label className="field">
        Location
        <textarea className="input textarea" value={location} maxLength={500} onChange={(e) => setLocation(e.target.value)} required />
        <small className="field-hint">Include the 6-digit pincode so the customer can be grouped by pincode.</small>
      </label>
      <label className="field">
        Plan
        <select
          className="select select-full"
          value={planId ?? ''}
          onChange={(e) => {
            setPlanId(e.target.value ? Number(e.target.value) : null);
            setTierId(null);
          }}
          disabled={plans.loading && !plans.data}
        >
          <option value="">No plan</option>
          {currentPlanInactive && <option value={customer.planId!}>{customer.planName} (inactive)</option>}
          {options.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name}
            </option>
          ))}
        </select>
      </label>
      {selectedPlan && selectedPlan.tiers.length > 0 && (
        <label className="field">
          Variant
          <select className="select select-full" value={tierId ?? ''} onChange={(e) => setTierId(e.target.value ? Number(e.target.value) : null)}>
            <option value="">Not specified</option>
            {selectedPlan.tiers.map((t) => (
              <option key={t.id} value={t.id}>
                {t.name}
              </option>
            ))}
          </select>
        </label>
      )}
      <label className="field">
        Status
        <select className="select select-full" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option>Active</option>
          <option>Inactive</option>
        </select>
      </label>
      {error && (
        <div className="auth-error" role="alert">
          {error}
          {conflict && (
            <button type="button" className="btn btn-secondary btn-sm" onClick={onReload}>
              <RefreshCw size={13} /> Reload customer
            </button>
          )}
        </div>
      )}
      <div className="drawer-footer">
        <button type="button" className="btn btn-secondary" onClick={onCancel} disabled={busy}>
          Cancel
        </button>
        <button className="btn btn-primary" disabled={busy}>
          {busy ? 'Saving…' : 'Save changes'}
        </button>
      </div>
    </form>
  );
}
