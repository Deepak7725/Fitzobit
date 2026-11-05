import { useEffect, useState, type FormEvent } from 'react';
import { ExternalLink, History, LocateFixed, MessageSquareText, PauseCircle, Pencil, Plus, RefreshCw, Shuffle, Trash2, X } from 'lucide-react';
import {
  api,
  type AuditEntry,
  type CustomerDetail,
  type CustomerNote,
  type CustomerPackage,
  type FilterOption,
  type JsonValue,
  type PauseResult,
  type RenewalDue,
} from '../api';
import { useApi, useDebounced, useDictionaries } from '../hooks';
import { formatDateTime, formatDay, formatTime, pausedDaysText, todayIso } from '../format';
import { Badge, ErrorPanel, Loading } from './ui';
import { ConfirmDialog, Modal, useToast } from './feedback';
import ChangeGroupDialog from './ChangeGroupDialog';
import { GroupPill, LocationBadge, formatCoordinate, groupLabel } from './groups';
import PauseCalendar, { type DayState } from './PauseCalendar';
import { RemindedNote, SendReminderButton } from './renewals';
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
  const isActive = c?.statusCode === 'ACTIVE' || c?.statusCode === 'PAUSED';
  const hasRunningPackage = !!c?.packages.some((p) => p.endDate >= today);
  const hasPausablePackage = !!c?.packages.some((p) => p.canPause && p.endDate >= today);
  const canPause = isActive && hasPausablePackage;
  const pauseHint = !c
    ? undefined
    : !isActive
      ? `${c.status} customers cannot pause a delivery; change the status to Active first`
      : !hasRunningPackage
        ? 'No package delivers from today on'
        : !hasPausablePackage
          ? 'None of the running packages can pause (only frequencies with “Can pause” in Settings → Dropdown values, e.g. Monthly)'
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
  // Renewal-due packages of this customer; reloaded whenever the customer is (e.g. after a pause or a new package).
  const renewals = useApi((signal) => api.renewals(c.id, signal), [c]);
  const renewalFor = (packageId: number) => renewals.data?.items.find((i) => i.packageId === packageId);
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
        <dt>Alternative phone</dt>
        <dd>{c.alternatePhoneNumber ?? <span className="muted">—</span>}</dd>
        <dt>Delivery time</dt>
        <dd>{c.preferredDeliveryTime ? formatTime(c.preferredDeliveryTime) : <Missing />}</dd>
        <dt>Address</dt>
        <dd>
          {c.address ?? '—'}
          {(c.locality || c.city) && <div className="item-sub">{[c.locality, c.city].filter(Boolean).join(' · ')}</div>}
        </dd>
        <dt>Pincode</dt>
        <dd>{c.pincode ?? <Missing />}</dd>
        <dt>Google Map</dt>
        <dd>
          {c.googleMapUrl ? (
            <a href={c.googleMapUrl} target="_blank" rel="noopener noreferrer" className="external-link">
              Open in Google Maps <ExternalLink size={12} />
            </a>
          ) : (
            <span className="muted">—</span>
          )}
        </dd>
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
            <PackageCard key={p.id} p={p} onEdit={() => onEditPackage(p)} onDeleted={onChanged} renewal={renewalFor(p.id)} onReminded={renewals.reload} />
          ))}
        </div>
      )}
      {c.pastPackages.length > 0 && (
        <details className="past-packages">
          <summary>Past packages ({c.pastPackages.length})</summary>
          <p className="muted small">Finished packages, moved here at 12 pm on their last day. Kept for history; use “Add package” for a renewal.</p>
          <div className="package-list">
            {c.pastPackages.map((p) => (
              <PackageCard key={p.id} p={p} past />
            ))}
          </div>
        </details>
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

/** One package; `past` packages are read-only (no edit or delete). `renewal` is set while its renewal is due. */
function PackageCard({ p, onEdit, onDeleted, past, renewal, onReminded }: {
  p: CustomerPackage;
  onEdit?: () => void;
  onDeleted?: () => void;
  past?: boolean;
  renewal?: RenewalDue;
  onReminded?: () => void;
}) {
  const toast = useToast();
  const [deleting, setDeleting] = useState(false);
  const [busy, setBusy] = useState(false);
  const ended = p.deliveryDaysLeft == null;
  const remove = async () => {
    setBusy(true);
    try {
      await api.deletePackage(p.id);
      toast(`Package ${packageName(p)} deleted.`);
      onDeleted?.();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'The package could not be deleted.', 'error');
    } finally {
      setBusy(false);
      setDeleting(false);
    }
  };
  return (
    <div className={`package-item ${ended || past ? 'is-ended' : ''}`}>
      <div className="package-item-header">
        <strong>{packageName(p)}</strong>
        {!past && (
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
        )}
      </div>
      {renewal && (
        <div className="renewal-box">
          <div>
            <Badge tone="warning">Renewal due</Badge>{' '}
            <span className="small">
              {renewal.daysLeft} delivery day{renewal.daysLeft === 1 ? '' : 's'} left
            </span>
            <div>
              <RemindedNote item={renewal} />
            </div>
          </div>
          <SendReminderButton item={renewal} onRecorded={() => onReminded?.()} small />
        </div>
      )}
      <dl className="facts facts-wide">
        <dt>Dates</dt>
        <dd>
          {formatDay(p.startDate, true)} – {formatDay(p.endDate, true)}
          <div className="item-sub">
            {past ? 'Finished' : ended ? 'Ended' : p.deliveryDaysLeft === 0 ? 'No deliveries left' : `${p.deliveryDaysLeft} delivery day(s) left`}
            {p.frequencyDeliveryDays != null && ` · ${p.frequencyDeliveryDays} delivery days per package`}
          </div>
        </dd>
        <dt>Paused</dt>
        <dd>
          {p.pauses.length === 0 ? (
            p.canPause || past ? 'No paused days' : <span className="muted">{p.frequency} packages cannot pause</span>
          ) : (
            <strong>{pausedDaysText(p.pauses.filter((x) => !x.festive).length, p.pauses.filter((x) => x.festive).length)}</strong>
          )}
          {p.pauses.length > 0 && (
            <div className="item-sub">
              {p.pauses.every((x) => x.extendedEndDate)
                ? 'Each paused day moved the end date one delivery day'
                : 'Each paused day moves the end date; some earlier days (old 3-day limit) did not'}
            </div>
          )}
          {p.pauses.length > 0 && (
            <ul className="plain-list pause-list">
              {p.pauses.map((x) => (
                <li key={x.pausedDate}>
                  <strong>{formatDay(x.pausedDate, true)}</strong> ·{' '}
                  {x.extendedEndDate ? `end ${formatDay(x.previousEndDate)} → ${formatDay(x.newEndDate)}` : 'end date not changed'}
                  {x.festive ? (
                    <div className="item-sub">Fitzobit closed{x.reason ? `: ${x.reason}` : ''} (paused for everyone)</div>
                  ) : (
                    x.reason && <div className="item-sub">“{x.reason}”</div>
                  )}
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
    alternatePhoneNumber: c.alternatePhoneNumber ?? '',
    address: c.address ?? '',
    pincode: c.pincode ?? '',
    googleMapUrl: c.googleMapUrl ?? '',
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

/**
 * Add a package, or edit one. Changing its start date or frequency recalculates the end date after a confirmation; when editing,
 * the end date can also be typed (a Monday–Saturday date on or after the start). Typing is forgotten when the start date or
 * frequency changes, so the recalculated date shows again.
 */
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
  const [typedEndDate, setTypedEndDate] = useState<string | null>(null);
  const calculatedEndDate = pkg ? (scheduleChanged ? (schedule.preview?.endDate ?? '') : pkg.endDate) : '';
  const endDate = typedEndDate ?? calculatedEndDate;
  const endDateError = !pkg || typedEndDate == null ? undefined : endDateProblem(typedEndDate, values.startDate);
  const changeValues = (next: PackageFormValues) => {
    if (next.startDate !== values.startDate || next.frequencyId !== values.frequencyId) setTypedEndDate(null);
    setValues(next);
  };

  const save = async (confirmScheduleChange: boolean) => {
    setBusy(true);
    setError(null);
    try {
      if (pkg) {
        const saved = await api.updatePackage(pkg.id, {
          ...toPackageRequest(values),
          expectedUpdatedAt: pkg.updatedAt,
          confirmScheduleChange,
          endDate: typedEndDate,
        });
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
    if (Object.keys(validatePackage(values)).length > 0 || endDateError || schedule.error || (scheduleChanged && schedule.loading)) return;
    if (pkg && scheduleChanged) setConfirming(true);
    else void save(false);
  };

  const name = (list: { id: number; displayName: string }[] | undefined, id: string) => list?.find((o) => String(o.id) === id)?.displayName;
  const confirmMessages = pkg
    ? [
        values.startDate !== pkg.startDate ? `Start date: ${formatDay(pkg.startDate)} → ${formatDay(values.startDate)}` : null,
        values.frequencyId !== String(pkg.frequencyId) ? `Frequency: ${pkg.frequency} → ${name(dictionaries.data?.frequency, values.frequencyId) ?? '—'}` : null,
        typedEndDate != null
          ? `End date: ${formatDay(pkg.endDate)} → ${formatDay(typedEndDate)} (typed)`
          : `End date: ${formatDay(pkg.endDate)} → ${schedule.preview ? formatDay(schedule.preview.endDate) : 'recalculated'}${pkg.pauses.length > 0 ? ' plus the paused days inside the new period' : ''}`,
        pkg.pauses.length > 0 ? `${pkg.pauses.length} paused day(s) are kept; each one inside the new period extends it.` : null,
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
            onChange={changeValues}
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
            editableEndDate={
              pkg
                ? {
                    value: endDate,
                    onChange: (value) => setTypedEndDate(value === calculatedEndDate ? null : value),
                    error: touched ? endDateError : undefined,
                    typed: typedEndDate != null,
                    onReset: () => setTypedEndDate(null),
                  }
                : undefined
            }
          />
          {pkg && (
            <p className="field-hint">
              {pkg.pauses.length > 0
                ? `Paused days: ${pausedDaysText(pkg.pauses.filter((x) => !x.festive).length, pkg.pauses.filter((x) => x.festive).length)}; each moves the end date. `
                : ''}
              Pauses are added with “Pause a delivery”{pkg.canPause ? '' : ` (${pkg.frequency} packages cannot pause)`}.
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

/** Why a typed end date is not allowed, or undefined when it is fine (the server checks again). */
function endDateProblem(endDate: string, startDate: string): string | undefined {
  if (!endDate) return 'Choose the last delivery day.';
  if (startDate && endDate < startDate) return 'The end date cannot be before the start date.';
  if (new Date(`${endDate}T00:00:00Z`).getUTCDay() === 0) return 'Sunday is not a delivery day; choose Monday–Saturday.';
  return undefined;
}

/**
 * Pauses several delivery days at once, chosen on a calendar. Only packages whose frequency can pause are paused; every paused day
 * moves the package's end date (no limit). The server decides; the preview below the calendar is the server's own answer for the
 * chosen days.
 */
function PauseDialog({ customer, onClose, onDone }: { customer: CustomerDetail; onClose: () => void; onDone: () => void }) {
  const toast = useToast();
  const today = todayIso();
  const pausable = customer.packages.filter((p) => p.canPause && p.endDate >= today);
  const lastEnd = pausable.reduce((max, p) => (p.endDate > max ? p.endDate : max), today);
  const [selected, setSelected] = useState<string[]>([]);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [preview, setPreview] = useState<{ key: string; result: PauseResult | null; error: string | null }>({ key: '', result: null, error: null });

  const key = useDebounced(selected.join(','), 300);
  useEffect(() => {
    if (!key) return;
    let cancelled = false;
    api
      .previewPause(customer.id, key.split(','))
      .then((result) => !cancelled && setPreview({ key, result, error: null }))
      .catch((e: unknown) => !cancelled && setPreview({ key, result: null, error: e instanceof Error ? e.message : 'Could not check these days.' }));
    return () => {
      cancelled = true;
    };
  }, [key, customer.id]);
  const current = selected.length > 0 && preview.key === selected.join(',');
  const checking = selected.length > 0 && !current;

  const dayState = (day: string): DayState => {
    if (new Date(`${day}T00:00:00Z`).getUTCDay() === 0) return { selectable: false, note: 'Sunday: no delivery' };
    if (day < today) return { selectable: false, note: 'Past day' };
    const running = pausable.filter((p) => p.startDate <= day && p.endDate >= day);
    if (running.length === 0) return { selectable: false, note: 'No package that can pause delivers on this day' };
    if (running.every((p) => p.pauses.some((x) => x.pausedDate === day))) return { selectable: false, note: 'Already paused', paused: true };
    return { selectable: true };
  };
  const toggle = (day: string) => setSelected((list) => (list.includes(day) ? list.filter((d) => d !== day) : [...list, day].sort()));

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (selected.length === 0) {
      setError('Choose at least one day on the calendar.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const result = await api.pauseCustomer(customer.id, selected, reason.trim() || null);
      toast(
        `${result.dates.length} day(s) paused. ` +
          result.packages.map((p) => `${p.package.replace(/ \(.*\)$/, '')}: ends ${formatDay(p.newEndDate, true)}`).join('; ') + '.',
      );
      onDone();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'The delivery could not be paused.');
      setBusy(false);
    }
  };

  return (
    <Modal
      title={`Pause deliveries for ${customer.customerName}`}
      onClose={onClose}
      wide
      footer={
        <>
          <button className="btn btn-secondary" onClick={onClose} disabled={busy}>
            Cancel
          </button>
          <button className="btn btn-primary" type="submit" form="pause-form" disabled={busy || selected.length === 0 || checking || !!(current && preview.error)}>
            {busy ? 'Pausing…' : selected.length <= 1 ? 'Pause this day' : `Pause ${selected.length} days`}
          </button>
        </>
      }
    >
      <form id="pause-form" onSubmit={submit}>
        <ul className="confirm-list">
          <li>Click the days to pause (click again to clear). Only packages whose frequency can pause are paused; others are still delivered.</li>
          <li>Every paused day moves the package's end date to the next delivery day, so no meal is lost. If the new end date is a day paused for everyone, that day is skipped too.</li>
          <li>Sundays are never delivery days. A pause cannot be undone.</li>
        </ul>
        <div className="pause-layout">
          <PauseCalendar from={today} to={lastEnd} selected={selected} dayState={dayState} onToggle={toggle} />
          <div className="pause-preview">
            {selected.length === 0 ? (
              <p className="muted small">No days chosen yet.</p>
            ) : checking ? (
              <p className="muted small">Checking {selected.length} day(s)…</p>
            ) : preview.error ? (
              <p className="text-warning small">{preview.error}</p>
            ) : (
              preview.result && (
                <>
                  {preview.result.packages.map((p) => (
                    <div key={p.packageId} className="pause-preview-package">
                      <p className="small strong">{p.package.replace(/ \(.*\)$/, '')}</p>
                      <ul className="plain-list small">
                        {p.days.map((d) => (
                          <li key={d.date}>
                            {formatDay(d.date, true)} · {d.extendedEndDate ? 'end date moves 1 delivery day' : 'end date stays'}
                          </li>
                        ))}
                        {p.festiveDays.map((d) => (
                          <li key={`festive-${d}`}>
                            {formatDay(d, true)} · paused for everyone, end date moves 1 more day
                          </li>
                        ))}
                      </ul>
                      <p className="item-sub">
                        Ends {formatDay(p.previousEndDate, true)}
                        {p.newEndDate !== p.previousEndDate ? ` → ${formatDay(p.newEndDate, true)}` : ' (unchanged)'} · {p.pausedCount} own paused day(s)
                        in total
                      </p>
                    </div>
                  ))}
                  {preview.result.notPaused.length > 0 && (
                    <p className="item-sub">Still delivered (cannot pause): {preview.result.notPaused.map((n) => n.replace(/ \(.*\)$/, '')).join('; ')}</p>
                  )}
                </>
              )
            )}
          </div>
        </div>
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
