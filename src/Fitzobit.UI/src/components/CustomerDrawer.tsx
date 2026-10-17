import { useEffect, useState, type FormEvent } from 'react';
import { History, LocateFixed, MessageSquareText, PauseCircle, Pencil, Plus, RefreshCw, Shuffle, Trash2, X } from 'lucide-react';
import { api, type AuditEntry, type CustomerDetail, type CustomerNote, type CustomerPackage, type FilterOption, type JsonValue } from '../api';
import { useApi, useDictionaries } from '../hooks';
import { formatDateTime, formatDay, formatTime, todayIso } from '../format';
import { Badge, ErrorPanel, Loading } from './ui';
import { ConfirmDialog, Modal, useToast } from './feedback';
import ChangeGroupDialog from './ChangeGroupDialog';
import { GroupPill, LocationBadge, formatCoordinate, groupLabel } from './groups';
import {
  CustomerFormFields,
  EMPTY_PACKAGE,
  Field,
  PackageFields,
  ReadOnlyValue,
  toCustomerRequest,
  toPackageRequest,
  useSchedulePreview,
  validate,
  validatePackage,
  type CustomerFormValues,
  type PackageFormValues,
} from './CustomerForm';

type Tab = 'details' | 'history' | 'audit';

export function statusTone(code: string): 'success' | 'warning' | 'neutral' {
  return code === 'ACTIVE' ? 'success' : code === 'PAUSED' ? 'warning' : 'neutral';
}

/** "Veg Medium ×1 · Weekly" */
export function packageName(p: { preference: string; mealType: string; quantity: number; frequency: string }): string {
  return `${p.preference} ${p.mealType} ×${p.quantity} · ${p.frequency}`;
}

/**
 * Side panel for one customer: details, packages and their pauses, notes, delivery group and its history, audit trail; edit the
 * customer, add/edit/delete packages, pause a day, write notes and change group.
 */
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
  const [pausing, setPausing] = useState(false);
  const [packageDialog, setPackageDialog] = useState<CustomerPackage | 'new' | null>(null);

  useEffect(() => {
    setTab('details');
    setEditing(false);
  }, [id]);

  const dialogOpen = changingGroup || pausing || packageDialog !== null;
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && !dialogOpen && onClose();
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose, dialogOpen]);

  const c = customer.data;

  // While a location lookup is pending, refresh every few seconds (the background geocoder usually answers within seconds).
  const pending = c?.geocodeStatus === 'Pending' && !c.geocodeNextAttemptAt;
  useEffect(() => {
    if (!pending || editing) return;
    let polls = 0;
    const timer = window.setInterval(() => {
      if (++polls > 20) window.clearInterval(timer);
      else customer.reload();
    }, 3000);
    return () => window.clearInterval(timer);
  }, [pending, editing]); // eslint-disable-line react-hooks/exhaustive-deps

  const changed = () => {
    customer.reload();
    onChanged();
  };
  const today = todayIso();
  const isActive = c?.statusCode === 'ACTIVE';
  const hasRunningPackage = !!c?.packages.some((p) => p.endDate >= today);
  const canPause = isActive && hasRunningPackage;
  const pauseHint = !c
    ? undefined
    : !isActive
      ? `${c.status} customers cannot pause a delivery; change the status to Active first`
      : !hasRunningPackage
        ? 'No package delivers from today on'
        : undefined;

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
                <Badge tone={statusTone(c.statusCode)}>{c.status}</Badge>
                <GroupPill groupNumber={c.groupNumber} />
                <Badge tone={c.packages.length > 0 ? 'neutral' : 'warning'}>
                  {c.packages.length === 0 ? 'No package' : `${c.packages.length} package${c.packages.length === 1 ? '' : 's'}`}
                </Badge>
                {c.preferredDeliveryTime && <Badge tone="info">{formatTime(c.preferredDeliveryTime)}</Badge>}
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
            <button className="btn btn-secondary btn-sm" onClick={() => setPackageDialog('new')}>
              <Plus size={14} /> Add package
            </button>
            <button className="btn btn-secondary btn-sm" onClick={() => setPausing(true)} disabled={!canPause} title={pauseHint}>
              <PauseCircle size={14} /> Pause a delivery
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
            <>
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
              <NotesSection customer={c} onChanged={changed} />
            </>
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
              {tab === 'details' && (
                <Details c={c} onChanged={changed} onEditPackage={(p) => setPackageDialog(p)} onAddPackage={() => setPackageDialog('new')} />
              )}
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
      {pausing && c && (
        <PauseDialog
          customer={c}
          onClose={() => setPausing(false)}
          onDone={() => {
            setPausing(false);
            changed();
          }}
        />
      )}
      {packageDialog && c && (
        <PackageDialog
          key={packageDialog === 'new' ? 'new' : packageDialog.id}
          customer={c}
          pkg={packageDialog === 'new' ? null : packageDialog}
          onClose={() => setPackageDialog(null)}
          onSaved={() => {
            setPackageDialog(null);
            changed();
          }}
        />
      )}
    </>
  );
}

