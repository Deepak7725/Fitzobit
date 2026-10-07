import { useState } from 'react';
import { api, type FilterOption, type MovePreview } from '../api';
import { Modal, useToast } from './feedback';

const REMOVE = 'remove';

/** Move customers to another delivery group (or remove them from their group): choose, preview exactly what changes, confirm. */
export default function ChangeGroupDialog({ customerIds, groups, currentGroupId, onClose, onDone }: {
  customerIds: number[];
  groups: FilterOption[];
  currentGroupId?: number | null;
  onClose: () => void;
  onDone: () => void;
}) {
  const toast = useToast();
  const [target, setTarget] = useState('');
  const [reason, setReason] = useState('');
  const [preview, setPreview] = useState<MovePreview | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const groupId = target === REMOVE || target === '' ? null : Number(target);
  const active = groups.filter((g) => g.isActive && g.id !== currentGroupId);
  const count = customerIds.length === 1 ? 'this customer' : `${customerIds.length.toLocaleString('en-IN')} customers`;

  const choose = (value: string) => {
    setTarget(value);
    setPreview(null);
    setError(null);
  };

  const loadPreview = async () => {
    setBusy(true);
    setError(null);
    try {
      setPreview(await api.previewCustomerGroup(customerIds, groupId));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Preview failed.');
    } finally {
      setBusy(false);
    }
  };

  const apply = async () => {
    setBusy(true);
    setError(null);
    try {
      const result = await api.changeCustomerGroup(customerIds, groupId, reason.trim() || null);
      toast(
        groupId === null
          ? `${result.changed.toLocaleString('en-IN')} customer(s) removed from their group.`
          : `${result.changed.toLocaleString('en-IN')} customer(s) moved to ${preview?.targetName ?? 'the group'}.`,
      );
      onDone();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The change failed.');
      setBusy(false);
    }
  };

  return (
    <Modal
      title="Change delivery group"
      onClose={onClose}
      footer={
        <>
          <button className="btn btn-secondary" onClick={onClose} disabled={busy}>
            Cancel
          </button>
          {preview ? (
            <button className="btn btn-primary" onClick={apply} disabled={busy || preview.willMove === 0} autoFocus>
              {busy ? 'Saving…' : 'Confirm change'}
            </button>
          ) : (
            <button className="btn btn-primary" onClick={loadPreview} disabled={busy || target === ''}>
              {busy ? 'Checking…' : 'Preview change'}
            </button>
          )}
        </>
      }
    >
      <p className="muted" style={{ marginBottom: 14 }}>
        Choose where {count} should be delivered from. Nothing changes until you confirm the preview.
      </p>
      <label className="field">
        New delivery group
        <select className="select select-full" value={target} onChange={(e) => choose(e.target.value)} autoFocus>
          <option value="">Choose a group…</option>
          {active.map((g) => (
            <option key={g.id} value={g.id}>
              {g.name}
            </option>
          ))}
          <option value={REMOVE}>— Remove from delivery group —</option>
        </select>
      </label>
      <label className="field">
        Reason (recorded in the history)
        <input className="input" value={reason} maxLength={300} onChange={(e) => setReason(e.target.value)} placeholder="e.g. Customer moved to RS Puram" />
      </label>
      {preview && (
        <div className="info-box">
          <h4>What will change</h4>
          <ul>
            {preview.messages.map((m) => (
              <li key={m}>{m}</li>
            ))}
          </ul>
        </div>
      )}
      {error && (
        <div className="auth-error" role="alert" style={{ marginTop: 12 }}>
          {error}
        </div>
      )}
    </Modal>
  );
}
