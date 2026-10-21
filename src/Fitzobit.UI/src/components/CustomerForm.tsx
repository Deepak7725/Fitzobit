import { useEffect, useState, type ReactNode } from 'react';
import { CalendarDays, Clock, Info, Lock, MapPin, Trash2 } from 'lucide-react';
import { api, type DictionaryOption, type DictionaryOptions, type SavePackageRequest, type SaveCustomerRequest, type SchedulePreview } from '../api';
import { useDebounced } from '../hooks';
import { formatDay } from '../format';

/** Form state for the Add/Edit customer screens (ids are strings so selects stay controlled; "" = not chosen). */
export interface CustomerFormValues {
  customerName: string;
  phoneNumber: string;
  /** Optional; "" = none. */
  alternatePhoneNumber: string;
  address: string;
  pincode: string;
  /** Optional; "" = none. */
  googleMapUrl: string;
  /** "HH:mm" from the time input. */
  deliveryTime: string;
  statusId: string;
}

/** Form state of one package. */
export interface PackageFormValues {
  preferenceId: string;
  mealTypeId: string;
  quantity: string;
  frequencyId: string;
  startDate: string;
}

export const EMPTY_PACKAGE: PackageFormValues = { preferenceId: '', mealTypeId: '', quantity: '1', frequencyId: '', startDate: '' };

/** A value a package already has that is no longer offered (deactivated in Settings), kept selectable for it. */
export interface CurrentValue {
  id: number | null;
  name: string | null;
}

export function toCustomerRequest(v: CustomerFormValues): SaveCustomerRequest {
  return {
    customerName: v.customerName,
    phoneNumber: v.phoneNumber,
    alternatePhoneNumber: v.alternatePhoneNumber.trim() || null,
    address: v.address,
    pincode: v.pincode.trim() || null,
    googleMapUrl: v.googleMapUrl.trim() || null,
    preferredDeliveryTime: v.deliveryTime ? `${v.deliveryTime.slice(0, 5)}:00` : null,
    statusId: v.statusId ? Number(v.statusId) : null,
  };
}

export function toPackageRequest(p: PackageFormValues): SavePackageRequest {
  const id = (s: string) => (s ? Number(s) : null);
  return {
    preferenceId: id(p.preferenceId),
    mealTypeId: id(p.mealTypeId),
    quantity: p.quantity ? Number(p.quantity) : null,
    frequencyId: id(p.frequencyId),
    startDate: p.startDate || null,
  };
}

export type CustomerErrors = Partial<Record<keyof CustomerFormValues, string>>;
export type PackageErrors = Partial<Record<keyof PackageFormValues, string>>;

/** Field checks shown while typing. The server repeats every check (and the business rules) on save. */
export function validate(v: CustomerFormValues, pincodeRequired: boolean): CustomerErrors {
  const errors: CustomerErrors = {};
  const name = v.customerName.trim();
  if (name.length < 2 || !/\p{L}/u.test(name)) errors.customerName = 'Enter the customer’s name (at least 2 characters).';
  const digits = v.phoneNumber.replace(/\D/g, '');
  if (!v.phoneNumber.trim()) errors.phoneNumber = 'Phone number is required.';
  else if (/[^\d\s+\-.()]/.test(v.phoneNumber) || digits.length < 10 || digits.length > 14) errors.phoneNumber = 'Enter a 10-digit mobile number.';
  const alternate = v.alternatePhoneNumber.trim();
  const alternateDigits = alternate.replace(/\D/g, '');
  if (alternate && (/[^\d\s+\-.()]/.test(alternate) || alternateDigits.length < 10 || alternateDigits.length > 14))
    errors.alternatePhoneNumber = 'Enter a 10-digit mobile number, or leave it empty.';
  else if (alternate && digits.length >= 10 && alternateDigits.slice(-10) === digits.slice(-10))
    errors.alternatePhoneNumber = 'Must be different from the phone number.';
  if (!v.address.trim()) errors.address = 'Address is required.';
  if (v.pincode.trim() && !/^[1-9]\d{5}$/.test(v.pincode.trim())) errors.pincode = 'Pincode must be 6 digits.';
  else if (!v.pincode.trim() && pincodeRequired) errors.pincode = 'Pincode is required.';
  if (v.googleMapUrl.trim() && !isGoogleMapUrl(v.googleMapUrl.trim()))
    errors.googleMapUrl = 'Paste a Google Maps link, e.g. https://maps.app.goo.gl/… (or leave it empty).';
  if (!v.deliveryTime) errors.deliveryTime = 'Choose the preferred delivery time.';
  if (!v.statusId) errors.statusId = 'Choose a status.';
  return errors;
}