function Missing({ label = 'Not set' }: { label?: string }) {
  return <span className="text-warning">{label}</span>;
}

function Details({ c, onChanged, onEditPackage, onAddPackage }: {
  c: CustomerDetail;
  onChanged: () => void;
  onEditPackage: (p: CustomerPackage) => void;
  onAddPackage: () => void;
}) {
  const toast = useToast();
  const [locating, setLocating] = useState(false);
  const retryLocation = async () => {
    setLocating(true);
    try {
      await api.locateCustomers([c.id]);
      toast('Location lookup queued. It usually takes a few seconds.', 'info');
      onChanged();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'The lookup could not be queued.', 'error');
    } finally {
      setLocating(false);
    }
  };

  const missing = [
    c.packages.length === 0 && 'a package',
    !c.preferredDeliveryTime && 'the preferred delivery time',
    !c.pincode && 'the pincode',
  ].filter(Boolean);
  return (
    <>
      {missing.length > 0 && (
        <div className="alert alert-warning compact-alert">
          <div>
            <strong>Details incomplete.</strong> Still missing: {missing.join(', ')}. Use Edit or Add package to complete them.
          </div>
        </div>
      )}
      <h4 className="list-heading">Customer</h4>
      <dl className="facts facts-wide">
        <dt>Customer ID</dt>
        <dd className="strong">{c.customerId}</dd>
        <dt>Name</dt>
        <dd>{c.customerName}</dd>
        <dt>Phone</dt>
        <dd>
          {c.phoneNumber}
          {c.normalizedPhoneNumber && c.normalizedPhoneNumber !== c.phoneNumber && <div className="item-sub">Normalized {c.normalizedPhoneNumber}</div>}
        </dd>
        <dt>Delivery time</dt>
        <dd>{c.preferredDeliveryTime ? formatTime(c.preferredDeliveryTime) : <Missing />}</dd>
        <dt>Address</dt>
        <dd>
          {c.address ?? '—'}
          {(c.locality || c.city) && <div className="item-sub">{[c.locality, c.city].filter(Boolean).join(' · ')}</div>}
        </dd>
        <dt>Pincode</dt>
        <dd>{c.pincode ?? <Missing />}</dd>
        <dt>Status</dt>
        <dd>
          <Badge tone={statusTone(c.statusCode)}>{c.status}</Badge>
        </dd>
        {c.legacyPlan && (
          <>
            <dt>Previous plan</dt>
            <dd>
              {c.legacyPlan}
              <div className="item-sub">From the previous plan/catalogue system (kept for reference)</div>
            </dd>
          </>
        )}
      </dl>

      <div className="section-heading-row drawer-section">
        <h4 className="list-heading">Packages ({c.packages.length})</h4>
        <button className="link-button small" onClick={onAddPackage}>
          <Plus size={13} /> Add package
        </button>
      </div>
      {c.packages.length === 0 ? (
        <p className="muted small">No package yet. Use “Add package”.</p>
      ) : (
        <div className="package-list">
          {c.packages.map((p) => (
            <PackageCard key={p.id} p={p} onEdit={() => onEditPackage(p)} onDeleted={onChanged} />
          ))}
        </div>
      )}

      <NotesSection customer={c} onChanged={onChanged} />

      <h4 className="list-heading drawer-section">Location</h4>
      <dl className="facts facts-wide">
        <dt>Status</dt>
        <dd>
          <LocationBadge status={c.geocodeStatus} level={c.geocodeMatchLevel} />
          {c.geocodeMessage && <div className="item-sub">{c.geocodeMessage}</div>}
          {c.geocodeStatus === 'Pending' && c.geocodeNextAttemptAt && (
            <div className="item-sub">Next automatic try {formatDateTime(c.geocodeNextAttemptAt)}</div>
          )}
        </dd>
        <dt>Latitude</dt>
        <dd className="mono">{c.latitude != null ? formatCoordinate(c.latitude) : <span className="muted">Location unavailable</span>}</dd>
        <dt>Longitude</dt>
        <dd className="mono">{c.longitude != null ? formatCoordinate(c.longitude) : <span className="muted">Location unavailable</span>}</dd>
        {c.geocodedAt && (
          <>
            <dt>Looked up</dt>
            <dd>
              {formatDateTime(c.geocodedAt)}
              {c.geocodeSource && <div className="item-sub">via {c.geocodeSource}</div>}
            </dd>
          </>
        )}
      </dl>
      <button className="btn btn-secondary btn-sm drawer-inline-action" onClick={retryLocation} disabled={locating}>
        <LocateFixed size={14} /> {c.geocodeStatus === 'Located' ? 'Look up location again' : 'Retry location lookup'}
      </button>

      <h4 className="list-heading drawer-section">Delivery</h4>
      <dl className="facts facts-wide">
        <dt>Current group</dt>
        <dd>{c.groupNumber != null ? <GroupPill groupNumber={c.groupNumber} /> : <Missing label="No delivery group" />}</dd>
        <dt>Assigned</dt>
        <dd>
          {c.assignedAt ? formatDateTime(c.assignedAt) : '—'}
          {c.assignedBy && <div className="item-sub">by {c.assignedBy}</div>}
        </dd>
      </dl>

      <h4 className="list-heading drawer-section">Record</h4>
      <dl className="facts facts-wide">
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

function PackageCard({ p, onEdit, onDeleted }: { p: CustomerPackage; onEdit: () => void; onDeleted: () => void }) {
  const toast = useToast();
  const [deleting, setDeleting] = useState(false);
  const [busy, setBusy] = useState(false);
  const ended = p.deliveryDaysLeft == null;
  const remove = async () => {
    setBusy(true);
    try {
      await api.deletePackage(p.id);
      toast(`Package ${packageName(p)} deleted.`);
      onDeleted();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'The package could not be deleted.', 'error');
    } finally {
      setBusy(false);
      setDeleting(false);
    }
  };
  return (
    <div className={`package-item ${ended ? 'is-ended' : ''}`}>
      <div className="package-item-header">
        <strong>{packageName(p)}</strong>
        <div className="package-item-actions">
          <button className="icon-btn" title="Edit package" aria-label={`Edit package ${packageName(p)}`} onClick={onEdit}>
            <Pencil size={14} />
          </button>
          {p.canDelete && (
            <button className="icon-btn danger" title="Delete package (not started yet)" aria-label={`Delete package ${packageName(p)}`}
              onClick={() => setDeleting(true)}>
              <Trash2 size={14} />
            </button>
          )}
        </div>
      </div>
      <dl className="facts facts-wide">
        <dt>Dates</dt>
        <dd>
          {formatDay(p.startDate, true)} – {formatDay(p.endDate, true)}
          <div className="item-sub">
            {ended ? 'Ended' : p.deliveryDaysLeft === 0 ? 'No deliveries left' : `${p.deliveryDaysLeft} delivery day(s) left`}
            {p.frequencyDeliveryDays != null && ` · ${p.frequencyDeliveryDays} delivery days per package`}
          </div>
        </dd>
        <dt>Paused</dt>
        <dd>
          {p.pausedCount} of {p.maxPauses}
          {p.pauses.length > 0 && (
            <ul className="plain-list pause-list">
              {p.pauses.map((x) => (
                <li key={x.pausedDate}>
                  <strong>{formatDay(x.pausedDate, true)}</strong> · end {formatDay(x.previousEndDate)} → {formatDay(x.newEndDate)}
                  {x.reason && <div className="item-sub">“{x.reason}”</div>}
                </li>
              ))}
            </ul>
          )}
        </dd>
      </dl>
      {deleting && (
        <ConfirmDialog
          danger
          title={`Delete package ${packageName(p)}?`}
          messages={[`It would have started on ${formatDay(p.startDate, true)}. Use this only for a package entered by mistake.`, 'The deletion is recorded in the audit log.']}
          confirmLabel="Delete package"
          busy={busy}
          onConfirm={() => void remove()}
          onCancel={() => setDeleting(false)}
        />
      )}
    </div>
  );
}

