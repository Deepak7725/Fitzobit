import { useState, type FormEvent } from 'react';
import { ArrowDown, ArrowUp, ListChecks, Pencil, Plus, Power, Trash2 } from 'lucide-react';
import { api, type DictionaryEntry, type DictionaryType } from '../../api';
import { useApi } from '../../hooks';
import { formatDateTime, formatNumber } from '../../format';
import { Badge, Card, EmptyState, ErrorPanel, Loading } from '../../components/ui';
import { ConfirmDialog, Modal, useToast } from '../../components/feedback';

const TYPES: { type: DictionaryType; label: string; singular: string; help: string }[] = [
  { type: 'Preference', label: 'Preferences', singular: 'preference', help: 'Food preference, e.g. Veg or With Egg.' },
  { type: 'MealType', label: 'Meal Types', singular: 'meal type', help: 'Box size or product, e.g. Regular, Medium, Large.' },
  {
    type: 'Frequency',
    label: 'Frequencies',
    singular: 'frequency',
    help: 'Package length in delivery days (Monday–Saturday; Sundays are never counted). Used to calculate end dates.',
  },
  {
    type: 'CustomerStatus',
    label: 'Customer Status',
    singular: 'status',
    help: 'Customer statuses. Active, Inactive and Paused are used by the application: they can be renamed but stay active.',
  },
];

