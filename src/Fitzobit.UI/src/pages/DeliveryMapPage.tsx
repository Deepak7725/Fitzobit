import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  AlertTriangle,
  BoxSelect,
  Eye,
  EyeOff,
  Hand,
  Info,
  LocateFixed,
  Maximize2,
  RefreshCw,
  Search,
  Sparkles,
  Star,
  Tag,
  Waypoints,
  X,
  ZoomIn,
  ZoomOut,
} from 'lucide-react';
import { api, type ChangeSummary, type MapData, type MapGroup, type Suggestion } from '../api';
import { useApi, useDebounced, useDictionaries } from '../hooks';
import { useAuth } from '../auth';
import { Card, ErrorPanel, Loading, PageHeader } from '../components/ui';
import { ConfirmDialog, useToast } from '../components/feedback';
import ChangeGroupDialog from '../components/ChangeGroupDialog';
import ClusterPlot, { type ClusterPlotHandle, type PlotGroup, type PlotPoint } from '../components/ClusterPlot';
import { NO_GROUP_COLOR, groupColor, groupLabel } from '../components/groups';

type Mode = 'live' | 'draft';

interface Draft {
  assignments: Map<number, number | null>;
  groups: MapGroup[];
  changes: ChangeSummary;
  notes: string[];
  requested: number;
  used: number;
}

const DEFAULT_STATUSES = ['ACTIVE', 'PAUSED'];