/** The same check as the server: google.TLD/maps…, maps.google.TLD/…, maps.app.goo.gl/…, goo.gl/maps/… */
export function isGoogleMapUrl(text: string): boolean {
  let url: URL;
  try {
    url = new URL(text);
  } catch {
    return false;
  }
  if (url.protocol !== 'https:' && url.protocol !== 'http:') return false;
  const host = url.hostname.toLowerCase();
  const maps = url.pathname.toLowerCase().startsWith('/maps');
  return (
    host === 'maps.app.goo.gl' ||
    (host === 'goo.gl' && maps) ||
    /^maps\.google\.[a-z]{2,3}(\.[a-z]{2})?$/.test(host) ||
    (/^(www\.)?google\.[a-z]{2,3}(\.[a-z]{2})?$/.test(host) && maps)
  );
}

export const MAX_QUANTITY = 20;

export function validatePackage(p: PackageFormValues): PackageErrors {
  const errors: PackageErrors = {};
  if (!p.preferenceId) errors.preferenceId = 'Choose a preference.';
  if (!p.mealTypeId) errors.mealTypeId = 'Choose a meal type.';
  const q = Number(p.quantity);
  if (!p.quantity || !Number.isInteger(q) || q < 1 || q > MAX_QUANTITY) errors.quantity = `Enter 1–${MAX_QUANTITY} boxes.`;
  if (!p.frequencyId) errors.frequencyId = 'Choose a frequency.';
  if (!p.startDate) errors.startDate = 'Choose the first delivery day.';
  return errors;
}

/**
 * The end date for a start date and frequency, calculated by the server in delivery days (Monday–Saturday). The browser
 * never does this arithmetic itself.
 */
export function useSchedulePreview(frequencyId: string, startDate: string) {
  const key = useDebounced(`${frequencyId}|${startDate}`, 250);
  const [state, setState] = useState<{ key: string; preview: SchedulePreview | null; error: string | null; loading: boolean }>({
    key: '',
    preview: null,
    error: null,
    loading: false,
  });

  useEffect(() => {
    const [f, s] = key.split('|');
    if (!f || !s) {
      setState({ key, preview: null, error: null, loading: false });
      return;
    }
    const controller = new AbortController();
    setState((prev) => ({ ...prev, key, loading: true, error: null }));
    api
      .schedule(Number(f), s, controller.signal)
      .then((preview) => setState({ key, preview, error: null, loading: false }))
      .catch((e: unknown) => {
        if (!controller.signal.aborted) setState({ key, preview: null, error: e instanceof Error ? e.message : 'Could not calculate the end date.', loading: false });
      });
    return () => controller.abort();
  }, [key]);

  const current = key === `${frequencyId}|${startDate}`;
  const complete = !!frequencyId && !!startDate;
  return { preview: current ? state.preview : null, error: current ? state.error : null, loading: complete && (!current || state.loading) };
}

