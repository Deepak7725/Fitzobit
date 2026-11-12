import { useState, type FormEvent } from 'react';
import { CheckCircle2, LocateFixed, MapPinned, Pencil } from 'lucide-react';
import { api } from '../../api';
import { useApi } from '../../hooks';
import { useAuth } from '../../auth';
import { formatDateTime } from '../../format';
import { Badge, Card, ErrorPanel, Loading } from '../../components/ui';
import { Modal, useToast } from '../../components/feedback';

/** Settings → General → Delivery area: the one city Fitzobit serves. Customer locations are looked up inside it. */
export default function DeliveryAreaCard() {
  const { isManager } = useAuth();
  const toast = useToast();
  const area = useApi(api.deliveryArea);
  const [editing, setEditing] = useState(false);
  const [queuing, setQueuing] = useState(false);
  const a = area.data;

  const relocate = async () => {
    setQueuing(true);
    try {
      const result = await api.locateCustomers(null);
      toast(`${result.queued.toLocaleString('en-IN')} customer(s) without a location were queued for a new lookup.`, 'info');
    } catch (e) {
      toast(e instanceof Error ? e.message : 'The lookups could not be queued.', 'error');
    } finally {
      setQueuing(false);
    }
  };

  return (
    <Card
      title={<><MapPinned size={18} /> Delivery area</>}
      subtitle="The city Fitzobit delivers in. Customer addresses are located only inside it, so a street name in another city is never used."
      actions={
        isManager && a && (
          <button className="btn btn-secondary btn-sm" onClick={() => setEditing(true)}>
            <Pencil size={14} /> {a.isConfigured ? 'Change' : 'Set city'}
          </button>
        )
      }
    >
      {area.error && <ErrorPanel message={area.error} onRetry={area.reload} />}
      {area.loading && !a && <Loading />}
      {a && (
        <>
          <dl className="facts facts-wide">
            <dt>Service city</dt>
            <dd>
              {a.isConfigured ? (
                <strong>
                  {a.city}
                  {a.state ? `, ${a.state}` : ''}, {a.country}
                </strong>
              ) : (
                <Badge tone="warning">Not set</Badge>
              )}
              {a.updatedAt && (
                <div className="item-sub">
                  Set {formatDateTime(a.updatedAt)}
                  {a.updatedBy ? ` by ${a.updatedBy}` : ''}
                </div>
              )}
            </dd>
            <dt>City area</dt>
            <dd>
              {a.areaFound ? (
                <Badge tone="success" icon={<CheckCircle2 size={13} />}>
                  Found
                </Badge>
              ) : (
                <Badge tone="neutral">Unknown</Badge>
              )}
              {a.areaMessage && <div className="item-sub">{a.areaMessage}</div>}
            </dd>
            <dt>Geocoding</dt>
            <dd>
              {a.geocodingConfigured ? <Badge tone="info">{a.geocodingProvider}</Badge> : <Badge tone="warning">Not configured</Badge>}
              <div className="item-sub">
                {a.geocodingConfigured
                  ? 'Locations are looked up in the background after a customer is saved; nothing is guessed when an address is not found.'
                  : 'Set Geocoding__Provider on the server (Nominatim or Google).'}
              </div>
            </dd>
          </dl>
          {isManager && a.isConfigured && (
            <p className="footnote">
              <button className="btn btn-secondary btn-sm" onClick={relocate} disabled={queuing}>
                <LocateFixed size={14} /> {queuing ? 'Queuing…' : 'Retry all unavailable locations'}
              </button>
            </p>
          )}
        </>
      )}
      {editing && a && (
        <AreaDialog
          city={a.city ?? ''}
          state={a.state ?? ''}
          onClose={() => setEditing(false)}
          onSaved={(message) => {
            setEditing(false);
            toast(message, 'info');
            area.reload();
          }}
        />
      )}
    </Card>
  );
}

function AreaDialog({ city, state, onClose, onSaved }: { city: string; state: string; onClose: () => void; onSaved: (message: string) => void }) {
  const [cityValue, setCity] = useState(city);
  const [stateValue, setState] = useState(state);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const saved = await api.saveDeliveryArea(cityValue, stateValue.trim() || null);
      onSaved(saved.areaMessage ?? 'Delivery area saved.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'The delivery area could not be saved.');
      setBusy(false);
    }
  };

  return (
    <Modal
      title="Delivery area"
      onClose={onClose}
      footer={
        <>
          <button className="btn btn-secondary" onClick={onClose} disabled={busy}>
            Cancel
          </button>
          <button className="btn btn-primary" type="submit" form="area-form" disabled={busy || !cityValue.trim()}>
            {busy ? 'Looking up the city…' : 'Save'}
          </button>
        </>
      }
    >
      <form id="area-form" onSubmit={submit}>
        <label className="field">
          City
          <input className="input" value={cityValue} onChange={(e) => setCity(e.target.value)} maxLength={100} autoFocus placeholder="e.g. the city you deliver in" />
        </label>
        <label className="field">
          State (optional)
          <input className="input" value={stateValue} onChange={(e) => setState(e.target.value)} maxLength={100} />
          <small className="field-hint">
            The city's area is looked up once when you save. Changing the city does not move existing customers; use “Retry all unavailable locations” or
            retry individual customers.
          </small>
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