/** Settings → Dropdown values (Admin): every value offered on the Add/Edit customer screens. */
export default function DictionariesTab() {
  const [type, setType] = useState<DictionaryType>('Preference');
  const meta = TYPES.find((t) => t.type === type)!;
  const entries = useApi((signal) => api.dictionaryEntries(type, signal), [type]);
  const toast = useToast();
  const [editing, setEditing] = useState<DictionaryEntry | 'new' | null>(null);
  const [toggling, setToggling] = useState<DictionaryEntry | null>(null);
  const [deleting, setDeleting] = useState<DictionaryEntry | null>(null);
  const [busy, setBusy] = useState(false);

  const list = entries.data ?? [];
  const isFrequency = type === 'Frequency';

  const run = async (action: () => Promise<unknown>, success: string) => {
    setBusy(true);
    try {
      await action();
      toast(success);
      entries.reload();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'The change could not be saved.', 'error');
    } finally {
      setBusy(false);
      setToggling(null);
      setDeleting(null);
    }
  };

  const move = (index: number, delta: number) => {
    const ids = list.map((e) => e.id);
    [ids[index], ids[index + delta]] = [ids[index + delta], ids[index]];
    void run(() => api.reorderDictionary(type, ids), 'Order saved.');
  };

  return (
    <Card
      title={<><ListChecks size={18} /> Dropdown values</>}
      subtitle="Values offered on the Add and Edit customer screens. Changes apply immediately. Values in use are deactivated, never deleted."
      actions={
        <button className="btn btn-primary btn-sm" onClick={() => setEditing('new')}>
          <Plus size={15} /> Add {meta.singular}
        </button>
      }
    >
      <div className="chips dictionary-types" role="tablist">
        {TYPES.map((t) => (
          <button key={t.type} role="tab" aria-selected={t.type === type} className={`chip ${t.type === type ? 'active' : ''}`} onClick={() => setType(t.type)}>
            {t.label}
          </button>
        ))}
      </div>
      <p className="muted small">{meta.help}</p>

      {entries.error && <ErrorPanel message={entries.error} onRetry={entries.reload} />}
      {entries.loading && !entries.data && <Loading label="Loading values…" />}
      {entries.data && list.length === 0 && <EmptyState title="No values yet" message={`Add the first ${meta.singular}.`} />}
      {entries.data && list.length > 0 && (
        <div className={`table-wrap ${entries.loading || busy ? 'is-loading' : ''}`}>
          <table className="table">
            <thead>
              <tr>
                <th className="col-order">Order</th>
                <th>Name</th>
                <th>Code</th>
                {isFrequency && <th className="num">Delivery days</th>}
                {isFrequency && <th>Can pause</th>}
                <th className="num" title="Packages using it (customers for a status)">In use</th>
                <th>Status</th>
                <th>Updated</th>
                <th className="actions-cell" aria-label="Actions" />
              </tr>
            </thead>
            <tbody>
              {list.map((e, i) => (
                <tr key={e.id} className={e.isActive ? '' : 'row-inactive'}>
                  <td className="col-order">
                    <button className="icon-btn icon-btn-xs" disabled={i === 0 || busy} onClick={() => move(i, -1)} aria-label={`Move ${e.displayName} up`}>
                      <ArrowUp size={14} />
                    </button>
                    <button
                      className="icon-btn icon-btn-xs"
                      disabled={i === list.length - 1 || busy}
                      onClick={() => move(i, 1)}
                      aria-label={`Move ${e.displayName} down`}
                    >
                      <ArrowDown size={14} />
                    </button>
                  </td>
                  <td className="item-name">{e.displayName}</td>
                  <td>
                    <code className="code">{e.code}</code>
                  </td>
                  {isFrequency && <td className="num">{e.numericValue ?? '—'}</td>}
                  {isFrequency && <td>{e.canPause ? <Badge tone="success">Yes</Badge> : <span className="muted">No</span>}</td>}
                  <td className="num">{formatNumber(e.customerCount)}</td>
                  <td>
                    {e.isActive ? <Badge tone="success">Active</Badge> : <Badge tone="neutral">Inactive</Badge>}{' '}
                    {e.isSystem && <Badge tone="info">System</Badge>}
                  </td>
                  <td>
                    <div className="small">{formatDateTime(e.updatedAt)}</div>
                    {e.updatedBy && <div className="item-sub">{e.updatedBy}</div>}
                  </td>
                  <td className="actions-cell">
                    <button className="icon-btn" title="Edit" aria-label={`Edit ${e.displayName}`} onClick={() => setEditing(e)}>
                      <Pencil size={15} />
                    </button>
                    {!e.isSystem && (
                      <button
                        className="icon-btn"
                        title={e.isActive ? 'Deactivate' : 'Activate'}
                        aria-label={`${e.isActive ? 'Deactivate' : 'Activate'} ${e.displayName}`}
                        onClick={() => setToggling(e)}
                      >
                        <Power size={15} />
                      </button>
                    )}
                    {!e.isSystem && e.customerCount === 0 && (
                      <button className="icon-btn danger" title="Delete" aria-label={`Delete ${e.displayName}`} onClick={() => setDeleting(e)}>
                        <Trash2 size={15} />
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {editing && (
        <EntryDialog
          type={type}
          singular={meta.singular}
          entry={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            entries.reload();
          }}
        />
      )}
      {toggling && (
        <ConfirmDialog
          title={`${toggling.isActive ? 'Deactivate' : 'Activate'} “${toggling.displayName}”?`}
          messages={
            toggling.isActive
              ? [
                  `It will no longer be offered when adding or editing customers.`,
                  toggling.customerCount > 0
                    ? `${formatNumber(toggling.customerCount)} ${type === 'CustomerStatus' ? 'customer(s)' : 'package(s)'} already have it and keep it until someone changes them.`
                    : 'No customer uses it.',
                ]
              : ['It will be offered again on the Add and Edit customer screens.']
          }
          confirmLabel={toggling.isActive ? 'Deactivate' : 'Activate'}
          busy={busy}
          onConfirm={() =>
            run(
              () => api.updateDictionaryEntry(toggling.id, { displayName: toggling.displayName, numericValue: toggling.numericValue, isActive: !toggling.isActive }),
              `${toggling.displayName} ${toggling.isActive ? 'deactivated' : 'activated'}.`,
            )
          }
          onCancel={() => setToggling(null)}
        />
      )}
      {deleting && (
        <ConfirmDialog
          danger
          title={`Delete “${deleting.displayName}”?`}
          messages={['No customer uses this value, so it can be removed. This cannot be undone.']}
          confirmLabel="Delete"
          busy={busy}
          onConfirm={() => run(() => api.deleteDictionaryEntry(deleting.id), `${deleting.displayName} deleted.`)}
          onCancel={() => setDeleting(null)}
        />
      )}
    </Card>
  );
}

function EntryDialog({ type, singular, entry, onClose, onSaved }: {
  type: DictionaryType;
  singular: string;
  entry: DictionaryEntry | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const toast = useToast();
  const [displayName, setDisplayName] = useState(entry?.displayName ?? '');
  const [days, setDays] = useState(entry?.numericValue != null ? String(entry.numericValue) : '');
  const [isActive, setIsActive] = useState(entry?.isActive ?? true);
  const [canPause, setCanPause] = useState(entry?.canPause ?? false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const isFrequency = type === 'Frequency';

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    const request = { displayName, numericValue: isFrequency && days ? Number(days) : null, canPause: isFrequency ? canPause : undefined, isActive };
    try {
      if (entry) await api.updateDictionaryEntry(entry.id, request);
      else await api.createDictionaryEntry(type, request);
      toast(entry ? `${displayName.trim()} saved.` : `${displayName.trim()} added.`);
      onSaved();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'The value could not be saved.');
      setBusy(false);
    }
  };

  return (
    <Modal
      title={entry ? `Edit ${singular}` : `Add ${singular}`}
      onClose={onClose}
      footer={
        <>
          <button className="btn btn-secondary" onClick={onClose} disabled={busy}>
            Cancel
          </button>
          <button className="btn btn-primary" type="submit" form="dictionary-form" disabled={busy || !displayName.trim()}>
            {busy ? 'Saving…' : entry ? 'Save' : 'Add'}
          </button>
        </>
      }
    >
      <form id="dictionary-form" onSubmit={submit}>
        <label className="field">
          Display name
          <input className="input" autoFocus value={displayName} maxLength={100} onChange={(e) => setDisplayName(e.target.value)} />
          <small className="field-hint">
            {entry ? `Code ${entry.code} stays the same when you rename it.` : 'Shown in the drop-downs. A stable code is created from it.'}
          </small>
        </label>
        {isFrequency && (
          <label className="field">
            Delivery days per package
            <input className="input" type="number" min={1} max={1000} value={days} onChange={(e) => setDays(e.target.value)} />
            <small className="field-hint">
              Monday–Saturday days in one package (Monthly 26, Weekly 6). Changing it affects end dates calculated from now on; existing end dates are kept.
            </small>
          </label>
        )}
        {isFrequency && (
          <label className="toggle toggle-field">
            <input type="checkbox" checked={canPause} onChange={(e) => setCanPause(e.target.checked)} />
            Can pause (packages of this frequency may pause delivery days; every paused day extends the end date)
          </label>
        )}
        {(!entry || !entry.isSystem) && (
          <label className="toggle toggle-field">
            <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
            Active (offered on the Add and Edit customer screens)
          </label>
        )}
        {error && (
          <div className="auth-error" role="alert">
            {error}
          </div>
        )}
      </form>
    </Modal>
  );
}
