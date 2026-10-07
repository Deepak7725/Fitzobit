import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { AlertCircle, AlertTriangle, Check, CheckCircle2 } from 'lucide-react';
import type { ImportGroupSummary, ImportIssue, ImportRow } from '../../api';
import { Badge } from '../../components/ui';

export const importSteps = [
  { key: 'upload', label: 'Upload Excel' },
  { key: 'validate', label: 'Validate' },
  { key: 'classify', label: 'Classify' },
  { key: 'groups', label: 'Create Groups' },
  { key: 'assign', label: 'Assign Customers' },
  { key: 'review', label: 'Review' },
  { key: 'save', label: 'Save' },
] as const;

export type StepKey = (typeof importSteps)[number]['key'];

export const stepPath = (batchId: number, step: StepKey) => (step === 'upload' ? '/upload' : `/upload/${batchId}/${step}`);

/** Workflow stepper. Steps after "Upload" are links once a batch exists; the current step is always highlighted. */
export function ImportStepper({ current, batchId, locked }: { current: StepKey; batchId?: number; locked?: boolean }) {
  const currentIndex = importSteps.findIndex((s) => s.key === current);
  return (
    <>
    <ol className="stepper" aria-label="Import steps">
      {importSteps.map((step, i) => {
        const state = i < currentIndex ? 'done' : i === currentIndex ? 'current' : '';
        const content = (
          <>
            <span className="step-dot">{state === 'done' ? <Check size={15} strokeWidth={3} /> : i + 1}</span>
            <span className="step-label">{step.label}</span>
          </>
        );
        const navigable = batchId !== undefined && !locked && i > 0 && i !== currentIndex;
        return (
          <li key={step.key} className={state} aria-current={state === 'current' ? 'step' : undefined}>
            {navigable ? (
              <Link to={stepPath(batchId!, step.key)} className="step-link">
                {content}
              </Link>
            ) : (
              <span className="step-link">{content}</span>
            )}
          </li>
        );
      })}
    </ol>
    {/* Step labels are hidden on narrow screens; keep the current step named. */}
    <p className="stepper-caption">
      Step {currentIndex + 1} of {importSteps.length} · <strong>{importSteps[currentIndex].label}</strong>
    </p>
    </>
  );
}

export function RowStatusBadge({ row }: { row: Pick<ImportRow, 'status' | 'included' | 'excludedByManager'> }) {
  if (row.status === 'Error') return <Badge tone="danger" icon={<AlertCircle size={13} />}>Error</Badge>;
  if (row.excludedByManager) return <Badge tone="neutral">Excluded</Badge>;
  if (row.status === 'Warning') return <Badge tone="warning" icon={<AlertTriangle size={13} />}>Warning</Badge>;
  return <Badge tone="success" icon={<CheckCircle2 size={13} />}>Valid</Badge>;
}

export function IssueList({ issues }: { issues: ImportIssue[] }) {
  if (issues.length === 0) return <span className="muted">—</span>;
  return (
    <ul className="issue-list">
      {issues.map((issue, i) => (
        <li key={i} className={issue.severity === 'Error' ? 'issue-error' : 'issue-warning'}>
          {issue.severity === 'Error' ? <AlertCircle size={13} /> : <AlertTriangle size={13} />}
          <span>{issue.message}</span>
        </li>
      ))}
    </ul>
  );
}

/** Drop-down of the groups available in an import: groups drafted here (new) and active saved groups. */
export function GroupSelect({ groups, value, onChange, placeholder = 'Choose group…', allowNone, disabled, ariaLabel }: {
  groups: ImportGroupSummary[];
  value: string;
  onChange: (key: string) => void;
  placeholder?: string;
  allowNone?: boolean;
  disabled?: boolean;
  ariaLabel?: string;
}) {
  const drafts = groups.filter((g) => g.isNew);
  const existing = groups.filter((g) => !g.isNew);
  return (
    <select className="select" value={value} onChange={(e) => onChange(e.target.value)} disabled={disabled} aria-label={ariaLabel ?? 'Delivery group'}>
      <option value="">{placeholder}</option>
      {allowNone && <option value="none">— No group —</option>}
      {drafts.length > 0 && (
        <optgroup label="New groups (this import)">
          {drafts.map((g) => (
            <option key={g.key} value={g.key}>
              {g.name}
            </option>
          ))}
        </optgroup>
      )}
      {existing.length > 0 && (
        <optgroup label="Existing groups">
          {existing.map((g) => (
            <option key={g.key} value={g.key}>
              {g.name}
            </option>
          ))}
        </optgroup>
      )}
    </select>
  );
}

export function StepFooter({ children, note }: { children: ReactNode; note?: ReactNode }) {
  return (
    <div className="step-footer">
      <div className="step-footer-note">{note}</div>
      <div className="step-footer-actions">{children}</div>
    </div>
  );
}

export function MiniStat({ label, value, tone }: { label: string; value: number | string; tone?: 'success' | 'warning' | 'danger' | 'info' }) {
  return (
    <div className={`mini-stat ${tone ? `mini-${tone}` : ''}`}>
      <span className="mini-value">{typeof value === 'number' ? value.toLocaleString('en-IN') : value}</span>
      <span className="mini-label">{label}</span>
    </div>
  );
}

export function BarList({ items, total, empty = 'Nothing to show', limit = 10 }: { items: { key: string; label: string; count: number }[]; total: number; empty?: string; limit?: number }) {
  if (items.length === 0) return <p className="muted small">{empty}</p>;
  const max = Math.max(...items.map((i) => i.count));
  return (
    <ul className="bar-list">
      {items.slice(0, limit).map((item) => (
        <li key={item.key}>
          <div className="bar-row">
            <span className="bar-label" title={item.label}>
              {item.label}
            </span>
            <span className="bar-count">{item.count.toLocaleString('en-IN')}</span>
          </div>
          <div className="bar-track" aria-hidden>
            <div className="bar-fill" style={{ width: `${(item.count / Math.max(max, 1)) * 100}%` }} />
          </div>
        </li>
      ))}
      {items.length > limit && <li className="muted small">and {items.length - limit} more · {total.toLocaleString('en-IN')} customers in total</li>}
    </ul>
  );
}
