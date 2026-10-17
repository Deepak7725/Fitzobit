import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { AlertTriangle, CheckCircle2, Inbox, RefreshCw } from 'lucide-react';

export function PageHeader({ title, subtitle, actions }: { title: string; subtitle?: string; actions?: ReactNode }) {
  return (
    <header className="page-header">
      <div>
        <h1>{title}</h1>
        {subtitle && <p>{subtitle}</p>}
      </div>
      {actions && <div className="page-header-actions">{actions}</div>}
    </header>
  );
}

export function Card({ title, subtitle, actions, children, className = '' }: {
  title?: ReactNode;
  subtitle?: ReactNode;
  actions?: ReactNode;
  children: ReactNode;
  className?: string;
}) {
  return (
    <section className={`card ${className}`}>
      {(title || actions) && (
        <div className="card-header">
          <div>
            {title && <h2>{title}</h2>}
            {subtitle && <p className="card-subtitle">{subtitle}</p>}
          </div>
          {actions}
        </div>
      )}
      {children}
    </section>
  );
}

export type Tone = 'blue' | 'green' | 'amber' | 'violet' | 'teal' | 'rose' | 'slate' | 'indigo';

/** A headline number; with <c>to</c> the whole card links to the matching customers. */
export function StatCard({ icon, label, value, hint, tone, to }: {
  icon: ReactNode;
  label: string;
  value: ReactNode;
  hint?: ReactNode;
  tone: Tone;
  to?: string;
}) {
  const body = (
    <>
      <div className={`stat-icon tone-${tone}`}>{icon}</div>
      <div className="stat-body">
        <span className="stat-label">{label}</span>
        <span className="stat-value">{value}</span>
        {hint && <span className="stat-hint">{hint}</span>}
      </div>
    </>
  );
  return to ? (
    <Link to={to} className="stat-card stat-card-link">
      {body}
    </Link>
  ) : (
    <div className="stat-card">{body}</div>
  );
}

export function Badge({ tone, children, icon }: { tone: 'success' | 'warning' | 'danger' | 'info' | 'neutral'; children: ReactNode; icon?: ReactNode }) {
  return (
    <span className={`badge badge-${tone}`}>
      {icon}
      {children}
    </span>
  );
}

export function WarningBadge({ count, label = 'Price mismatch' }: { count: number; label?: string }) {
  if (count === 0)
    return (
      <Badge tone="success" icon={<CheckCircle2 size={13} />}>
        OK
      </Badge>
    );
  return (
    <Badge tone="warning" icon={<AlertTriangle size={13} />}>
      {label}
      {count > 1 ? ` (${count})` : ''}
    </Badge>
  );
}

export function EmptyState({ icon, title, message, action }: { icon?: ReactNode; title: string; message: ReactNode; action?: ReactNode }) {
  return (
    <div className="empty-state">
      <div className="empty-icon">{icon ?? <Inbox size={22} />}</div>
      <h3>{title}</h3>
      <p>{message}</p>
      {action}
    </div>
  );
}

export function Loading({ label = 'Loading…' }: { label?: string }) {
  return (
    <div className="loading" role="status">
      <span className="spinner" aria-hidden />
      {label}
    </div>
  );
}

export function ErrorPanel({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div className="error-panel" role="alert">
      <AlertTriangle size={18} />
      <div>
        <strong>Could not load data.</strong> {message}
      </div>
      {onRetry && (
        <button className="btn btn-secondary btn-sm" onClick={onRetry}>
          <RefreshCw size={14} /> Retry
        </button>
      )}
    </div>
  );
}

/** Short list of values as tags, with "+N" when more exist. */
export function Chips({ values, more = 0 }: { values: string[]; more?: number }) {
  if (values.length === 0) return <span className="muted">—</span>;
  return (
    <div className="tag-cloud">
      {values.map((v) => (
        <span key={v} className="tag">
          {v}
        </span>
      ))}
      {more > 0 && <span className="tag tag-more">+{more}</span>}
    </div>
  );
}
