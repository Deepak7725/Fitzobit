import { useEffect, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { CalendarCheck2, CheckCircle2, Info, MapPinned, MessageSquareText, Plus, UserPlus } from 'lucide-react';
import { api, type CreateCustomerResult, type DictionaryOptions, type SchedulePreview } from '../api';
import { useDictionaries } from '../hooks';
import { formatDay, formatTime } from '../format';
import { Card, ErrorPanel, Loading, PageHeader } from '../components/ui';
import { useToast } from '../components/feedback';
import {
  CustomerFormFields,
  EMPTY_PACKAGE,
  Field,
  PackageFields,
  PackagesTitle,
  ReadOnlyValue,
  toCustomerRequest,
  toPackageRequest,
  useSchedulePreview,
  validate,
  validatePackage,
  type CustomerFormValues,
  type PackageFormValues,
} from '../components/CustomerForm';

const EMPTY: CustomerFormValues = { customerName: '', phoneNumber: '', alternatePhoneNumber: '', address: '', pincode: '', deliveryTime: '', statusId: '' };
const MAX_NOTE = 2000;

interface PackageDraft {
  key: number;
  values: PackageFormValues;
}

export default function AddCustomerPage() {
  const toast = useToast();
  const dictionaries = useDictionaries();
  const [values, setValues] = useState<CustomerFormValues>(EMPTY);
  const [packages, setPackages] = useState<PackageDraft[]>([{ key: 1, values: EMPTY_PACKAGE }]);
  const [previews, setPreviews] = useState<Record<number, { preview: SchedulePreview | null; error: string | null }>>({});
  const [note, setNote] = useState('');
  const [touched, setTouched] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [created, setCreated] = useState<{ result: CreateCustomerResult; name: string } | null>(null);

  // New customers start as Active (the status the application relies on), but any active status can be chosen.
  const activeStatusId = dictionaries.data?.customerStatus.find((s) => s.code === 'ACTIVE')?.id;
  useEffect(() => {
    if (activeStatusId && !values.statusId) setValues((v) => ({ ...v, statusId: String(activeStatusId) }));
  }, [activeStatusId]); // eslint-disable-line react-hooks/exhaustive-deps

  const errors = touched ? validate(values, true) : {};
  const packageErrors = (p: PackageFormValues) => (touched ? validatePackage(p) : {});

  const addPackage = () => setPackages((list) => [...list, { key: Math.max(0, ...list.map((p) => p.key)) + 1, values: EMPTY_PACKAGE }]);
  const removePackage = (key: number) => setPackages((list) => list.filter((p) => p.key !== key));
  const updatePackage = (key: number, v: PackageFormValues) => setPackages((list) => list.map((p) => (p.key === key ? { ...p, values: v } : p)));

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setTouched(true);
    setError(null);
    if (packages.length === 0) {
      setError('Add at least one package.');
      return;
    }
    const invalid =
      Object.keys(validate(values, true)).length > 0 ||
      packages.some((p) => Object.keys(validatePackage(p.values)).length > 0 || previews[p.key]?.error);
    if (invalid || note.length > MAX_NOTE) return;
    setBusy(true);
    try {
      const result = await api.createCustomer({
        ...toCustomerRequest(values),
        packages: packages.map((p) => toPackageRequest(p.values)),
        note: note.trim() || null,
      });
      result.warnings.forEach((w) => toast(w, 'info'));
      toast(`Customer ${result.customerId} created.`);
      setCreated({ result, name: values.customerName.trim() });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'The customer could not be saved.');
    } finally {
      setBusy(false);
    }
  };

  const startAgain = () => {
    setCreated(null);
    setTouched(false);
    setError(null);
    setNote('');
    setPackages([{ key: 1, values: EMPTY_PACKAGE }]);
    setPreviews({});
    setValues({ ...EMPTY, statusId: activeStatusId ? String(activeStatusId) : '' });
  };

  if (created)
    return (
      <>
        <PageHeader title="Add Customer" />
        <Card className="created-card">
          <div className="save-hero success">
            <CheckCircle2 size={40} />
            <h2>Customer {created.result.customerId} created</h2>
            <p className="muted">
              {created.name} · {created.result.packages.length} package{created.result.packages.length === 1 ? '' : 's'}
            </p>
            <ul className="plain-list created-packages">
              {created.result.packages.map((p, i) => (
                <li key={p.id}>
                  Package {i + 1}: {formatDay(p.startDate, true)} – {formatDay(p.endDate, true)}
                </li>
              ))}
            </ul>
          </div>
          <div className="button-row centered">
            <Link to={`/data?q=${encodeURIComponent(created.result.customerId)}`} className="btn btn-secondary">
              View in customer list
            </Link>
            <button className="btn btn-primary" onClick={startAgain}>
              <UserPlus size={16} /> Add another customer
            </button>
          </div>
        </Card>
      </>
    );

  return (
    <>
      <PageHeader
        title="Add Customer"
        subtitle="Create a customer, their preferred delivery time and one or more meal packages. The Customer ID and end dates are set automatically."
      />
      {dictionaries.error && <ErrorPanel message={dictionaries.error} onRetry={dictionaries.reload} />}
      {dictionaries.loading && !dictionaries.data && <Loading label="Loading form…" />}

      {dictionaries.data && (
        <form className="grid-main-side" onSubmit={submit} noValidate>
          <Card>
            <CustomerFormFields values={values} onChange={setValues} errors={errors} dictionaries={dictionaries.data} customerId="Generated when you save" />

            <section className="form-section">
              <h3 className="form-section-title">
                <PackagesTitle count={packages.length} />
              </h3>
              <p className="field-hint">A customer can have several packages running at the same time, e.g. lunch monthly and breakfast weekly.</p>
              {packages.map((p, i) => (
                <PackageEditor
                  key={p.key}
                  index={i}
                  draft={p}
                  dictionaries={dictionaries.data!}
                  errors={packageErrors(p.values)}
                  onChange={(v) => updatePackage(p.key, v)}
                  onRemove={packages.length > 1 ? () => removePackage(p.key) : undefined}
                  onPreview={(preview, previewError) =>
                    setPreviews((all) =>
                      all[p.key]?.preview === preview && all[p.key]?.error === previewError ? all : { ...all, [p.key]: { preview, error: previewError } },
                    )
                  }
                />
              ))}
              <button type="button" className="btn btn-secondary btn-sm" onClick={addPackage}>
                <Plus size={14} /> Add another package
              </button>
            </section>

            <section className="form-section">
              <h3 className="form-section-title">
                <MessageSquareText size={15} /> Note
              </h3>
              <Field label="First note (optional)" hint={`Saved with today’s date and your name. More notes can be added later. ${note.length}/${MAX_NOTE}`}
                error={note.length > MAX_NOTE ? `At most ${MAX_NOTE} characters.` : undefined} span htmlFor="note">
                <textarea id="note" className="input textarea" rows={3} value={note} maxLength={MAX_NOTE}
                  placeholder="e.g. Gate code 1234. Call before delivery." onChange={(e) => setNote(e.target.value)} />
              </Field>
            </section>

            {error && (
              <div className="auth-error" role="alert">
                {error}
              </div>
            )}
            <div className="form-actions">
              <Link to="/data" className="btn btn-secondary">
                Cancel
              </Link>
              <button className="btn btn-primary" disabled={busy}>
                {busy ? (
                  <>
                    <span className="spinner spinner-light" aria-hidden /> Saving…
                  </>
                ) : (
                  <>
                    <UserPlus size={16} /> Save customer
                  </>
                )}
              </button>
            </div>
          </Card>

          <div className="stack">
            <Card title={<><CalendarCheck2 size={18} /> Summary</>}>
              <dl className="facts facts-wide">
                <dt>Delivery time</dt>
                <dd>{values.deliveryTime ? formatTime(values.deliveryTime) : '—'}</dd>
                {packages.map((p, i) => {
                  const preview = previews[p.key]?.preview;
                  const name = (list: { id: number; displayName: string }[], id: string) => list.find((o) => String(o.id) === id)?.displayName;
                  const meal = name(dictionaries.data!.mealType, p.values.mealTypeId);
                  const frequency = name(dictionaries.data!.frequency, p.values.frequencyId);
                  return (
                    <PackageSummaryRow key={p.key} index={i} label={[meal && `${meal} ×${p.values.quantity || 1}`, frequency].filter(Boolean).join(' · ')}
                      start={p.values.startDate} end={preview?.endDate ?? null} days={preview?.deliveryDays ?? null} />
                  );
                })}
              </dl>
              <p className="footnote">
                <Info size={12} /> Deliveries run Monday to Saturday. Sundays are not counted. Only packages whose frequency can pause (e.g. Monthly)
                can pause days; the first 3 paused days move the end date to the next delivery day.
              </p>
            </Card>
            <Card title={<><MapPinned size={18} /> Map location</>}>
              <div className="form-grid">
                <Field label="Latitude">
                  <ReadOnlyValue muted>Not yet available</ReadOnlyValue>
                </Field>
                <Field label="Longitude">
                  <ReadOnlyValue muted>Not yet available</ReadOnlyValue>
                </Field>
              </div>
              <p className="footnote">Looked up automatically from the address after saving.</p>
            </Card>
          </div>
        </form>
      )}
    </>
  );
}

