import { useEffect, useState } from 'react';
import { CalendarOff, Trash2 } from 'lucide-react';
import { api, type FestivePause, type FestivePreview } from '../api';
import { useApi, useDebounced } from '../hooks';
import { formatDateTime, formatDay, todayIso } from '../format';
import { ConfirmDialog, Modal, useToast } from './feedback';
import { ErrorPanel, Loading } from './ui';
import PauseCalendar, { type DayState } from './PauseCalendar';

/** "yyyy-mm-dd" plus n days, in UTC. */
function addDays(day: string, n: number): string {
  const [y, m, d] = day.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d + n)).toISOString().slice(0, 10);
}

/**
 * Pause for everyone (Admins): festive or emergency days. Every package running that day — any frequency — of Active and Paused
 * customers is paused; its end date always moves and the day is counted as "for everyone", not as the customer's own. Upcoming ones can be removed.
 */
/** Pause for everyone: Managers choose and remove days; Admins (readOnly) only see the list. */
export default function FestivePauseDialog({ onClose, onDone, readOnly = false }: { onClose: () => void; onDone: () => void; readOnly?: boolean }) {
  const toast = useToast();
  const today = todayIso();
  const list = useApi(api.festivePauses);
  const [selected, setSelected] = useState<string[]>([]);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [removing, setRemoving] = useState<FestivePause | null>(null);
  const [preview, setPreview] = useState<{ key: string; result: FestivePreview | null; error: string | null }>({ key: '', result: null, error: null });

  const key = useDebounced(selected.join(','), 300);
  useEffect(() => {
    if (!key) return;
    let cancelled = false;
    api
      .previewFestivePause(key.split(','), null)
      .then((result) => !cancelled && setPreview({ key, result, error: null }))
      .catch((e: unknown) => !cancelled && setPreview({ key, result: null, error: e instanceof Error ? e.message : 'Could not check these days.' }));
    return () => {
      cancelled = true;
    };
  }, [key]);
  const current = selected.length > 0 && preview.key === selected.join(',');
  const checking = selected.length > 0 && !current;

  const festiveDays = new Set((list.data ?? []).map((f) => f.date));
  const dayState = (day: string): DayState => {
    if (new Date(`${day}T00:00:00Z`).getUTCDay() === 0) return { selectable: false, note: 'Sunday: no delivery' };
    if (day < today) return { selectable: false, note: 'Past day' };
    if (festiveDays.has(day)) return { selectable: false, note: 'Already paused for everyone', paused: true };
    return { selectable: true };
  };
  const toggle = (day: string) => setSelected((s) => (s.includes(day) ? s.filter((d) => d !== day) : [...s, day].sort()));

  const save = async () => {
    setBusy(true);
    setError(null);
    try {
      const result = await api.createFestivePause(selected, reason.trim() || null);
      toast(`Paused for everyone on ${result.dates.map((d) => formatDay(d)).join(', ')}: ${result.customers} customer(s), ${result.packages} package(s).`);
      setSelected([]);
      setReason('');
      list.reload();
      onDone();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The days could not be paused.');
    } finally {
      setBusy(false);
    }
  };

  const remove = async (f: FestivePause) => {
    setBusy(true);
    try {
      await api.removeFestivePause(f.id);
      toast(`Pause for everyone on ${formatDay(f.date)} removed; end dates moved back.`);
      list.reload();
      onDone();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'The pause could not be removed.', 'error');
    } finally {
      setBusy(false);
      setRemoving(null);
    }
  };

  const upcoming = (list.data ?? []).filter((f) => f.date >= today);
  const past = (list.data ?? []).filter((f) => f.date < today);

  return (
    <Modal
      title={readOnly ? 'Paused for everyone (festive / emergency)' : 'Pause for everyone (festive / emergency)'}
      onClose={() => !busy && onClose()}
      wide={!readOnly}
      footer={
        <>
          <button className="btn btn-secondary" onClick={onClose} disabled={busy}>
            Close
          </button>
          {!readOnly && (
            <button className="btn btn-primary" onClick={() => void save()} disabled={busy || selected.length === 0 || checking || !!(current && preview.error)}>
              {busy ? 'Pausing…' : selected.length <= 1 ? 'Pause this day for everyone' : `Pause ${selected.length} days for everyone`}
            </button>
          )}
        </>
      }
    >
      {readOnly && <p className="muted small">Only a Manager account can pause days for everyone or remove them.</p>}
      {!readOnly && (
        <>
      <ul className="confirm-list">
        <li>Every package running on the chosen days is paused — Monthly, Weekly and Trail — for Active and Paused customers.</li>
        <li>Each end date moves one delivery day per paused day, so nobody loses a meal. These days are counted as “for everyone”, not as the customer's own pauses.</li>
        <li>Packages added or renewed later over these days skip them too. You can remove a day before it comes.</li>
      </ul>
      <div className="pause-layout">
        <PauseCalendar from={today} to={addDays(today, 180)} selected={selected} dayState={dayState} onToggle={toggle} />
        <div className="pause-preview">
          {selected.length === 0 ? (
            <p className="muted small">Choose the days on the calendar.</p>
          ) : checking ? (
            <p className="muted small">Checking {selected.length} day(s)…</p>
          ) : preview.error ? (
            <p className="text-warning small">{preview.error}</p>
          ) : (
            preview.result && (
              <>
                <p className="small strong">
                  {preview.result.customers} customer(s) · {preview.result.packages} package(s)
                </p>
                <ul className="plain-list small festive-preview-list">
                  {preview.result.items.map((i) => (
                    <li key={`${i.customerId}-${i.package}`}>
                      <strong>{i.customerCode}</strong> {i.customerName} · {i.package.replace(/ \(.*\)$/, '')}
                      <div className="item-sub">
                        Ends {formatDay(i.previousEndDate)} → {formatDay(i.newEndDate)}
                      </div>
                    </li>
                  ))}
                </ul>
                {preview.result.packages === 0 && <p className="muted small">No package runs on these days.</p>}
              </>
            )
          )}
        </div>
      </div>
      <label className="field">
        Reason (shown on each customer's paused day)
        <input className="input" value={reason} maxLength={300} onChange={(e) => setReason(e.target.value)} placeholder="e.g. Diwali" />
      </label>
      {error && (
        <div className="auth-error" role="alert">
          {error}
        </div>
      )}
        </>
      )}

      <h4 className="list-heading drawer-section">Paused for everyone</h4>
      {list.error && <ErrorPanel message={list.error} onRetry={list.reload} />}
      {list.loading && !list.data && <Loading />}
      {list.data && list.data.length === 0 && <p className="muted small">No days paused for everyone yet.</p>}
      {[...upcoming].reverse().concat(past).map((f) => (
        <div key={f.id} className="festive-row">
          <CalendarOff size={16} />
          <div className="festive-row-text">
            <strong>{formatDay(f.date, true)}</strong>
            {f.reason ? ` · ${f.reason}` : ''}
            <div className="item-sub">
              {f.customers} customer(s), {f.packages} package(s) · set {formatDateTime(f.createdAt)}
              {f.createdBy ? ` by ${f.createdBy}` : ''}
            </div>
          </div>
          {f.canRemove && !readOnly && (
            <button className="btn btn-secondary btn-sm" onClick={() => setRemoving(f)} disabled={busy}>
              <Trash2 size={14} /> Remove
            </button>
          )}
        </div>
      ))}

      {removing && (
        <ConfirmDialog
          danger
          title={`Remove the pause for everyone on ${formatDay(removing.date, true)}?`}
          messages={[
            `${removing.customers} customer(s) will get their delivery on that day again.`,
            `${removing.packages} package end date(s) move back one delivery day.`,
          ]}
          confirmLabel="Remove pause"
          busy={busy}
          onConfirm={() => void remove(removing)}
          onCancel={() => setRemoving(null)}
        />
      )}
    </Modal>
  );
}