/** Data classification: customers by longitude (X) and latitude (Y), coloured by delivery group. */
export default function DeliveryMapPage() {
  const toast = useToast();
  const { isAdmin } = useAuth();
  const dictionaries = useDictionaries();
  const plot = useRef<ClusterPlotHandle>(null);
  const [statuses, setStatuses] = useState<string[]>(DEFAULT_STATUSES);
  const map = useApi((signal) => api.classificationMap(statuses, signal), [statuses.join(',')]);
  const [mode, setMode] = useState<Mode>('live');
  const [draft, setDraft] = useState<Draft | null>(null);
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [highlight, setHighlight] = useState<number | null>(null);
  const [showLabels, setShowLabels] = useState(true);
  const [showBoundaries, setShowBoundaries] = useState(true);
  const [showCentres, setShowCentres] = useState(true);
  const [boxSelect, setBoxSelect] = useState(false);
  const [hidden, setHidden] = useState<Set<number | 'none'>>(new Set());
  const [changingGroup, setChangingGroup] = useState(false);
  const [saving, setSaving] = useState(false);
  const [confirmSave, setConfirmSave] = useState(false);
  const [reason, setReason] = useState('');

  const data = map.data;
  const groupOf = useCallback(
    (id: number, current: number | null) => (mode === 'draft' && draft ? (draft.assignments.has(id) ? draft.assignments.get(id)! : current) : current),
    [mode, draft],
  );

  const points: PlotPoint[] = useMemo(
    () =>
      (data?.points ?? []).map((p) => {
        const group = groupOf(p.id, p.groupNumber);
        return {
          id: p.id,
          lat: p.lat,
          lng: p.lng,
          group,
          approximate: p.approximate,
          label: p.customerId,
          title: `${p.customerId} · ${p.name}`,
          lines: [
            p.address ?? 'No address',
            [p.area, p.pincode, p.mealType, p.status].filter((x, i, all) => x && all.indexOf(x) === i).join(' · '),
            mode === 'draft' && group !== p.groupNumber
              ? `${groupLabel(p.groupNumber)} → ${groupLabel(group)} (draft)`
              : groupLabel(group) + (p.approximate ? ' · approximate location (pincode)' : ''),
            `${p.lat.toFixed(5)}, ${p.lng.toFixed(5)}`,
          ],
        };
      }),
    [data, groupOf, mode],
  );
  const groups: MapGroup[] = mode === 'draft' && draft ? draft.groups : (data?.groups ?? []);
  const plotGroups: PlotGroup[] = groups.map((g) => ({
    number: g.number,
    boundary: g.boundary,
    centerLat: g.centerLat,
    centerLng: g.centerLng,
    areas: g.areas.slice(0, 3).map((a) => a.name),
  }));
  const unassignedCount = points.filter((p) => p.group == null).length + (data?.unlocated ?? []).filter((u) => groupOf(u.id, u.groupNumber) == null).length;

  // Leave the draft when the scope changes (it was computed for another set of customers).
  useEffect(() => {
    setMode('live');
    setDraft(null);
    setSelected(new Set());
  }, [statuses.join(',')]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && !changingGroup && !confirmSave && setSelected(new Set());
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [changingGroup, confirmSave]);

  const select = (ids: number[], how: 'replace' | 'add') =>
    setSelected((prev) => {
      if (how === 'replace') return new Set(ids);
      const next = new Set(prev);
      if (ids.length === 1 && next.has(ids[0])) next.delete(ids[0]);
      else ids.forEach((id) => next.add(id));
      return next;
    });

  // ---------- Draft (suggested groups) ----------
  const evaluate = useRef(0);
  const reevaluate = async (assignments: Map<number, number | null>) => {
    const run = ++evaluate.current;
    try {
      const result = await api.evaluateDraft([...assignments].map(([customerId, groupNumber]) => ({ customerId, groupNumber })));
      if (run === evaluate.current) setDraft((d) => (d ? { ...d, groups: result.groups, changes: result.changes } : d));
    } catch (e) {
      toast(e instanceof Error ? e.message : 'The draft could not be checked.', 'error');
    }
  };

  const startDraft = (s: Suggestion) => {
    setDraft({
      assignments: new Map(s.assignments.map((a) => [a.id, a.groupNumber])),
      groups: s.groups,
      changes: s.changes,
      notes: s.notes,
      requested: s.requestedGroups,
      used: s.groupsUsed,
    });
    setMode('draft');
    setSelected(new Set());
    setTimeout(() => plot.current?.fit(), 0);
  };

  const moveInDraft = (target: number | null) => {
    if (!draft || selected.size === 0) return;
    const next = new Map(draft.assignments);
    selected.forEach((id) => next.set(id, target));
    setDraft({ ...draft, assignments: next });
    toast(`${selected.size} customer(s) set to ${groupLabel(target)} in the draft.`, 'info');
    setSelected(new Set());
    void reevaluate(next);
  };

  const saveDraft = async () => {
    if (!draft) return;
    setSaving(true);
    try {
      const result = await api.applyDraft(
        [...draft.assignments].map(([customerId, groupNumber]) => ({ customerId, groupNumber })),
        reason.trim() || null,
      );
      toast(
        `Saved: ${result.assigned} assigned, ${result.moved} moved, ${result.removed} removed` +
          (result.createdGroupNumbers.length > 0 ? `; created ${result.createdGroupNumbers.map(groupLabel).join(', ')}.` : '.'),
      );
      setConfirmSave(false);
      setDraft(null);
      setMode('live');
      setReason('');
      map.reload();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'The groups could not be saved.', 'error');
    } finally {
      setSaving(false);
    }
  };

  const retryLookups = async () => {
    try {
      const result = await api.locateCustomers(null);
      toast(`${result.queued.toLocaleString('en-IN')} customer(s) queued for a location lookup. Refresh in a minute.`, 'info');
      map.reload();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'The lookups could not be queued.', 'error');
    }
  };

  const statusOptions = dictionaries.data?.customerStatus ?? [];
  const toggleStatus = (code: string) =>
    setStatuses((s) => (s.includes(code) ? (s.length > 1 ? s.filter((x) => x !== code) : s) : [...s, code]));

  return (
    <>
      <PageHeader
        title="Delivery Classification"
        subtitle="Every customer with a known location plotted by longitude (X) and latitude (Y), coloured by delivery group and labelled with the areas each group covers."
        actions={
          <button className="btn btn-secondary" onClick={map.reload} disabled={map.loading}>
            <RefreshCw size={16} /> Refresh
          </button>
        }
      />

      {map.error && <ErrorPanel message={map.error} onRetry={map.reload} />}
      {map.loading && !data && <Loading label="Loading customer locations…" />}

      {data && (
        <>
          <AreaNotices data={data} isAdmin={isAdmin} onRetry={retryLookups} />

          <div className="map-toolbar card">
            <div className="tabs tabs-inline" role="tablist" aria-label="Map mode">
              <button className={mode === 'live' ? 'active' : ''} onClick={() => setMode('live')}>
                Current groups
              </button>
              <button className={mode === 'draft' ? 'active' : ''} onClick={() => setMode('draft')}>
                Suggest groups{draft ? ' (draft)' : ''}
              </button>
            </div>
            <div className="chips" aria-label="Customer statuses shown">
              {statusOptions.map((s) => (
                <button key={s.code} className={`chip ${statuses.includes(s.code) ? 'active' : ''}`} onClick={() => toggleStatus(s.code)}>
                  {s.displayName}
                </button>
              ))}
            </div>
            <CustomerSearch data={data} onPick={(id, located) => {
              setHighlight(id);
              setSelected(new Set([id]));
              if (located) plot.current?.focus(id);
            }} />
          </div>

          {mode === 'draft' && <SuggestPanel draft={draft} located={data.located} statuses={statuses} onSuggested={startDraft}
            onDiscard={() => { setDraft(null); setSelected(new Set()); }} onSave={() => setConfirmSave(true)} />}

          <div className="map-stats" aria-label="Classification summary">
            <span>
              <strong>{groups.length.toLocaleString('en-IN')}</strong> group{groups.length === 1 ? '' : 's'}
              {mode === 'draft' ? ' (draft)' : ''}
            </span>
            <span>
              <strong>{data.located.toLocaleString('en-IN')}</strong> of {data.inScope.toLocaleString('en-IN')} on the map
              {data.approximate > 0 && <small> ({data.approximate} approximate)</small>}
            </span>
            <span className={data.unlocatedTotal > 0 ? 'map-stat-warn' : ''}>
              <strong>{data.unlocatedTotal.toLocaleString('en-IN')}</strong> without coordinates
            </span>
            <span className={unassignedCount > 0 ? 'map-stat-warn' : ''}>
              <strong>{unassignedCount.toLocaleString('en-IN')}</strong> unassigned
            </span>
          </div>

          <div className="map-layout">
            <Card className="map-card">
              <div className="plot-tools">
                <button className={`icon-btn ${!boxSelect ? 'active' : ''}`} title="Pan (drag)" aria-pressed={!boxSelect} onClick={() => setBoxSelect(false)}>
                  <Hand size={16} />
                </button>
                <button className={`icon-btn ${boxSelect ? 'active' : ''}`} title="Select an area (or Shift+drag)" aria-pressed={boxSelect} onClick={() => setBoxSelect(true)}>
                  <BoxSelect size={16} />
                </button>
                <span className="tool-sep" />
                <button className="icon-btn" title="Zoom in" onClick={() => plot.current?.zoom(1.5)}>
                  <ZoomIn size={16} />
                </button>
                <button className="icon-btn" title="Zoom out" onClick={() => plot.current?.zoom(1 / 1.5)}>
                  <ZoomOut size={16} />
                </button>
                <button className="icon-btn" title="Fit all customers" onClick={() => plot.current?.fit()}>
                  <Maximize2 size={16} />
                </button>
                <span className="tool-sep" />
                <label className="toggle">
                  <input type="checkbox" checked={showLabels} onChange={(e) => setShowLabels(e.target.checked)} />
                  <Tag size={14} /> Labels
                </label>
                <label className="toggle">
                  <input type="checkbox" checked={showBoundaries} onChange={(e) => setShowBoundaries(e.target.checked)} />
                  <Waypoints size={14} /> Boundaries
                </label>
                <label className="toggle" title="Star at each group's centre (the K-Means centroid of a suggestion)">
                  <input type="checkbox" checked={showCentres} onChange={(e) => setShowCentres(e.target.checked)} />
                  <Star size={14} /> Centres
                </label>
              </div>
              {points.length === 0 ? (
                <div className="plot-empty">
                  <Info size={20} />
                  <p>
                    No customer in this view has a known location yet.
                    {data.inScope > 0 && ' Locations are looked up automatically once the service city is set; customers can still be assigned by hand below.'}
                  </p>
                </div>
              ) : (
                <ClusterPlot
                  ref={plot}
                  points={points}
                  groups={plotGroups}
                  selected={selected}
                  highlightId={highlight}
                  showLabels={showLabels}
                  showBoundaries={showBoundaries}
                  showCentres={showCentres}
                  boxSelect={boxSelect}
                  hiddenGroups={hidden}
                  onSelect={select}
                />
              )}
              <p className="footnote">
                Scroll to zoom, drag to pan, click a customer to select (Ctrl/Shift adds), Shift+drag to select an area. Hollow points are approximate
                (pincode area only); stars mark each group's centre. Outlines are drawn only for groups with three or more locations.
              </p>
            </Card>

            <div className="stack map-side">
              <Card title="Groups and their areas" subtitle={mode === 'draft' ? 'Draft (not saved yet)' : 'Customers per group, with the areas (localities) they live in'}>
                <ul className="legend">
                  {groups.map((g) => (
                    <li key={g.number} className={hidden.has(g.number) ? 'is-hidden' : ''}>
                      <button className="legend-toggle" title={hidden.has(g.number) ? 'Show' : 'Hide'} onClick={() => setHidden((h) => toggle(h, g.number))}>
                        {hidden.has(g.number) ? <EyeOff size={14} /> : <Eye size={14} />}
                      </button>
                      <button className="legend-name" onClick={() => select(points.filter((p) => p.group === g.number).map((p) => p.id), 'replace')}
                        title="Select this group's customers">
                        <span className="swatch" style={{ background: groupColor(g.number) }} />
                        {groupLabel(g.number)}
                        {g.isNew && <span className="tag tag-new">new</span>}
                      </button>
                      <span className="legend-count" title={`${g.customerCount} customer(s), ${g.locatedCount} on the map`}>
                        {g.customerCount}
                        {g.locatedCount < g.customerCount && <small> ({g.locatedCount} located)</small>}
                      </span>
                      {g.areas.length > 0 && (
                        <span className="legend-areas" title={g.areas.map((a) => `${a.name} (${a.count})`).join(', ')}>
                          {g.areas.slice(0, 5).map((a, i) => (
                            <span key={a.name}>
                              {i > 0 && ' · '}
                              <b>{a.name}</b> {a.count}
                            </span>
                          ))}
                          {g.areas.length > 5 && ` · +${g.areas.length - 5} more`}
                        </span>
                      )}
                    </li>
                  ))}
                  <li className={hidden.has('none') ? 'is-hidden' : ''}>
                    <button className="legend-toggle" onClick={() => setHidden((h) => toggle(h, 'none'))}>
                      {hidden.has('none') ? <EyeOff size={14} /> : <Eye size={14} />}
                    </button>
                    <button className="legend-name" onClick={() => select(points.filter((p) => p.group == null).map((p) => p.id), 'replace')}>
                      <span className="swatch" style={{ background: NO_GROUP_COLOR }} />
                      No group
                    </button>
                    <span className="legend-count">{unassignedCount}</span>
                  </li>
                </ul>
                {groups.length === 0 && (
                  <p className="footnote">
                    No delivery groups yet. Use <strong>Suggest groups</strong>, or <Link to="/groups" className="link">create groups</Link> and assign customers.
                  </p>
                )}
              </Card>

              <SelectionPanel
                count={selected.size}
                mode={mode}
                draft={draft}
                onClear={() => setSelected(new Set())}
                onChangeGroup={() => setChangingGroup(true)}
                onMoveInDraft={moveInDraft}
              />

              <UnlocatedPanel data={data} selected={selected} groupOf={groupOf} onToggle={(id) => select([id], 'add')} onRetry={retryLookups} />
            </div>
          </div>
        </>
      )}

      {changingGroup && data && (
        <ChangeGroupDialog
          customerIds={[...selected]}
          groups={data.groups.filter((g) => g.id != null).map((g) => ({ id: g.id!, name: groupLabel(g.number), isActive: g.isActive }))}
          onClose={() => setChangingGroup(false)}
          onDone={() => {
            setChangingGroup(false);
            setSelected(new Set());
            map.reload();
          }}
        />
      )}
      {confirmSave && draft && (
        <ConfirmDialog
          title="Save these delivery groups?"
          messages={
            draft.changes.messages.length > 0
              ? [
                  ...draft.changes.messages,
                  ...(draft.changes.newGroupNumbers.length > 0 ? [`New groups will be created: ${draft.changes.newGroupNumbers.map(groupLabel).join(', ')}.`] : []),
                  `${draft.changes.unchanged} customer(s) stay in their current group. Every change is kept in the assignment history.`,
                ]
              : ['Nothing changes: every customer is already in the suggested group.']
          }
          confirmLabel={`Save ${draft.changes.assigned + draft.changes.moved + draft.changes.removed} change(s)`}
          busy={saving}
          onConfirm={saveDraft}
          onCancel={() => setConfirmSave(false)}
        >
          <label className="field" style={{ marginTop: 12 }}>
            Reason (recorded in the history)
            <input className="input" value={reason} maxLength={300} onChange={(e) => setReason(e.target.value)} placeholder="e.g. New delivery routes from November" />
          </label>
        </ConfirmDialog>
      )}
    </>
  );
}

