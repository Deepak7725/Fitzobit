const moneyFormatters = new Map<string, Intl.NumberFormat>();

export function formatMoney(amount: number, currency = 'INR'): string {
  let formatter = moneyFormatters.get(currency);
  if (!formatter) {
    formatter = new Intl.NumberFormat('en-IN', { style: 'currency', currency, minimumFractionDigits: 2 });
    moneyFormatters.set(currency, formatter);
  }
  return formatter.format(amount);
}

export function formatNumber(value: number): string {
  return new Intl.NumberFormat('en-IN').format(value);
}

export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return '—';
  return new Intl.DateTimeFormat('en-IN', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(iso));
}

export function formatBytes(bytes: number | null | undefined): string {
  if (bytes == null) return '—';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

/** "Monthly", "Weekly · 6 days" — only what the catalogue states. */
export function describePeriod(period: string, days: number | null): string {
  const parts = [period === 'Unspecified' ? 'Not stated' : period];
  if (days != null) parts.push(`${days} days`);
  return parts.join(' · ');
}

/** "07 Oct 2026" — compact date for tables. */
export function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—';
  return new Intl.DateTimeFormat('en-IN', { day: '2-digit', month: 'short', year: 'numeric' }).format(new Date(iso));
}