/** Dated notes: newest first; add here; the writer (or an Admin) edits or deletes. */
function NotesSection({ customer, onChanged }: { customer: CustomerDetail; onChanged: () => void }) {
  const toast = useToast();
  const [text, setText] = useState('');
  const [busy, setBusy] = useState(false);
  const [editing, setEditing] = useState<{ id: number; text: string } | null>(null);
  const [deleting, setDeleting] = useState<CustomerNote | null>(null);

  const run = async (action: () => Promise<unknown>, success: string, after?: () => void) => {
    setBusy(true);
    try {
      await action();
      toast(success);
      after?.();
      onChanged();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'The note could not be saved.', 'error');
    } finally {
      setBusy(false);
    }
  };

  const add = (e: FormEvent) => {
    e.preventDefault();
    if (!text.trim()) return;
    void run(() => api.addNote(customer.id, text.trim()), 'Note added.', () => setText(''));
  };

  return (
    <section className="notes-section">
      <h4 className="list-heading drawer-section">
        <MessageSquareText size={14} /> Notes ({customer.notes.length})
      </h4>
      <form className="note-add" onSubmit={add}>
        <textarea className="input textarea" rows={2} maxLength={2000} value={text} placeholder="Write a note, e.g. Call before delivery"
          onChange={(e) => setText(e.target.value)} aria-label="New note" />
        <button className="btn btn-secondary btn-sm" disabled={busy || !text.trim()}>
          <Plus size={14} /> Add note
        </button>
      </form>
      {customer.notes.length === 0 ? (
        <p className="muted small">No notes yet.</p>
      ) : (
        <ol className="note-list">
          {customer.notes.map((n) => (
            <li key={n.id}>
              <div className="note-meta">
                <span>
                  {formatDateTime(n.createdAt)} · {n.createdByName ?? n.createdBy ?? 'unknown'}
                  {n.edited && <span className="muted"> · edited {formatDateTime(n.updatedAt)}{n.updatedBy ? ` by ${n.updatedBy}` : ''}</span>}
                </span>
                {n.canEdit && editing?.id !== n.id && (
                  <span className="note-actions">
                    <button className="icon-btn icon-btn-xs" title="Edit note" aria-label="Edit note" onClick={() => setEditing({ id: n.id, text: n.text })}>
                      <Pencil size={13} />
                    </button>
                    <button className="icon-btn icon-btn-xs danger" title="Delete note" aria-label="Delete note" onClick={() => setDeleting(n)}>
                      <Trash2 size={13} />
                    </button>
                  </span>
                )}
              </div>
              {editing?.id === n.id ? (
                <div className="note-edit">
                  <textarea className="input textarea" rows={3} maxLength={2000} value={editing.text} autoFocus
                    onChange={(e) => setEditing({ id: n.id, text: e.target.value })} aria-label="Edit note" />
                  <div className="button-row">
                    <button className="btn btn-secondary btn-sm" onClick={() => setEditing(null)} disabled={busy}>
                      Cancel
                    </button>
                    <button className="btn btn-primary btn-sm" disabled={busy || !editing.text.trim()}
                      onClick={() => void run(() => api.updateNote(n.id, editing.text.trim()), 'Note saved.', () => setEditing(null))}>
                      Save note
                    </button>
                  </div>
                </div>
              ) : (
                <p className="note-text">{n.text}</p>
              )}
            </li>
          ))}
        </ol>
      )}
      {deleting && (
        <ConfirmDialog
          danger
          title="Delete this note?"
          messages={[`“${deleting.text.length > 120 ? `${deleting.text.slice(0, 120)}…` : deleting.text}”`, 'The deleted text stays in the audit log.']}
          confirmLabel="Delete note"
          busy={busy}
          onConfirm={() => void run(() => api.deleteNote(deleting.id), 'Note deleted.', () => setDeleting(null))}
          onCancel={() => setDeleting(null)}
        />
      )}
    </section>
  );
}

