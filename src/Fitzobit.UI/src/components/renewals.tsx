import { useState } from 'react';
import { Link } from 'react-router-dom';
import { MessageCircle } from 'lucide-react';
import { api, type RenewalDue, type RenewalList } from '../api';
import { formatDateTime, formatDay } from '../format';
import { EmptyState, ErrorPanel, Loading } from './ui';
import { Modal, useToast } from './feedback';

/**
 * Opens WhatsApp (app or WhatsApp Web) with the customer's chat and the reminder text filled in, and records that a reminder was
 * sent. It is a real link, so the browser opens the new tab directly (no pop-up blocker); recording happens alongside.
 */
export function SendReminderButton({ item, onRecorded, small }: { item: RenewalDue; onRecorded: () => void; small?: boolean }) {
  const toast = useToast();
  const [busy, setBusy] = useState(false);
  if (!item.whatsAppUrl)
    return (
      <span className="text-warning small" title="WhatsApp needs a 10-digit mobile number">
        No mobile number for WhatsApp
      </span>
    );
  const record = async () => {
    setBusy(true);
    try {
      await api.recordRenewalReminder(item.packageId);
      onRecorded();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'The reminder could not be recorded.', 'error');
    } finally {
      setBusy(false);
    }
  };
  return (
    <a
      href={item.whatsAppUrl}
      target="_blank"
      rel="noopener noreferrer"
      className={`btn btn-primary ${small ? 'btn-sm' : ''}`}
      onClick={() => void record()}
      aria-disabled={busy || undefined}
      title={item.message}
    >
      <MessageCircle size={14} /> {item.remindedAt ? 'Send again' : 'Send reminder'}
    </a>
  );
}

/** "Reminded 26 Oct 2026, 10:15 am by Siva Sankari" */
export function RemindedNote({ item }: { item: RenewalDue }) {
  if (!item.remindedAt) return <span className="muted small">Not reminded yet</span>;
  return (
    <span className="small text-success">
      Reminded {formatDateTime(item.remindedAt)}
      {item.remindedBy ? ` by ${item.remindedBy}` : ''}
    </span>
  );
}

/** The dashboard's list of renewals due: customer, package, end date, days left, reminder status and the Send button. */
export function RenewalsDialog({ list, loading, error, onReload, onClose }: {
  list: RenewalList | null;
  loading: boolean;
  error: string | null;
  onReload: () => void;
  onClose: () => void;
}) {
  return (
    <Modal title={`Renewals due${list ? ` (${list.items.length})` : ''}`} onClose={onClose} wide>
      {error && <ErrorPanel message={error} onRetry={onReload} />}
      {loading && !list && <Loading />}
      {list && (
        <>
          <p className="muted small">
            Packages with {list.daysBefore} or fewer delivery days left (paused days not counted), for customers who have not renewed yet. “Send
            reminder” opens WhatsApp with the message ready; press Send there. The days and the message are set in Settings → General.
          </p>
          {list.items.length === 0 ? (
            <EmptyState icon={<MessageCircle size={22} />} title="No renewals due" message="Nobody's package is ending soon." />
          ) : (
            <div className="table-wrap">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Customer</th>
                    <th>Package</th>
                    <th>Ends</th>
                    <th className="num">Days left</th>
                    <th>Reminder</th>
                    <th className="actions-cell" aria-label="Actions" />
                  </tr>
                </thead>
                <tbody>
                  {list.items.map((item) => (
                    <tr key={item.packageId}>
                      <td>
                        <Link to={`/data?q=${encodeURIComponent(item.customerCode)}`} className="link strong" onClick={onClose}>
                          {item.customerCode}
                        </Link>
                        <div className="item-sub">
                          {item.customerName} · {item.phoneNumber}
                          {item.status !== 'Active' ? ` · ${item.status}` : ''}
                        </div>
                      </td>
                      <td>{item.package}</td>
                      <td className="nowrap">{formatDay(item.endDate, true)}</td>
                      <td className="num">{item.daysLeft}</td>
                      <td>
                        <RemindedNote item={item} />
                      </td>
                      <td className="actions-cell">
                        <SendReminderButton item={item} onRecorded={onReload} small />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
    </Modal>
  );
}
