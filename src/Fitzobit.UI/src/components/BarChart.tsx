import { Link } from 'react-router-dom';
import type { ChartItem } from '../api';

/**
 * Horizontal bar chart for one series (customer counts by a category). One hue; a recessive gray for the
 * "no value" bucket. Each row is a hover/click target with a tooltip and, where given, a link to the matching
 * customers. Labels and values are plain text, so the chart doubles as its own table.
 */
export default function BarChart({ items, total, linkFor, limit = 10, mutedKeys = ['none'], unit = 'customer', label }: {
  items: ChartItem[];
  total: number;
  linkFor?: (item: ChartItem) => string | null;
  limit?: number;
  mutedKeys?: string[];
  unit?: string;
  label: string;
}) {
  const shown = items.slice(0, limit);
  const hiddenCount = items.length - shown.length;
  const max = Math.max(1, ...shown.map((i) => i.count));

  return (
    <div className="hbar-chart" role="table" aria-label={label}>
      {shown.map((item) => {
        const share = total > 0 ? Math.round((item.count / total) * 1000) / 10 : 0;
        const tooltip = `${item.label}: ${item.count.toLocaleString('en-IN')} ${unit}${item.count === 1 ? '' : 's'} · ${share}% of all`;
        const href = linkFor?.(item) ?? null;
        const row = (
          <>
            <span className="hbar-label" role="cell" title={item.label}>
              {item.label}
            </span>
            <span className="hbar-track" role="cell" aria-hidden>
              <span
                className={`hbar-fill ${mutedKeys.includes(item.key) ? 'muted-fill' : ''}`}
                style={{ width: item.count > 0 ? `${Math.max(1.5, (item.count / max) * 100)}%` : 0 }}
              />
            </span>
            <span className="hbar-value" role="cell">
              {item.count.toLocaleString('en-IN')}
            </span>
            <span className="hbar-tip" role="tooltip">
              {tooltip}
            </span>
          </>
        );
        return href ? (
          <Link key={item.key} to={href} className="hbar-row" role="row" aria-label={tooltip}>
            {row}
          </Link>
        ) : (
          <div key={item.key} className="hbar-row" role="row" aria-label={tooltip}>
            {row}
          </div>
        );
      })}
      {hiddenCount > 0 && (
        <p className="hbar-more muted small">
          + {hiddenCount} more not shown ({items.slice(limit).reduce((s, i) => s + i.count, 0).toLocaleString('en-IN')} {unit}s)
        </p>
      )}
    </div>
  );
}