function PackageSummaryRow({ index, label, start, end, days }: { index: number; label: string; start: string; end: string | null; days: number | null }) {
  return (
    <>
      <dt>Package {index + 1}</dt>
      <dd>
        {label || '—'}
        <div className="item-sub">
          {start ? formatDay(start, true) : 'No start date'}
          {end && <> – {formatDay(end, true)}</>}
          {days != null && <> · {days} delivery days</>}
        </div>
      </dd>
    </>
  );
}

/** One package on the Add Customer form, with its own end-date preview (reported to the summary). */
function PackageEditor({ index, draft, dictionaries, errors, onChange, onRemove, onPreview }: {
  index: number;
  draft: PackageDraft;
  dictionaries: DictionaryOptions;
  errors: ReturnType<typeof validatePackage>;
  onChange: (v: PackageFormValues) => void;
  onRemove?: () => void;
  onPreview: (preview: SchedulePreview | null, error: string | null) => void;
}) {
  const schedule = useSchedulePreview(draft.values.frequencyId, draft.values.startDate);
  useEffect(() => onPreview(schedule.preview, schedule.error), [schedule.preview, schedule.error]); // eslint-disable-line react-hooks/exhaustive-deps
  return (
    <div className="package-card">
      <PackageFields
        idPrefix={`package-${draft.key}`}
        title={`Package ${index + 1}`}
        values={draft.values}
        onChange={onChange}
        errors={errors}
        dictionaries={dictionaries}
        preview={schedule}
        onRemove={onRemove}
      />
    </div>
  );
}