function toggle<T>(set: Set<T>, value: T): Set<T> {
  const next = new Set(set);
  if (next.has(value)) next.delete(value);
  else next.add(value);
  return next;
}

function AreaNotices({ data, isAdmin, onRetry }: { data: MapData; isAdmin: boolean; onRetry: () => void }) {
  const missing = data.inScope - data.located;
  return (
    <>
      {!data.area.geocodingConfigured && (
        <div className="alert alert-warning">
          <AlertTriangle size={18} />
          <div>
            <strong>Geocoding is not configured.</strong> Customer locations cannot be looked up until a geocoding provider is set on the server
            (<code className="code">Geocoding__Provider</code>).
          </div>
        </div>
      )}
      {data.area.geocodingConfigured && !data.area.isConfigured && (
        <div className="alert alert-warning">
          <AlertTriangle size={18} />
          <div>
            <strong>No service city set.</strong> Locations are looked up only inside the city Fitzobit delivers in.{' '}
            {isAdmin ? 'Set it under Settings → General → Delivery area.' : 'Ask an administrator to set it (Settings → General).'}
          </div>
          {isAdmin && (
            <Link to="/settings" className="btn btn-secondary btn-sm">
              Open Settings
            </Link>
          )}
        </div>
      )}
      {data.area.isConfigured && missing > 0 && (
        <div className="alert alert-info">
          <Info size={18} />
          <div>
            <strong>{missing.toLocaleString('en-IN')} customer(s) have no location</strong> and are not on the map (still being looked up, or the
            address was not found in {data.area.city}). They can still be assigned to a group by hand.
          </div>
          <button className="btn btn-secondary btn-sm" onClick={onRetry}>
            <LocateFixed size={14} /> Retry lookups
          </button>
        </div>
      )}
    </>
  );
}

