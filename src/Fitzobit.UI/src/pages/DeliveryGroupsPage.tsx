import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Ban, Eye, Map as MapIcon, MapPin, Pencil, Plus, RotateCcw, Search, Trash2, UserX, UsersRound } from 'lucide-react';
import { api, type CountItem, type DeliveryGroup, type MovePreview } from '../api';
import { useApi, useDebounced } from '../hooks';
import { formatDateTime } from '../format';
import { Badge, Card, Chips, EmptyState, ErrorPanel, Loading, PageHeader, StatCard } from '../components/ui';
import { ConfirmDialog, Modal, Pagination, useToast } from '../components/feedback';
import { GroupPill, groupColor } from '../components/groups';
import BarChart from '../components/BarChart';

type View = { key: string; name: string; groupNumber: number | null };

function Counts({ items }: { items: CountItem[] }) {
  if (items.length === 0) return <span className="muted">—</span>;
  return (
    <div className="count-chips">
      {items.map((i) => (
        <span key={i.label} className="tag">
          {i.label} <strong>{i.count}</strong>
        </span>
      ))}
    </div>
  );
}

export default function DeliveryGroupsPage() {
  const toast = useToast();
  const overview = useApi(api.deliveryGroups);
  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<DeliveryGroup | null>(null);
  const [confirm, setConfirm] = useState<{ title: string; messages: string[]; label: string; danger?: boolean; run: () => Promise<void> } | null>(null);
  const [busy, setBusy] = useState(false);
  const [view, setView] = useState<View | null>(null);

  const groups = overview.data?.groups ?? [];
  const active = groups.filter((g) => g.status === 'Active');

  const run = async () => {
    if (!confirm) return;
    setBusy(true);
    try {
      await confirm.run();
      setConfirm(null);
      overview.reload();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'The change failed', 'error');
      setConfirm(null);
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <PageHeader
        title="Delivery Groups"
        subtitle="Numbered delivery groups. A group can cover customers from different streets, areas and pincodes; groups are formed for delivery efficiency."
        actions={
          <>
            <Link to="/map" className="btn btn-secondary">
              <MapIcon size={16} /> Delivery classification
            </Link>
            <button className="btn btn-primary" onClick={() => setCreating(true)}>
              <Plus size={16} /> Create groups
            </button>
          </>
        }
      />

      {overview.loading && !overview.data && <Loading />}
      {overview.error && <ErrorPanel message={overview.error} onRetry={overview.reload} />}

      {overview.data && (
        <>
          <div className="stat-grid">
            <StatCard tone="teal" icon={<UsersRound size={20} />} label="Groups" value={groups.length} hint={`${active.length} active · ${groups.length - active.length} inactive`} />
            <StatCard tone="blue" icon={<UsersRound size={20} />} label="Customers" value={overview.data.totalCustomers.toLocaleString('en-IN')} />
            <StatCard
              tone="violet"
              icon={<UsersRound size={20} />}
              label="Assigned customers"
              value={(overview.data.totalCustomers - overview.data.unassignedCustomers).toLocaleString('en-IN')}
            />
            <StatCard tone="rose" icon={<UserX size={20} />} label="Unassigned customers" value={overview.data.unassignedCustomers.toLocaleString('en-IN')} />
          </div>

          {groups.length > 0 && overview.data.totalCustomers > 0 && (
            <Card title="Group distribution" subtitle="Customers per delivery group. Select a bar to see those customers in the customer list.">
              <BarChart
                label="Customers per delivery group"
                items={[
                  ...groups.filter((g) => g.activeCustomerCount > 0 || g.status === 'Active').map((g) => ({ key: String(g.id), label: g.name, count: g.activeCustomerCount })),
                  ...(overview.data.unassignedCustomers > 0 ? [{ key: 'none', label: 'No group', count: overview.data.unassignedCustomers }] : []),
                ]}
                total={overview.data.totalCustomers}
                linkFor={(i) => `/data?group=${i.key}`}
                limit={20}
              />
            </Card>
          )}

          <Card title={`Groups (${groups.length})`}>
            {groups.length === 0 ? (
              <EmptyState
                icon={<UsersRound size={22} />}
                title="No delivery groups created"
                message="Create the number of groups you deliver with, then assign customers on the Delivery Classification map or from the customer list."
                action={
                  <button className="btn btn-primary" onClick={() => setCreating(true)}>
                    <Plus size={16} /> Create groups
                  </button>
                }
              />
            ) : (
              <div className="table-wrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Group</th>
                      <th className="num">Customers</th>
                      <th>Customer status</th>
                      <th>Pincodes</th>
                      <th>Areas</th>
                      <th>Meal types</th>
                      <th>Frequencies</th>
                      <th>Status</th>
                      <th aria-label="Actions" />
                    </tr>
                  </thead>
                  <tbody>
                    {groups.map((g) => (
                      <tr key={g.id} className={view?.key === String(g.id) ? 'row-selected' : ''}>
                        <td>
                          <GroupPill groupNumber={g.groupNumber} />
                          {g.notes && <div className="item-sub group-notes">{g.notes}</div>}
                          <div className="item-sub">
                            Created {formatDateTime(g.createdAt)}
                            {g.createdBy ? ` by ${g.createdBy}` : ''}
                          </div>
                        </td>
                        <td className="num">
                          <div className="strong">{g.activeCustomerCount.toLocaleString('en-IN')}</div>
                          {g.activeCustomerCount > 0 && (
                            <div className="item-sub" title="Customers with a known location">
                              <MapPin size={11} /> {g.locatedCount} located
                            </div>
                          )}
                        </td>
                        <td>
                          <Counts items={g.statusCounts} />
                        </td>
                        <td>
                          <Chips values={g.pincodes} more={g.pincodeCount - g.pincodes.length} />
                        </td>
                        <td className="small">
                          {g.localities.length > 0 ? g.localities.join(', ') : <span className="muted">—</span>}
                          {g.locationCount > 0 && <div className="item-sub">{g.locationCount} address(es)</div>}
                        </td>
                        <td>
                          <Counts items={g.mealTypes} />
                        </td>
                        <td>
                          <Counts items={g.frequencies} />
                        </td>
                        <td>
                          <Badge tone={g.status === 'Active' ? 'success' : 'neutral'}>{g.status}</Badge>
                        </td>
                        <td className="actions-cell">
                          <button
                            className="icon-btn"
                            title="View customers"
                            aria-label={`View customers of ${g.name}`}
                            onClick={() => setView({ key: String(g.id), name: g.name, groupNumber: g.groupNumber })}
                          >
                            <Eye size={16} />
                          </button>
                          <button className="icon-btn" title="Edit notes" aria-label={`Edit notes of ${g.name}`} onClick={() => setEditing(g)}>
                            <Pencil size={15} />
                          </button>
                          {g.status === 'Active' ? (
                            <button
                              className="icon-btn"
                              title="Deactivate"
                              aria-label={`Deactivate ${g.name}`}
                              onClick={() =>
                                setConfirm({
                                  title: `Deactivate ${g.name}?`,
                                  messages:
                                    g.activeCustomerCount > 0
                                      ? [`It still has ${g.activeCustomerCount} customer(s). Move them to another group first.`]
                                      : ['The group will no longer receive customers. Its number stays reserved; you can reactivate it later.'],
                                  label: 'Deactivate',
                                  danger: true,
                                  run: async () => {
                                    await api.setGroupActive(g.id, false);
                                    toast(`${g.name} deactivated`);
                                  },
                                })
                              }
                            >
                              <Ban size={15} />
                            </button>
                          ) : (
                            <button
                              className="icon-btn"
                              title="Reactivate"
                              aria-label={`Reactivate ${g.name}`}
                              onClick={async () => {
                                await api.setGroupActive(g.id, true);
                                toast(`${g.name} reactivated`);
                                overview.reload();
                              }}
                            >
                              <RotateCcw size={15} />
                            </button>
                          )}
                          <button
                            className="icon-btn danger"
                            title="Delete"
                            aria-label={`Delete ${g.name}`}
                            onClick={() =>
                              setConfirm({
                                title: `Delete ${g.name}?`,
                                messages: ['Only groups that have never had customers can be deleted. Otherwise, deactivate the group.'],
                                label: 'Delete',
                                danger: true,
                                run: async () => {
                                  await api.deleteGroup(g.id);
                                  toast(`${g.name} deleted`);
                                  if (view?.key === String(g.id)) setView(null);
                                },
                              })
                            }
                          >
                            <Trash2 size={15} />
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
            {overview.data.unassignedCustomers > 0 && (
              <div className="unassigned-row">
                <UserX size={18} />
                <span>
                  <strong>{overview.data.unassignedCustomers.toLocaleString('en-IN')}</strong> customer(s) have no delivery group.
                </span>
                <button className="btn btn-secondary btn-sm" onClick={() => setView({ key: 'unassigned', name: 'Customers without a group', groupNumber: null })}>
                  View &amp; assign
                </button>
              </div>
            )}
          </Card>

          {view && <GroupCustomersPanel key={view.key} view={view} targets={active} onClose={() => setView(null)} onMoved={overview.reload} />}
        </>
      )}

      {creating && (
        <CreateGroupsModal
          nextNumber={(groups.length === 0 ? 0 : Math.max(...groups.map((g) => g.groupNumber))) + 1}
          onClose={() => setCreating(false)}
          onSaved={() => {
            setCreating(false);
            overview.reload();
          }}
        />
      )}
      {editing && (
        <NotesModal
          group={editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            overview.reload();
          }}
        />
      )}
      {confirm && (
        <ConfirmDialog
          title={confirm.title}
          messages={confirm.messages}
          confirmLabel={confirm.label}
          danger={confirm.danger}
          busy={busy}
          onCancel={() => setConfirm(null)}
          onConfirm={run}
        />
      )}
    </>
  );
}

function GroupCustomersPanel({ view, targets, onClose, onMoved }: { view: View; targets: DeliveryGroup[]; onClose: () => void; onMoved: () => void }) {
  const toast = useToast();
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [target, setTarget] = useState('');
  const [reason, setReason] = useState('');
  const [preview, setPreview] = useState<MovePreview | null>(null);
  const [busy, setBusy] = useState(false);
  const debounced = useDebounced(search);
  const customers = useApi((signal) => api.groupCustomers(view.key, debounced, page, 50, signal), [view.key, debounced, page]);

  const ids = customers.data?.items.map((c) => c.id) ?? [];
  const allOnPage = ids.length > 0 && ids.every((id) => selected.has(id));
  const choices = targets.filter((t) => String(t.id) !== view.key);

  const askMove = async () => {
    try {
      setPreview(await api.previewMove([...selected], Number(target)));
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not prepare the move', 'error');
    }
  };

  const move = async () => {
    setBusy(true);
    try {
      const result = await api.move([...selected], Number(target), reason || null);
      toast(`${result.changed} customer(s) moved`);
      setPreview(null);
      setSelected(new Set());
      customers.reload();
      onMoved();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'The move failed', 'error');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Card
      title={
        view.groupNumber != null ? (
          <span className="title-with-swatch">
            <span className="swatch" style={{ background: groupColor(view.groupNumber) }} /> {view.name}
          </span>
        ) : (
          view.name
        )
      }
      subtitle={customers.data ? `${customers.data.total.toLocaleString('en-IN')} customer(s)` : undefined}
      actions={
        <button className="btn btn-secondary btn-sm" onClick={onClose}>
          Close
        </button>
      }
    >
      <div className="toolbar">
        <div className="search">
          <Search size={16} />
          <input
            type="search"
            placeholder="Search ID, name, phone, address, pincode…"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
            aria-label="Search customers"
          />
        </div>
      </div>

      {selected.size > 0 && (
        <div className="selection-bar">
          <span>
            <strong>{selected.size}</strong> selected
          </span>
          <select className="select" value={target} onChange={(e) => setTarget(e.target.value)} aria-label="Move to group">
            <option value="">{view.key === 'unassigned' ? 'Assign to…' : 'Move to…'}</option>
            {choices.map((t) => (
              <option key={t.id} value={t.id}>
                {t.name}
              </option>
            ))}
          </select>
          <input className="input input-sm" placeholder="Reason (optional)" value={reason} onChange={(e) => setReason(e.target.value)} maxLength={500} />
          <button className="btn btn-primary btn-sm" disabled={!target} onClick={askMove}>
            {view.key === 'unassigned' ? 'Assign' : 'Move'}
          </button>
          <button className="link-button" onClick={() => setSelected(new Set())}>
            Clear
          </button>
        </div>
      )}

      {customers.error && <ErrorPanel message={customers.error} onRetry={customers.reload} />}
      {customers.loading && !customers.data && <Loading />}
      {customers.data && customers.data.items.length === 0 && <EmptyState title="No customers" message="Nothing to show here." />}
      {customers.data && customers.data.items.length > 0 && (
        <>
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>
                    <input
                      type="checkbox"
                      checked={allOnPage}
                      onChange={() =>
                        setSelected((prev) => {
                          const next = new Set(prev);
                          ids.forEach((id) => (allOnPage ? next.delete(id) : next.add(id)));
                          return next;
                        })
                      }
                      aria-label="Select page"
                    />
                  </th>
                  <th>CustomerID</th>
                  <th>Name</th>
                  <th>Phone</th>
                  <th>Address</th>
                  <th>Pincode</th>
                  <th>Meal type</th>
                  <th>Status</th>
                  <th>Location</th>
                  {view.key !== 'unassigned' && <th>In group since</th>}
                </tr>
              </thead>
              <tbody>
                {customers.data.items.map((c) => (
                  <tr key={c.id} className={selected.has(c.id) ? 'row-selected' : ''}>
                    <td>
                      <input
                        type="checkbox"
                        checked={selected.has(c.id)}
                        onChange={() =>
                          setSelected((prev) => {
                            const next = new Set(prev);
                            if (next.has(c.id)) next.delete(c.id);
                            else next.add(c.id);
                            return next;
                          })
                        }
                        aria-label={`Select ${c.customerId}`}
                      />
                    </td>
                    <td className="strong">{c.customerId}</td>
                    <td>{c.customerName}</td>
                    <td>{c.phoneNumber}</td>
                    <td className="cell-wide">
                      <div>{c.address}</div>
                      {(c.locality || c.city) && <div className="item-sub">{[c.locality, c.city].filter(Boolean).join(' · ')}</div>}
                    </td>
                    <td>{c.pincode ?? '—'}</td>
                    <td className="small">{c.mealType ?? '—'}</td>
                    <td className="small">{c.status}</td>
                    <td className="small">{c.hasLocation ? 'Located' : <span className="muted">Unavailable</span>}</td>
                    {view.key !== 'unassigned' && <td className="small">{formatDateTime(c.assignedAt)}</td>}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination page={page} pageSize={50} total={customers.data.total} onPage={setPage} />
        </>
      )}

      {preview && (
        <ConfirmDialog
          title={view.key === 'unassigned' ? 'Assign customers?' : 'Move customers?'}
          messages={[...preview.messages, ...(reason ? [`Reason recorded in history: “${reason}”`] : [])]}
          confirmLabel={`Confirm (${preview.willMove})`}
          busy={busy}
          onCancel={() => setPreview(null)}
          onConfirm={move}
        />
      )}
    </Card>
  );
}

function CreateGroupsModal({ nextNumber, onClose, onSaved }: { nextNumber: number; onClose: () => void; onSaved: () => void }) {
  const toast = useToast();
  const [count, setCount] = useState('1');
  const [notes, setNotes] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const n = Number(count);
  const valid = Number.isInteger(n) && n >= 1 && n <= 50;

  const save = async () => {
    setBusy(true);
    setError(null);
    try {
      const created = await api.createGroups(n, notes.trim() || null);
      const numbers = created.map((g) => g.groupNumber);
      toast(numbers.length === 1 ? `Group ${numbers[0]} created` : `Groups ${numbers[0]}–${numbers[numbers.length - 1]} created`);
      onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not create the groups');
      setBusy(false);
    }
  };

  return (
    <Modal
      title="Create delivery groups"
      onClose={onClose}
      footer={
        <>
          <button className="btn btn-secondary" onClick={onClose} disabled={busy}>
            Cancel
          </button>
          <button className="btn btn-primary" onClick={save} disabled={busy || !valid}>
            {busy ? 'Creating…' : valid && n > 1 ? `Create ${n} groups` : 'Create group'}
          </button>
        </>
      }
    >
      <label className="field">
        How many groups?
        <input className="input" type="number" min={1} max={50} value={count} onChange={(e) => setCount(e.target.value)} autoFocus />
        <small className="field-hint">
          {valid
            ? n === 1
              ? `Group ${nextNumber} will be created.`
              : `Groups ${nextNumber}–${nextNumber + n - 1} will be created.`
            : 'Between 1 and 50.'}{' '}
          Numbers are assigned automatically and never reused while a group exists.
        </small>
      </label>
      <label className="field">
        Notes (optional)
        <input className="input" value={notes} onChange={(e) => setNotes(e.target.value)} placeholder="Route, timing or delivery person" maxLength={1000} />
      </label>
      {error && (
        <div className="auth-error" role="alert">
          {error}
        </div>
      )}
    </Modal>
  );
}

function NotesModal({ group, onClose, onSaved }: { group: DeliveryGroup; onClose: () => void; onSaved: () => void }) {
  const toast = useToast();
  const [notes, setNotes] = useState(group.notes ?? '');
  const [busy, setBusy] = useState(false);

  const save = async () => {
    setBusy(true);
    try {
      await api.updateGroupNotes(group.id, notes.trim() || null);
      toast(`${group.name} updated`);
      onSaved();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not save the notes', 'error');
      setBusy(false);
    }
  };

  return (
    <Modal
      title={`${group.name} notes`}
      onClose={onClose}
      footer={
        <>
          <button className="btn btn-secondary" onClick={onClose}>
            Cancel
          </button>
          <button className="btn btn-primary" onClick={save} disabled={busy}>
            Save
          </button>
        </>
      }
    >
      <label className="field">
        Notes
        <textarea className="input textarea" value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={1000} rows={3} autoFocus />
        <small className="field-hint">Route, timing or delivery person. The group number itself cannot change.</small>
      </label>
    </Modal>
  );
}
