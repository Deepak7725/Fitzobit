import type { GeoMatchLevel, GeocodeStatus } from '../api';
import { Badge } from './ui';

/** "Group 3" */
export function groupLabel(groupNumber: number | null | undefined): string {
  return groupNumber == null ? 'No group' : `Group ${groupNumber}`;
}

/** Distinct, colour-blind-friendly hues; the same group number always gets the same colour on every screen. */
const PALETTE = ['#2563eb', '#e8590c', '#0f9d58', '#c2255c', '#7048e8', '#0b8bad', '#d9480f', '#5c940d', '#ae3ec9', '#1864ab', '#a61e4d', '#2b8a3e'];
export const NO_GROUP_COLOR = '#94a3b8';

export function groupColor(groupNumber: number | null | undefined): string {
  return groupNumber == null ? NO_GROUP_COLOR : PALETTE[(groupNumber - 1) % PALETTE.length];
}

export function GroupPill({ groupNumber }: { groupNumber: number | null | undefined }) {
  if (groupNumber == null) return <Badge tone="warning">Unassigned</Badge>;
  return (
    <span className="group-pill" style={{ ['--group-color' as string]: groupColor(groupNumber) }}>
      <span className="group-dot" aria-hidden />
      {groupLabel(groupNumber)}
    </span>
  );
}

/** Location state of a customer: located (with precision), looking up, or unavailable. Never shows invented coordinates. */
export function LocationBadge({ status, level }: { status: GeocodeStatus; level: GeoMatchLevel | null }) {
  if (status === 'Located')
    return level === 'Pincode' ? (
      <Badge tone="warning">Approximate (pincode)</Badge>
    ) : (
      <Badge tone="success">{level === 'Locality' ? 'Located (area)' : 'Located'}</Badge>
    );
  if (status === 'Pending') return <Badge tone="info">Looking up…</Badge>;
  return <Badge tone="neutral">Location unavailable</Badge>;
}

export function formatCoordinate(value: number | null | undefined): string {
  return value == null ? '—' : value.toFixed(6);
}
