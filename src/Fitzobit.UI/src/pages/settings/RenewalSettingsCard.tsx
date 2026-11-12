import { useState, type FormEvent } from 'react';
import { MessageCircle, Pencil } from 'lucide-react';
import { api } from '../../api';
import { useApi } from '../../hooks';
import { useAuth } from '../../auth';
import { formatDateTime } from '../../format';
import { Card, ErrorPanel, Loading } from '../../components/ui';
import { Modal, useToast } from '../../components/feedback';

const SAMPLE = { name: 'Anitha Raman', package: 'Veg Medium ×1 · Monthly', endDate: 'Tue, 03 Nov 2026', daysLeft: '3' };

/** The message with the blanks filled in from a sample customer (the server does the same for real customers). */
function preview(message: string): string {
  return message
    .replace(/\{name\}/gi, SAMPLE.name)
    .replace(/\{package\}/gi, SAMPLE.package)
    .replace(/\{endDate\}/gi, SAMPLE.endDate)
    .replace(/\{daysLeft\}/gi, SAMPLE.daysLeft);
}

/** Settings → General → Renewal reminders: days before the end and the WhatsApp message (Manager accounts change them). */
export default function RenewalSettingsCard() {
  const { isManager } = useAuth();
  const settings = useApi(api.renewalSettings);
  const [editing, setEditing] = useState(false);
  const s = settings.data;

  return (
    <Card
      title={<><MessageCircle size={18} /> Renewal reminders</>}
      subtitle="When a package shows as “Renewal due” and the WhatsApp message sent with “Send reminder”."
      actions={
        isManager && s && (
          <button className="btn btn-secondary btn-sm" onClick={() => setEditing(true)}>
            <Pencil size={14} /> Change
          </button>
        )
      }
    >
      {settings.error && <ErrorPanel message={settings.error} onRetry={settings.reload} />}
      {settings.loading && !s && <Loading />}
      {s && (
        <dl className="facts facts-wide">
          <dt>Renewal due</dt>
          <dd>
            <strong>{s.daysBefore}</strong> or fewer delivery days left
            <div className="item-sub">Paused days are not counted; customers who already have a following package are left out.</div>
          </dd>
          <dt>Message</dt>
          <dd>
            <span className="pre-line">{s.message}</span>
            <div className="item-sub">Example: {preview(s.message)}</div>
            {s.updatedAt && (
              <div className="item-sub">
                Changed {formatDateTime(s.updatedAt)}
                {s.updatedBy ? ` by ${s.updatedBy}` : ''}
              </div>
            )}
          </dd>
        </dl>
      )}
      {editing && s && (
        <RenewalSettingsDialog
          daysBefore={s.daysBefore}
          message={s.message}
          defaultMessage={s.defaultMessage}
          onClose={() => setEditing(false)}
          onSaved={() => {
            setEditing(false);
            settings.reload();
          }}
        />
      )}
    </Card>
  );
}

function RenewalSettingsDialog({ daysBefore, message, defaultMessage, onClose, onSaved }: {
  daysBefore: number;
  message: string;
  defaultMessage: string;
  onClose: () => void;
  onSaved: () => void;
}) {
  const toast = useToast();
  const [days, setDays] = useState(String(daysBefore));
  const [text, setText] = useState(message);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.saveRenewalSettings(Number(days), text);
      toast('Renewal reminder settings saved.');
      onSaved();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'The settings could not be saved.');
      setBusy(false);
    }
  };

  return (
    <Modal
      title="Renewal reminders"
      onClose={onClose}
      footer={
        <>
          <button className="btn btn-secondary" onClick={onClose} disabled={busy}>
            Cancel
          </button>
          <button className="btn btn-primary" type="submit" form="renewal-form" disabled={busy || !text.trim() || !days}>
            {busy ? 'Saving…' : 'Save'}
          </button>
        </>
      }
    >
      <form id="renewal-form" onSubmit={submit}>
        <label className="field">
          Show “Renewal due” when this many delivery days are left (or fewer)
          <input className="input" type="number" min={1} max={30} value={days} onChange={(e) => setDays(e.target.value)} />
          <small className="field-hint">1–30 delivery days (Monday–Saturday). Paused days are not counted.</small>
        </label>
        <label className="field">
          WhatsApp message
          <textarea className="input textarea" rows={4} maxLength={1000} value={text} onChange={(e) => setText(e.target.value)} />
          <small className="field-hint">
            Filled in automatically: {'{name}'}, {'{package}'}, {'{endDate}'}, {'{daysLeft}'}.{' '}
            <button type="button" className="link-button" onClick={() => setText(defaultMessage)}>
              Use the default text
            </button>
          </small>
        </label>
        <div className="pause-preview">
          <p className="small strong">Example</p>
          <p className="small pre-line">{preview(text)}</p>
        </div>
        {error && (
          <div className="auth-error" role="alert">
            {error}
          </div>
        )}
      </form>
    </Modal>
  );
}