export function Field({ label, error, hint, children, span, htmlFor }: {
  label: ReactNode;
  error?: string;
  hint?: ReactNode;
  children: ReactNode;
  span?: boolean;
  htmlFor?: string;
}) {
  return (
    <div className={`field ${span ? 'field-span' : ''} ${error ? 'has-error' : ''}`}>
      <label htmlFor={htmlFor}>{label}</label>
      {children}
      {error ? <small className="field-error">{error}</small> : hint && <small className="field-hint">{hint}</small>}
    </div>
  );
}

export function ReadOnlyValue({ children, muted }: { children: ReactNode; muted?: boolean }) {
  return (
    <div className={`readonly-value ${muted ? 'is-muted' : ''}`} aria-readonly="true">
      <Lock size={13} aria-hidden />
      <span>{children}</span>
    </div>
  );
}

function DictionarySelect({ id, value, options, current, placeholder, onChange, invalid, showDays }: {
  id: string;
  value: string;
  options: DictionaryOption[];
  current?: CurrentValue;
  placeholder: string;
  onChange: (value: string) => void;
  invalid?: boolean;
  showDays?: boolean;
}) {
  const currentMissing = current?.id != null && !options.some((o) => o.id === current.id);
  return (
    <select id={id} className="select select-full" value={value} onChange={(e) => onChange(e.target.value)} aria-invalid={invalid || undefined}>
      <option value="">{placeholder}</option>
      {currentMissing && <option value={current!.id!}>{current!.name} (no longer offered)</option>}
      {options.map((o) => (
        <option key={o.id} value={o.id}>
          {o.displayName}
          {o.numericValue != null && showDays ? ` · ${o.numericValue} delivery days` : ''}
        </option>
      ))}
    </select>
  );
}

/**
 * The customer's own fields (no packages): used by Add Customer and by Edit in the customer panel.
 */
export function CustomerFormFields({ values, onChange, errors, dictionaries, currentStatus, customerId, compact, extra }: {
  values: CustomerFormValues;
  onChange: (values: CustomerFormValues) => void;
  errors: CustomerErrors;
  dictionaries: DictionaryOptions;
  currentStatus?: CurrentValue;
  customerId: ReactNode;
  /** One column (the side panel). */
  compact?: boolean;
  /** Extra read-only rows at the end (e.g. coordinates). */
  extra?: ReactNode;
}) {
  const set = <K extends keyof CustomerFormValues>(key: K) => (value: CustomerFormValues[K]) => onChange({ ...values, [key]: value });

  return (
    <div className={`customer-form ${compact ? 'is-compact' : ''}`}>
      <section className="form-section">
        <h3 className="form-section-title">Customer</h3>
        <div className="form-grid">
          <Field label="Customer ID" hint="Assigned by the system; cannot be changed.">
            <ReadOnlyValue muted={typeof customerId !== 'string'}>{customerId}</ReadOnlyValue>
          </Field>
          <Field label="Customer name" error={errors.customerName} htmlFor="customerName">
            <input id="customerName" className="input" value={values.customerName} maxLength={150} autoComplete="off"
              onChange={(e) => set('customerName')(e.target.value)} aria-invalid={!!errors.customerName || undefined} />
          </Field>
          <Field label="Phone number" error={errors.phoneNumber} hint="10-digit mobile number; +91 or a leading 0 is fine." htmlFor="phoneNumber">
            <input id="phoneNumber" className="input" value={values.phoneNumber} inputMode="tel" maxLength={30} autoComplete="off"
              onChange={(e) => set('phoneNumber')(e.target.value)} aria-invalid={!!errors.phoneNumber || undefined} />
          </Field>
          <Field label="Alternative phone (optional)" error={errors.alternatePhoneNumber} hint="Another number to reach the customer." htmlFor="alternatePhoneNumber">
            <input id="alternatePhoneNumber" className="input" value={values.alternatePhoneNumber} inputMode="tel" maxLength={30} autoComplete="off"
              onChange={(e) => set('alternatePhoneNumber')(e.target.value)} aria-invalid={!!errors.alternatePhoneNumber || undefined} />
          </Field>
          <Field label={<><Clock size={13} /> Preferred delivery time</>} error={errors.deliveryTime}
            hint="One time for all of the customer’s packages." htmlFor="deliveryTime">
            <input id="deliveryTime" type="time" className="input" value={values.deliveryTime} step={300}
              onChange={(e) => set('deliveryTime')(e.target.value)} aria-invalid={!!errors.deliveryTime || undefined} />
          </Field>
          <Field label="Status" error={errors.statusId} htmlFor="status">
            <DictionarySelect id="status" value={values.statusId} options={dictionaries.customerStatus} current={currentStatus}
              placeholder="Choose status…" onChange={set('statusId')} invalid={!!errors.statusId} />
          </Field>
        </div>
      </section>

      <section className="form-section">
        <h3 className="form-section-title">
          <MapPin size={15} /> Address
        </h3>
        <div className="form-grid">
          <Field label="Address" error={errors.address} span htmlFor="address">
            <textarea id="address" className="input textarea" value={values.address} maxLength={500} rows={3}
              onChange={(e) => set('address')(e.target.value)} aria-invalid={!!errors.address || undefined} />
          </Field>
          <Field label="Pincode" error={errors.pincode} htmlFor="pincode">
            <input id="pincode" className="input" value={values.pincode} inputMode="numeric" maxLength={6} autoComplete="off"
              onChange={(e) => set('pincode')(e.target.value.replace(/\D/g, ''))} aria-invalid={!!errors.pincode || undefined} />
          </Field>
          <Field label="Google Maps link (optional)" error={errors.googleMapUrl} span htmlFor="googleMapUrl"
            hint="In Google Maps: open the place → Share → Copy link, then paste it here.">
            <input id="googleMapUrl" className="input" type="url" value={values.googleMapUrl} maxLength={2000} autoComplete="off"
              placeholder="https://maps.app.goo.gl/…" onChange={(e) => set('googleMapUrl')(e.target.value)} aria-invalid={!!errors.googleMapUrl || undefined} />
          </Field>
          {extra}
        </div>
      </section>
    </div>
  );
}