function GroupHistory({ c }: { c: CustomerDetail }) {
  if (c.history.length === 0) return <p className="muted">This customer has never been assigned to a delivery group.</p>;
  return (
    <ol className="timeline">
      {c.history.map((h, i) => (
        <li key={i}>
          <div className="timeline-title">
            {h.previousGroupNumber != null && h.newGroupNumber != null
              ? `${groupLabel(h.previousGroupNumber)} → ${groupLabel(h.newGroupNumber)}`
              : h.newGroupNumber != null
                ? `Assigned to ${groupLabel(h.newGroupNumber)}`
                : `Removed from ${h.previousGroupNumber != null ? groupLabel(h.previousGroupNumber) : 'group'}`}
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

function initialValues(c: CustomerDetail): CustomerFormValues {
  return {
    customerName: c.customerName,
    phoneNumber: c.phoneNumber,
    address: c.address ?? '',
    pincode: c.pincode ?? '',
    deliveryTime: c.preferredDeliveryTime ? c.preferredDeliveryTime.slice(0, 5) : '',
    statusId: String(c.statusId),
  };
}

/** Edit the customer's own fields (packages and notes have their own actions). */
function EditCustomerForm({ customer, onCancel, onSaved, onReload }: {
  customer: CustomerDetail;
  onCancel: () => void;
  onSaved: () => void;
  onReload: () => void;
}) {
  const toast = useToast();
  const dictionaries = useDictionaries();
  const [values, setValues] = useState<CustomerFormValues>(() => initialValues(customer));
  const [touched, setTouched] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);

  // A pincode the customer already has cannot be cleared; one missing since the old system may stay empty.
  const pincodeRequired = !!customer.pincode;
  const errors = touched ? validate(values, pincodeRequired) : {};

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setTouched(true);
    if (Object.keys(validate(values, pincodeRequired)).length > 0) return;
    setBusy(true);
    setError(null);
    setConflict(false);
    try {
      const result = await api.updateCustomer(customer.id, { ...toCustomerRequest(values), expectedUpdatedAt: customer.updatedAt });
      if (!result.changed) toast('Nothing changed.', 'info');
      else toast(`Saved: ${result.changedFields.join(', ')}.`);
      result.warnings.forEach((w) => toast(w, 'info'));
      onSaved();
    } catch (err) {
      const message = err instanceof Error ? err.message : 'The customer could not be saved.';
      setError(message);
      setConflict(message.includes('after you opened it') || message.includes('at the same time'));
    } finally {
      setBusy(false);
    }
  };

  if (dictionaries.error) return <ErrorPanel message={dictionaries.error} onRetry={dictionaries.reload} />;
  if (!dictionaries.data) return <Loading label="Loading form…" />;

  return (
    <form onSubmit={submit} className="drawer-form" noValidate>
      <CustomerFormFields
        compact
        values={values}
        onChange={setValues}
        errors={errors}
        dictionaries={dictionaries.data}
        customerId={customer.customerId}
        currentStatus={{ id: customer.statusId, name: customer.status }}
        extra={
          <Field label="Latitude / Longitude" hint="Looked up again automatically when the address or pincode changes.">
            <ReadOnlyValue muted={customer.latitude == null}>
              {customer.latitude != null && customer.longitude != null
                ? `${formatCoordinate(customer.latitude)}, ${formatCoordinate(customer.longitude)}`
                : 'Location unavailable'}
            </ReadOnlyValue>
          </Field>
        }
      />
      <p className="field-hint">Packages are added, changed or deleted with the package buttons in the customer panel.</p>
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

function packageValues(p: CustomerPackage): PackageFormValues {
  return {
    preferenceId: String(p.preferenceId),
    mealTypeId: String(p.mealTypeId),
    quantity: String(p.quantity),
    frequencyId: String(p.frequencyId),
    startDate: p.startDate,
  };
}

/** Add a package, or edit one (changing its start date or frequency recalculates the end date after a confirmation). */
function PackageDialog({ customer, pkg, onClose, onSaved }: { customer: CustomerDetail; pkg: CustomerPackage | null; onClose: () => void; onSaved: () => void }) {
  const toast = useToast();
  const dictionaries = useDictionaries();
  const [values, setValues] = useState<PackageFormValues>(() => (pkg ? packageValues(pkg) : EMPTY_PACKAGE));
  const [touched, setTouched] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);

  const scheduleChanged = !pkg || values.startDate !== pkg.startDate || values.frequencyId !== String(pkg.frequencyId);
  const schedule = useSchedulePreview(scheduleChanged ? values.frequencyId : '', scheduleChanged ? values.startDate : '');
  const errors = touched ? validatePackage(values) : {};

  const save = async (confirmScheduleChange: boolean) => {
    setBusy(true);
    setError(null);
    try {
      if (pkg) {
        const saved = await api.updatePackage(pkg.id, { ...toPackageRequest(values), expectedUpdatedAt: pkg.updatedAt, confirmScheduleChange });
        toast(`Package saved. It ends ${formatDay(saved.endDate, true)}.`);
      } else {
        const saved = await api.addPackage(customer.id, toPackageRequest(values));
        toast(`Package added: ${formatDay(saved.startDate, true)} – ${formatDay(saved.endDate, true)}.`);
      }
      onSaved();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'The package could not be saved.');
      setConfirming(false);
      setBusy(false);
    }
  };

  const submit = (e: FormEvent) => {
    e.preventDefault();
    setTouched(true);
    if (Object.keys(validatePackage(values)).length > 0 || schedule.error || (scheduleChanged && schedule.loading)) return;
    if (pkg && scheduleChanged) setConfirming(true);
    else void save(false);
  };

  const name = (list: { id: number; displayName: string }[] | undefined, id: string) => list?.find((o) => String(o.id) === id)?.displayName;
  const confirmMessages = pkg
    ? [
        values.startDate !== pkg.startDate ? `Start date: ${formatDay(pkg.startDate)} → ${formatDay(values.startDate)}` : null,
        values.frequencyId !== String(pkg.frequencyId) ? `Frequency: ${pkg.frequency} → ${name(dictionaries.data?.frequency, values.frequencyId) ?? '—'}` : null,
        `End date: ${formatDay(pkg.endDate)} → ${schedule.preview ? formatDay(schedule.preview.endDate) : 'recalculated'}${pkg.pauses.length > 0 ? ' plus paused days inside the new period' : ''}`,
        pkg.pauses.length > 0
          ? `${pkg.pauses.length} paused day(s) are kept; only those inside the new period count towards its ${pkg.maxPauses} pauses.`
          : null,
        'The previous dates stay in the audit history.',
      ].filter((m): m is string => !!m)
    : [];

  return (
    <Modal
      title={pkg ? `Edit package · ${packageName(pkg)}` : `Add a package for ${customer.customerName}`}
      onClose={() => !busy && onClose()}
      wide
      footer={
        <>
          <button className="btn btn-secondary" onClick={onClose} disabled={busy}>
            Cancel
          </button>
          <button className="btn btn-primary" type="submit" form="package-form" disabled={busy || !dictionaries.data}>
            {busy ? 'Saving…' : pkg ? 'Save package' : 'Add package'}
          </button>
        </>
      }
    >
      {dictionaries.error && <ErrorPanel message={dictionaries.error} onRetry={dictionaries.reload} />}
      {!dictionaries.data && !dictionaries.error && <Loading label="Loading form…" />}
      {dictionaries.data && (
        <form id="package-form" onSubmit={submit} noValidate>
          <PackageFields
            idPrefix="package-dialog"
            values={values}
            onChange={setValues}
            errors={errors}
            dictionaries={dictionaries.data}
            current={
              pkg
                ? {
                    preference: { id: pkg.preferenceId, name: pkg.preference },
                    mealType: { id: pkg.mealTypeId, name: pkg.mealType },
                    frequency: { id: pkg.frequencyId, name: pkg.frequency },
                  }
                : undefined
            }
            preview={schedule}
            endDateLabel={pkg && scheduleChanged ? 'New end date' : 'End date'}
            fallbackEndDate={pkg && !scheduleChanged ? pkg.endDate : null}
          />
          {pkg && (
            <p className="field-hint">
              Paused {pkg.pausedCount} of {pkg.maxPauses}. Pauses are added with “Pause a delivery”.
            </p>
          )}
          {error && (
            <div className="auth-error" role="alert">
              {error}
            </div>
          )}
        </form>
      )}
      {confirming && (
        <ConfirmDialog
          title="Change the package dates?"
          messages={confirmMessages}
          confirmLabel="Save and recalculate"
          busy={busy}
          onConfirm={() => void save(true)}
          onCancel={() => setConfirming(false)}
        />
      )}
    </Modal>
  );
}

/** Pauses one day for every package running that day (the server decides; this only shows what will happen). */
function PauseDialog({ customer, onClose, onDone }: { customer: CustomerDetail; onClose: () => void; onDone: () => void }) {
  const toast = useToast();
  const today = todayIso();
  const lastEnd = customer.packages.reduce((max, p) => (p.endDate > max ? p.endDate : max), today);
  const [date, setDate] = useState('');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const running = date ? customer.packages.filter((p) => p.startDate <= date && p.endDate >= date) : [];
  const full = running.filter((p) => p.pausedCount >= p.maxPauses && !p.pauses.some((x) => x.pausedDate === date));

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!date) {
      setError('Choose the delivery day to pause.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const result = await api.pauseCustomer(customer.id, date, reason.trim() || null);
      toast(
        `${formatDay(result.pausedDate, true)} paused for ${result.packages.length} package(s): ` +
          result.packages.map((p) => `ends ${formatDay(p.newEndDate)} (${p.pausesLeft} pause(s) left)`).join('; ') + '.',
      );
      onDone();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'The delivery could not be paused.');
      setBusy(false);
    }
  };

  return (
    <Modal
      title={`Pause a delivery for ${customer.customerName}`}
      onClose={onClose}
      footer={
        <>
          <button className="btn btn-secondary" onClick={onClose} disabled={busy}>
            Cancel
          </button>
          <button className="btn btn-primary" type="submit" form="pause-form" disabled={busy || full.length > 0}>
            {busy ? 'Pausing…' : 'Pause this day'}
          </button>
        </>
      }
    >
      <form id="pause-form" onSubmit={submit}>
        <ul className="confirm-list">
          <li>Every package running on that day is paused. No meal is lost: each package’s end date moves to the next delivery day.</li>
          <li>Sundays are never delivery days. At most 3 pauses per package. A pause cannot be undone.</li>
        </ul>
        <label className="field">
          Delivery day to pause
          <input className="input" type="date" value={date} min={today} max={lastEnd} onChange={(e) => setDate(e.target.value)} autoFocus />
        </label>
        {date && (
          <div className="pause-preview">
            {running.length === 0 ? (
              <p className="text-warning">No package delivers on {formatDay(date, true)}.</p>
            ) : (
              <>
                <p className="small strong">Packages paused on {formatDay(date, true)}:</p>
                <ul className="plain-list">
                  {running.map((p) => (
                    <li key={p.id} className={full.includes(p) ? 'text-warning' : ''}>
                      {packageName(p)} · ends {formatDay(p.endDate)} · {p.pausedCount} of {p.maxPauses} pauses used
                      {full.includes(p) && ' — no pauses left, so this day cannot be paused'}
                    </li>
                  ))}
                </ul>
              </>
            )}
          </div>
        )}
        <label className="field">
          Reason (optional)
          <input className="input" value={reason} maxLength={300} onChange={(e) => setReason(e.target.value)} placeholder="e.g. Out of town" />
        </label>
        {error && (
          <div className="auth-error" role="alert">
            {error}
          </div>
        )}
      </form>
    </Modal>
  );
}
