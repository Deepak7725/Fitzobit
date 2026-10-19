import { useState } from 'react';
import { ChevronLeft, ChevronRight } from 'lucide-react';

/** How one day looks in the calendar; only selectable days can be clicked. */
export interface DayState {
  selectable: boolean;
  /** Why the day cannot be chosen (shown as a tooltip). */
  note?: string;
  /** The day is already paused. */
  paused?: boolean;
}

const WEEKDAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

const toIso = (d: Date) => d.toISOString().slice(0, 10);

function monthOf(day: string): Date {
  const [y, m] = day.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, 1));
}

/**
 * A month calendar (Monday first) for choosing several days: click a day to select it, click again to clear it. Only months
 * from `from` to `to` can be shown. Dates are "yyyy-mm-dd" strings, handled in UTC so no time zone can shift a day.
 */
export default function PauseCalendar({ from, to, selected, dayState, onToggle }: {
  from: string;
  to: string;
  selected: string[];
  dayState: (day: string) => DayState;
  onToggle: (day: string) => void;
}) {
  const first = monthOf(from);
  const last = monthOf(to);
  const [month, setMonth] = useState(first);
  const year = month.getUTCFullYear();
  const index = month.getUTCMonth();
  const shift = (n: number) => setMonth(new Date(Date.UTC(year, index + n, 1)));
  const lead = (month.getUTCDay() + 6) % 7;
  const days = new Date(Date.UTC(year, index + 1, 0)).getUTCDate();
  const title = new Intl.DateTimeFormat('en-IN', { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(month);

  return (
    <div className="pause-calendar">
      <div className="pause-calendar-header">
        <button type="button" className="icon-btn" onClick={() => shift(-1)} disabled={month <= first} aria-label="Previous month">
          <ChevronLeft size={16} />
        </button>
        <strong>{title}</strong>
        <button type="button" className="icon-btn" onClick={() => shift(1)} disabled={month >= last} aria-label="Next month">
          <ChevronRight size={16} />
        </button>
      </div>
      <div className="pause-calendar-grid">
        {WEEKDAYS.map((w) => (
          <span key={w} className="pause-calendar-weekday">
            {w}
          </span>
        ))}
        {Array.from({ length: lead }, (_, i) => (
          <span key={`blank-${i}`} />
        ))}
        {Array.from({ length: days }, (_, i) => {
          const day = toIso(new Date(Date.UTC(year, index, i + 1)));
          const state = dayState(day);
          const isSelected = selected.includes(day);
          return (
            <button
              key={day}
              type="button"
              className={`pause-calendar-day ${isSelected ? 'is-selected' : ''} ${state.paused ? 'is-paused' : ''}`}
              disabled={!state.selectable}
              aria-pressed={isSelected}
              title={state.note}
              onClick={() => onToggle(day)}
            >
              {i + 1}
            </button>
          );
        })}
      </div>
    </div>
  );
}