/** An end date the user may type over (package dialog when editing); `calculated` is what the server would use. */
export interface EditableEndDate {
  value: string;
  onChange: (value: string) => void;
  error?: string;
  /** True when the value was typed rather than calculated. */
  typed: boolean;
  onReset: () => void;
}

/**
 * The fields of one package with its server-calculated end date. Used for each package on Add Customer and in the package
 * dialog of the customer panel (where the end date can also be typed).
 */
export function PackageFields({ idPrefix, values, onChange, errors, dictionaries, current, preview, endDateLabel, fallbackEndDate, editableEndDate, onRemove, title }: {
  idPrefix: string;
  values: PackageFormValues;
  onChange: (values: PackageFormValues) => void;
  errors: PackageErrors;
  dictionaries: DictionaryOptions;
  current?: { preference?: CurrentValue; mealType?: CurrentValue; frequency?: CurrentValue };
  preview: ReturnType<typeof useSchedulePreview>;
  endDateLabel?: string;
  fallbackEndDate?: string | null;
  editableEndDate?: EditableEndDate;
  onRemove?: () => void;
  title?: ReactNode;
}) {
  const set = <K extends keyof PackageFormValues>(key: K) => (value: PackageFormValues[K]) => onChange({ ...values, [key]: value });
  const id = (name: string) => `${idPrefix}-${name}`;
  return (
    <div className="package-fields">
      {(title || onRemove) && (
        <div className="package-fields-header">
          <strong>{title}</strong>
          {onRemove && (
            <button type="button" className="icon-btn danger" onClick={onRemove} aria-label="Remove this package" title="Remove this package">
              <Trash2 size={15} />
            </button>
          )}
        </div>
      )}
      <div className="form-grid">
        <Field label="Preference" error={errors.preferenceId} htmlFor={id('preference')}>
          <DictionarySelect id={id('preference')} value={values.preferenceId} options={dictionaries.preference} current={current?.preference}
            placeholder="Choose preference…" onChange={set('preferenceId')} invalid={!!errors.preferenceId} />
        </Field>
        <Field label="Meal type" error={errors.mealTypeId} htmlFor={id('mealType')}>
          <DictionarySelect id={id('mealType')} value={values.mealTypeId} options={dictionaries.mealType} current={current?.mealType}
            placeholder="Choose meal type…" onChange={set('mealTypeId')} invalid={!!errors.mealTypeId} />
        </Field>
        <Field label="Quantity" error={errors.quantity} hint="Boxes per delivery." htmlFor={id('quantity')}>
          <input id={id('quantity')} type="number" className="input" min={1} max={MAX_QUANTITY} value={values.quantity}
            onChange={(e) => set('quantity')(e.target.value)} aria-invalid={!!errors.quantity || undefined} />
        </Field>
        <Field label="Frequency" error={errors.frequencyId} htmlFor={id('frequency')}>
          <DictionarySelect id={id('frequency')} value={values.frequencyId} options={dictionaries.frequency} current={current?.frequency} showDays
            placeholder="Choose frequency…" onChange={set('frequencyId')} invalid={!!errors.frequencyId} />
        </Field>
        <Field label="Start date" error={errors.startDate} hint="First delivery day (Monday–Saturday)." htmlFor={id('startDate')}>
          <input id={id('startDate')} type="date" className="input" value={values.startDate} onChange={(e) => set('startDate')(e.target.value)}
            aria-invalid={!!errors.startDate || undefined} />
        </Field>
        {editableEndDate ? (
          <Field
            label={endDateLabel ?? 'End date'}
            error={editableEndDate.error ?? preview.error ?? undefined}
            htmlFor={id('endDate')}
            hint={
              editableEndDate.typed ? (
                <>
                  Typed by hand.{' '}
                  <button type="button" className="link-button" onClick={editableEndDate.onReset}>
                    Use the calculated date
                  </button>
                </>
              ) : preview.preview ? (
                `Calculated: ${preview.preview.deliveryDays} delivery days (${preview.preview.frequency}), Sundays excluded. You can type another Monday–Saturday date.`
              ) : (
                'Last delivery day. You can type another Monday–Saturday date.'
              )
            }
          >
            <input id={id('endDate')} type="date" className="input" value={editableEndDate.value} min={values.startDate || undefined}
              onChange={(e) => editableEndDate.onChange(e.target.value)} aria-invalid={!!editableEndDate.error || undefined} />
          </Field>
        ) : (
          <EndDateField label={endDateLabel} preview={preview.preview} error={preview.error} loading={preview.loading} fallback={fallbackEndDate} />
        )}
      </div>
    </div>
  );
}

/** The calculated end date (read-only) with the delivery-day explanation. */
export function EndDateField({ preview, error, loading, fallback, label = 'End date' }: {
  preview: SchedulePreview | null;
  error: string | null;
  loading: boolean;
  fallback?: string | null;
  label?: string;
}) {
  return (
    <Field
      label={label}
      error={error ?? undefined}
      hint={
        preview ? (
          `${preview.deliveryDays} delivery days (${preview.frequency}), Sundays excluded.`
        ) : (
          <>
            <Info size={11} /> Calculated from the start date and frequency.
          </>
        )
      }
    >
      <ReadOnlyValue muted={!preview && !fallback}>
        {preview ? formatDay(preview.endDate, true) : loading && !fallback ? 'Calculating…' : fallback ? formatDay(fallback, true) : 'Choose a start date and frequency'}
      </ReadOnlyValue>
    </Field>
  );
}

export function PackagesTitle({ count }: { count: number }) {
  return (
    <>
      <CalendarDays size={15} /> Packages{count > 0 ? ` (${count})` : ''}
    </>
  );
}
