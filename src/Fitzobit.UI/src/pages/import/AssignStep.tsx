import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ArrowLeft, ArrowRight, Check, Lightbulb, Search, Sparkles } from 'lucide-react';
import { api, type AssignmentPreview, type ImportDetail, type RowFilter, type RowSelection, type SuggestionResult } from '../../api';
import { useApi, useDebounced } from '../../hooks';
import { Badge, Card, EmptyState, ErrorPanel, Loading } from '../../components/ui';
import { ConfirmDialog, Pagination, useToast } from '../../components/feedback';
import { GroupSelect, MiniStat, StepFooter, stepPath } from './shared';

type Dimension = 'pincode' | 'locality' | 'city';
const PageSize = 50;

interface PendingChange {
  title: string;
  preview: AssignmentPreview;
  run: () => Promise<number>;
}

export default function AssignStep({ detail, onChanged }: { detail: ImportDetail; onChanged: () => void }) {
  const navigate = useNavigate();
  const toast = useToast();
  const editable = detail.status === 'Validated';
  const groups = useApi((signal) => api.importGroups(detail.id, signal), [detail.id]);
  const classification = useApi((signal) => api.classification(detail.id, signal), [detail.id]);

  const [search, setSearch] = useState('');
  const [groupFilter, setGroupFilter] = useState('');
  const [pincodeFilter, setPincodeFilter] = useState('');
  const [suggestedOnly, setSuggestedOnly] = useState(false);
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [allMatching, setAllMatching] = useState(false);
  const [bulkTarget, setBulkTarget] = useState('');
  const [pending, setPending] = useState<PendingChange | null>(null);
  const [busy, setBusy] = useState(false);
  const [suggestions, setSuggestions] = useState<SuggestionResult | null>(null);
  const [replaceExisting, setReplaceExisting] = useState(false);
  const [dimension, setDimension] = useState<Dimension>('pincode');
  const [quickTargets, setQuickTargets] = useState<Record<string, string>>({});
  const debounced = useDebounced(search);

  const filter: RowFilter = useMemo(
    () => ({
      status: 'Included',
      search: debounced || undefined,
      group: groupFilter || undefined,
      pincode: pincodeFilter || undefined,
      hasSuggestion: suggestedOnly || undefined,
    }),
    [debounced, groupFilter, pincodeFilter, suggestedOnly],
  );
  const rows = useApi((signal) => api.importRows(detail.id, filter, page, PageSize, signal), [detail.id, filter, page]);

  const resetSelection = () => {
    setSelected(new Set());
    setAllMatching(false);
  };

  const refresh = () => {
    rows.reload();
    groups.reload();
    classification.reload();
    onChanged();
  };

  /** Bulk changes always show a preview of exactly what will move before anything is changed. */
  const confirmAssign = async (title: string, selection: RowSelection, target: string) => {
    try {
      const preview = await api.previewAssignment(detail.id, selection, target);
      if (preview.willChange === 0) return toast(preview.messages[0] ?? 'Nothing to change', 'info');
      setPending({ title, preview, run: async () => (await api.assign(detail.id, selection, target)).updated });
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not prepare the change', 'error');
    }
  };

  const runPending = async () => {
    if (!pending) return;
    setBusy(true);
    try {
      const updated = await pending.run();
      toast(`${updated.toLocaleString('en-IN')} customer(s) updated`);
      setPending(null);
      resetSelection();
      refresh();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'The change failed', 'error');
    } finally {
      setBusy(false);
    }
  };

  const assignOne = async (rowId: number, target: string) => {
    try {
      await api.assign(detail.id, { rowIds: [rowId] }, target === 'none' ? null : target);
      refresh();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not assign', 'error');
    }
  };

  const acceptSuggestion = async (rowId: number) => {
    await api.applySuggestions(detail.id, [rowId], false);
    refresh();
  };

  const generate = async () => {
    setBusy(true);
    try {
      const result = await api.suggest(detail.id);
      setSuggestions(result);
      toast(result.rowsWithSuggestion ? `Suggestions ready for ${result.rowsWithSuggestion} customer(s)` : 'No suggestions could be made', 'info');
      rows.reload();
    } finally {
      setBusy(false);
    }
  };

  const applySuggestions = async () => {
    const onlyUnassigned = !replaceExisting;
    const preview = await api.previewSuggestions(detail.id, null, onlyUnassigned);
    if (preview.selectedRows === 0) return toast('No suggestions to apply', 'info');
    setPending({
      title: 'Apply suggested groups?',
      preview,
      run: async () => (await api.applySuggestions(detail.id, null, onlyUnassigned)).updated,
    });
  };

  const selection: RowSelection = allMatching ? { filter } : { rowIds: [...selected] };
  const selectionCount = allMatching ? rows.data?.total ?? 0 : selected.size;
  const pageIds = rows.data?.items.map((r) => r.id) ?? [];
  const pageAllSelected = pageIds.length > 0 && pageIds.every((id) => selected.has(id));
  const groupList = groups.data ?? [];
  const s = detail.summary!;
  const dimensionItems = classification.data
    ? { pincode: classification.data.byPincode, locality: classification.data.byLocality, city: classification.data.byCity }[dimension]
    : [];

  return (
    <>
      <div className="mini-stat-grid mini-stat-grid-4">
        <MiniStat label="Customers to import" value={s.toImport} tone="info" />
        <MiniStat label="Assigned to a group" value={s.toImport - s.unassigned} tone="success" />
        <MiniStat label="Without a group" value={s.unassigned} tone={s.unassigned ? 'warning' : undefined} />
        <MiniStat label="Groups available" value={groupList.length} />
      </div>

      {groups.data && groupList.length === 0 && (
        <div className="alert alert-warning">
          <Lightbulb size={18} />
          <div>There are no groups yet. Create at least one in the previous step.</div>
          <button className="btn btn-secondary btn-sm" onClick={() => navigate(stepPath(detail.id, 'groups'))}>
            Create groups
          </button>
        </div>
      )}

      <div className="grid-2">
        <Card
          title={
            <>
              <Sparkles size={17} /> Smart suggestions
            </>
          }
          subtitle="Optional. Rules in order: existing assignment → same pincode → same location → same area → same city. You approve before anything changes."
        >
          <div className="suggestion-panel">
            <button className="btn btn-secondary" onClick={generate} disabled={!editable || busy}>
              <Lightbulb size={16} /> {suggestions ? 'Refresh suggestions' : 'Suggest groups'}
            </button>
            {suggestions && (
              <>
                <div className="tag-cloud">
                  {suggestions.byRule.map((r) => (
                    <span key={r.key} className="tag">
                      {r.label}: <strong>{r.count}</strong>
                    </span>
                  ))}
                  {suggestions.byRule.length === 0 && <span className="muted small">No matching evidence yet. Assign a few customers first.</span>}
                </div>
                <label className="toggle">
                  <input type="checkbox" checked={replaceExisting} onChange={(e) => setReplaceExisting(e.target.checked)} />
                  Also replace groups already chosen
                </label>
                <button className="btn btn-primary" onClick={applySuggestions} disabled={!editable || suggestions.rowsWithSuggestion === 0}>
                  <Check size={16} /> Review &amp; apply suggestions
                </button>
              </>
            )}
          </div>
        </Card>

        <Card title="Quick assign" subtitle="Assign every customer that shares a pincode, area or city">
          <div className="tabs tabs-inline" role="tablist">
            {(['pincode', 'locality', 'city'] as Dimension[]).map((d) => (
              <button key={d} role="tab" aria-selected={dimension === d} className={dimension === d ? 'active' : ''} onClick={() => setDimension(d)}>
                {{ pincode: 'By pincode', locality: 'By area', city: 'By city' }[d]}
              </button>
            ))}
          </div>
          <div className="quick-list">
            {dimensionItems.length === 0 && <p className="muted small">Nothing detected for this dimension.</p>}
            {dimensionItems.slice(0, 40).map((item) => {
              const key = `${dimension}:${item.key}`;
              return (
                <div key={key} className="quick-row">
                  <span className="quick-label" title={item.label}>
                    {item.label}
                  </span>
                  <span className="muted small">{item.count}</span>
                  <GroupSelect
                    groups={groupList}
                    value={quickTargets[key] ?? ''}
                    onChange={(v) => setQuickTargets((t) => ({ ...t, [key]: v }))}
                    disabled={!editable}
                    ariaLabel={`Group for ${item.label}`}
                  />
                  <button
                    className="btn btn-secondary btn-sm"
                    disabled={!editable || !quickTargets[key]}
                    onClick={() =>
                      confirmAssign(
                        `Assign ${dimension === 'pincode' ? 'pincode' : dimension === 'locality' ? 'area' : 'city'} ${item.label}?`,
                        { filter: { status: 'Included', [dimension]: item.key } },
                        quickTargets[key],
                      )
                    }
                  >
                    Assign
                  </button>
                </div>
              );
            })}
          </div>
        </Card>
      </div>

      <Card title="Customers" subtitle="Assign one at a time from the Group column, or select several and assign them together">
        <div className="toolbar">
          <div className="search">
            <Search size={16} />
            <input
              type="search"
              placeholder="Search ID, name, phone, location…"
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                setPage(1);
                resetSelection();
              }}
              aria-label="Search customers"
            />
          </div>
          <GroupSelect
            groups={groupList}
            value={groupFilter}
            onChange={(v) => {
              setGroupFilter(v);
              setPage(1);
              resetSelection();
            }}
            placeholder="All groups"
            allowNone
            ariaLabel="Filter by group"
          />
          <input
            className="input input-sm"
            placeholder="Pincode"
            value={pincodeFilter}
            onChange={(e) => {
              setPincodeFilter(e.target.value.replace(/\D/g, '').slice(0, 6));
              setPage(1);
              resetSelection();
            }}
            aria-label="Filter by pincode"
          />
          <label className="toggle">
            <input
              type="checkbox"
              checked={suggestedOnly}
              onChange={(e) => {
                setSuggestedOnly(e.target.checked);
                setPage(1);
                resetSelection();
              }}
            />
            With suggestion
          </label>
        </div>

        {editable && selectionCount > 0 && (
          <div className="selection-bar">
            <span>
              <strong>{selectionCount.toLocaleString('en-IN')}</strong> selected
              {!allMatching && pageAllSelected && rows.data && rows.data.total > pageIds.length && (
                <>
                  {' · '}
                  <button className="link-button" onClick={() => setAllMatching(true)}>
                    Select all {rows.data.total.toLocaleString('en-IN')} matching
                  </button>
                </>
              )}
            </span>
            <GroupSelect groups={groupList} value={bulkTarget} onChange={setBulkTarget} ariaLabel="Target group" />
            <button
              className="btn btn-primary btn-sm"
              disabled={!bulkTarget}
              onClick={() => confirmAssign('Assign selected customers?', selection, bulkTarget)}
            >
              Assign selected
            </button>
            <button className="link-button" onClick={resetSelection}>
              Clear
            </button>
          </div>
        )}

        {rows.error && <ErrorPanel message={rows.error} onRetry={rows.reload} />}
        {rows.loading && !rows.data && <Loading />}
        {rows.data && rows.data.items.length === 0 && <EmptyState title="No customers match" message="Change the filters to see more customers." />}
        {rows.data && rows.data.items.length > 0 && (
          <>
            <div className="table-wrap">
              <table className="table rows-table">
                <thead>
                  <tr>
                    {editable && (
                      <th>
                        <input
                          type="checkbox"
                          checked={pageAllSelected}
                          onChange={() => {
                            setAllMatching(false);
                            setSelected((prev) => {
                              const next = new Set(prev);
                              pageIds.forEach((id) => (pageAllSelected ? next.delete(id) : next.add(id)));
                              return next;
                            });
                          }}
                          aria-label="Select page"
                        />
                      </th>
                    )}
                    <th className="num">Row</th>
                    <th>Customer</th>
                    <th>Location</th>
                    <th>Pincode</th>
                    <th>Plan</th>
                    <th>Group</th>
                    <th>Suggested group</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.data.items.map((r) => (
                    <tr key={r.id} className={selected.has(r.id) || allMatching ? 'row-selected' : ''}>
                      {editable && (
                        <td>
                          <input
                            type="checkbox"
                            checked={allMatching || selected.has(r.id)}
                            disabled={allMatching}
                            onChange={() =>
                              setSelected((prev) => {
                                const next = new Set(prev);
                                if (next.has(r.id)) next.delete(r.id);
                                else next.add(r.id);
                                return next;
                              })
                            }
                            aria-label={`Select ${r.customerId}`}
                          />
                        </td>
                      )}
                      <td className="num muted">{r.rowNumber}</td>
                      <td>
                        <div className="item-name">{r.customerName}</div>
                        <div className="item-sub">
                          {r.customerId}
                          {r.isExistingCustomer && (
                            <>
                              {' · '}
                              <Badge tone="neutral">Existing{r.existingGroupName ? `: ${r.existingGroupName}` : ''}</Badge>
                            </>
                          )}
                        </div>
                      </td>
                      <td className="cell-wide">
                        <div>{r.location}</div>
                        {(r.locality || r.city) && <div className="item-sub">{[r.locality, r.city].filter(Boolean).join(' · ')}</div>}
                      </td>
                      <td>{r.pincode ?? <Badge tone="warning">None</Badge>}</td>
                      <td>
                        <div className="small">{r.planName ?? r.plan}</div>
                        {r.tierName && <div className="item-sub">{r.tierName}</div>}
                      </td>
                      <td>
                        <GroupSelect
                          groups={groupList}
                          value={r.group?.key ?? ''}
                          onChange={(v) => assignOne(r.id, v || 'none')}
                          placeholder="— Not assigned —"
                          disabled={!editable}
                          ariaLabel={`Group for ${r.customerId}`}
                        />
                      </td>
                      <td>
                        {r.suggestedGroup ? (
                          <div className="suggestion">
                            <div>
                              <div className="small strong">{r.suggestedGroup.name}</div>
                              <div className="item-sub">{r.suggestionReason}</div>
                            </div>
                            {r.suggestedGroup.key !== r.group?.key ? (
                              <button className="btn btn-secondary btn-sm" disabled={!editable} onClick={() => acceptSuggestion(r.id)}>
                                Accept
                              </button>
                            ) : (
                              <Check size={16} className="text-success" aria-label="Already applied" />
                            )}
                          </div>
                        ) : (
                          <span className="muted">—</span>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Pagination page={page} pageSize={PageSize} total={rows.data.total} onPage={setPage} />
          </>
        )}
      </Card>

      <StepFooter
        note={
          s.unassigned > 0
            ? `${s.unassigned.toLocaleString('en-IN')} customer(s) still have no group. You can save them without one and assign them later.`
            : 'Every customer has a group.'
        }
      >
        <button className="btn btn-secondary" onClick={() => navigate(stepPath(detail.id, 'groups'))}>
          <ArrowLeft size={16} /> Back
        </button>
        <button className="btn btn-primary" onClick={() => navigate(stepPath(detail.id, 'review'))}>
          Next: Review <ArrowRight size={16} />
        </button>
      </StepFooter>

      {pending && (
        <ConfirmDialog
          title={pending.title}
          messages={pending.preview.messages}
          confirmLabel={`Confirm (${pending.preview.willChange.toLocaleString('en-IN')})`}
          busy={busy}
          onCancel={() => setPending(null)}
          onConfirm={runPending}
        />
      )}
    </>
  );
}