function CustomerSearch({ data, onPick }: { data: MapData; onPick: (id: number, located: boolean) => void }) {
  const [text, setText] = useState('');
  const q = useDebounced(text.trim().toLowerCase(), 150);
  const results = useMemo(() => {
    if (q.length < 2) return [];
    const located = data.points
      .filter((p) => p.customerId.toLowerCase().includes(q) || p.name.toLowerCase().includes(q))
      .map((p) => ({ id: p.id, customerId: p.customerId, name: p.name, located: true }));
    const unlocated = data.unlocated
      .filter((p) => p.customerId.toLowerCase().includes(q) || p.name.toLowerCase().includes(q))
      .map((p) => ({ id: p.id, customerId: p.customerId, name: p.name, located: false }));
    return [...located, ...unlocated].slice(0, 8);
  }, [q, data]);

  return (
    <div className="map-search">
      <label className="search">
        <Search size={16} />
        <input placeholder="Find a customer (ID or name)" value={text} onChange={(e) => setText(e.target.value)} aria-label="Find a customer on the map" />
        {text && (
          <button className="icon-btn icon-btn-xs" onClick={() => setText('')} aria-label="Clear search">
            <X size={14} />
          </button>
        )}
      </label>
      {results.length > 0 && (
        <ul className="menu map-search-results">
          {results.map((r) => (
            <li key={r.id}>
              <button
                onClick={() => {
                  onPick(r.id, r.located);
                  setText('');
                }}
              >
                <strong>{r.customerId}</strong> {r.name}
                {!r.located && <small className="muted"> · no location</small>}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function SuggestPanel({ draft, located, statuses, onSuggested, onDiscard, onSave }: {
  draft: Draft | null;
  located: number;
  statuses: string[];
  onSuggested: (s: Suggestion) => void;
  onDiscard: () => void;
  onSave: () => void;
}) {
  const toast = useToast();
  const [count, setCount] = useState(String(draft?.requested ?? 3));
  const [auto, setAuto] = useState(!draft);
  const [balance, setBalance] = useState(false);
  const [byPincode, setByPincode] = useState(false);
  const [busy, setBusy] = useState(false);
  const n = Number(count);
  const valid = auto || (Number.isInteger(n) && n >= 1 && n <= 50);

  const suggest = async () => {
    setBusy(true);
    try {
      const suggestion = await api.suggestGroups(auto ? 0 : n, auto, statuses, balance, byPincode);
      if (auto) setCount(String(suggestion.groupsUsed));
      onSuggested(suggestion);
    } catch (e) {
      toast(e instanceof Error ? e.message : 'No suggestion could be made.', 'error');
    } finally {
      setBusy(false);
    }
  };

  const changes = draft?.changes;
  const changeCount = changes ? changes.assigned + changes.moved + changes.removed : 0;
  return (
    <Card className="suggest-card">
      <div className="suggest-row">
        <label className="toggle" title="Find how many separate areas the customer locations form">
          <input type="checkbox" checked={auto} onChange={(e) => setAuto(e.target.checked)} />
          Auto (natural groups)
        </label>
        <label className="field field-inline">
          Number of groups
          <input className="input input-sm" type="number" min={1} max={50} value={count} disabled={auto} onChange={(e) => setCount(e.target.value)} />
        </label>
        <label className="toggle" title="No group gets more than about 15% above the average size">
          <input type="checkbox" checked={balance} onChange={(e) => setBalance(e.target.checked)} />
          Balance group sizes
        </label>
        <label className="toggle" title="Customers without a location join the group most customers with the same pincode are in">
          <input type="checkbox" checked={byPincode} onChange={(e) => setByPincode(e.target.checked)} />
          Place customers without a location by pincode
        </label>
        <button className="btn btn-primary" onClick={suggest} disabled={busy || !valid || located === 0}>
          <Sparkles size={16} /> {busy ? 'Calculating…' : draft ? 'Suggest again' : 'Suggest groups'}
        </button>
        {draft && (
          <>
            <button className="btn btn-secondary" onClick={onDiscard}>
              Discard draft
            </button>
            <button className="btn btn-primary" onClick={onSave} disabled={busy}>
              Review &amp; save ({changeCount})
            </button>
          </>
        )}
      </div>
      <p className="footnote">
        <Info size={12} /> Each customer is a point at its longitude and latitude; nearby points are put in the same group (K-Means, same result
        every time). With Auto, the number of groups follows how many separate areas the points form; otherwise you choose it. Suggested groups keep
        existing group numbers where they overlap, so as few customers as possible move. This is a starting point, not a route plan: move customers between
        groups on the map, then review and save. Nothing changes until you save.
        {located === 0 && ' No customer in this view has a location yet, so no suggestion is possible.'}
      </p>
      {draft && (
        <div className="draft-summary">
          {draft.notes.map((note) => (
            <div key={note} className="draft-note">
              <Info size={13} /> {note}
            </div>
          ))}
          <div className="draft-changes">
            <strong>
              Draft: {changes!.assigned} to assign · {changes!.moved} to move · {changes!.removed} to remove · {changes!.unchanged} unchanged
            </strong>
            {changes!.messages.length > 0 && (
              <ul>
                {changes!.messages.slice(0, 8).map((m) => (
                  <li key={m}>{m}</li>
                ))}
                {changes!.messages.length > 8 && <li className="muted">… and {changes!.messages.length - 8} more</li>}
              </ul>
            )}
          </div>
        </div>
      )}
    </Card>
  );
}

function SelectionPanel({ count, mode, draft, onClear, onChangeGroup, onMoveInDraft }: {
  count: number;
  mode: Mode;
  draft: Draft | null;
  onClear: () => void;
  onChangeGroup: () => void;
  onMoveInDraft: (target: number | null) => void;
}) {
  const [target, setTarget] = useState('');
  if (count === 0)
    return (
      <Card title="Selection">
        <p className="muted small">Click customers on the map, select an area, or pick a group in the legend to assign them to a group.</p>
      </Card>
    );

  const numbers = draft ? draft.groups.map((g) => g.number) : [];
  const nextNumber = numbers.length > 0 ? Math.max(...numbers) + 1 : 1;
  return (
    <Card title={`${count.toLocaleString('en-IN')} selected`} actions={<button className="link-button" onClick={onClear}>Clear</button>}>
      {mode === 'live' || !draft ? (
        <button className="btn btn-primary btn-full" onClick={onChangeGroup}>
          Assign / move to a group…
        </button>
      ) : (
        <div className="selection-actions">
          <select className="select select-full" value={target} onChange={(e) => setTarget(e.target.value)} aria-label="Draft group">
            <option value="">Move to…</option>
            {numbers.map((n) => (
              <option key={n} value={n}>
                {groupLabel(n)}
              </option>
            ))}
            <option value={nextNumber}>{groupLabel(nextNumber)} (new)</option>
            <option value="none">No group</option>
          </select>
          <button className="btn btn-primary" disabled={!target} onClick={() => { onMoveInDraft(target === 'none' ? null : Number(target)); setTarget(''); }}>
            Move in draft
          </button>
        </div>
      )}
      <p className="footnote">
        {mode === 'live' ? 'Changes are saved right away, with a preview and a reason in the history.' : 'Changes stay in the draft until you save it.'}
      </p>
    </Card>
  );
}

function UnlocatedPanel({ data, selected, groupOf, onToggle, onRetry }: {
  data: MapData;
  selected: ReadonlySet<number>;
  groupOf: (id: number, current: number | null) => number | null;
  onToggle: (id: number) => void;
  onRetry: () => void;
}) {
  if (data.unlocatedTotal === 0) return null;
  return (
    <Card
      title={`Location unavailable (${data.unlocatedTotal.toLocaleString('en-IN')})`}
      subtitle="Not on the map; select to assign by hand"
      actions={
        <button className="icon-btn" title="Retry location lookups" onClick={onRetry}>
          <LocateFixed size={16} />
        </button>
      }
    >
      <ul className="unlocated-list">
        {data.unlocated.map((u) => (
          <li key={u.id}>
            <label>
              <input type="checkbox" checked={selected.has(u.id)} onChange={() => onToggle(u.id)} />
              <span>
                <strong>{u.customerId}</strong> {u.name}
                <small className="item-sub">
                  {[u.pincode, u.geocodeStatus === 'Pending' ? 'looking up…' : u.geocodeStatus === 'NotFound' ? 'address not found' : 'lookup failed'].filter(Boolean).join(' · ')}
                  {' · '}
                  {groupLabel(groupOf(u.id, u.groupNumber))}
                </small>
              </span>
            </label>
          </li>
        ))}
      </ul>
      {data.unlocatedTotal > data.unlocated.length && (
        <p className="footnote">
          Showing {data.unlocated.length} of {data.unlocatedTotal}. Find the rest in <Link to="/data?geo=missing" className="link">Customers → Location unavailable</Link>.
        </p>
      )}
    </Card>
  );
}
