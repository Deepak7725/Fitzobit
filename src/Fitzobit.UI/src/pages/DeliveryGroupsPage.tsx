import { useState } from 'react';
import { Ban, Eye, Pencil, Plus, RotateCcw, Search, Trash2, UserX, UsersRound } from 'lucide-react';
import { api, type DeliveryGroup, type MovePreview } from '../api';
import { useApi, useDebounced } from '../hooks';
import { formatDateTime } from '../format';
import { Badge, Card, EmptyState, ErrorPanel, Loading, PageHeader, StatCard } from '../components/ui';
import { ConfirmDialog, Modal, Pagination, useToast } from '../components/feedback';
import { Chips } from './import/GroupsStep';
import BarChart from '../components/BarChart';

type View = { key: string; name: string };

export default function DeliveryGroupsPage() {
  const toast = useToast();
  const overview = useApi(api.deliveryGroups);
  const [editing, setEditing] = useState<DeliveryGroup | 'new' | null>(null);
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
        subtitle="Manager-defined groups. A group can cover several locations, pincodes and plans. Location and delivery group are separate."
        actions={
          <button className="btn btn-primary" onClick={() => setEditing('new')}>
            <Plus size={16} /> Add group
          </button>
        }
      />

      {overview.loading && !overview.data && <Loading />}
      {overview.error && <ErrorPanel message={overview.error} onRetry={overview.reload} />}

      {overview.data && (
        <>
          <div className="stat-grid">
            <StatCard tone="teal" icon={<UsersRound size={20} />} label="Total groups" value={groups.length} hint={`${active.length} active · ${groups.length - active.length} inactive`} />
            <StatCard tone="blue" icon={<UsersRound size={20} />} label="Customers" value={overview.data.totalCustomers.toLocaleString('en-IN')} />
            <StatCard tone="rose" icon={<UserX size={20} />} label="Unassigned customers" value={overview.data.unassignedCustomers.toLocaleString('en-IN')} />
            <StatCard
              tone="violet"
              icon={<UsersRound size={20} />}
              label="Assigned customers"
              value={(overview.data.totalCustomers - overview.data.unassignedCustomers).toLocaleString('en-IN')}
            />
          </div>

          {groups.length > 0 && overview.data.totalCustomers > 0 && (
            <Card title="Group distribution" subtitle="Active customers per delivery group. Select a bar to see those customers.">
              <BarChart
                label="Customers per delivery group"
                items={[
                  ...[...groups]
                    .filter((g) => g.activeCustomerCount > 0 || g.status === 'Active')
                    .sort((a, b) => b.activeCustomerCount - a.activeCustomerCount || a.name.localeCompare(b.name))
                    .map((g) => ({ key: String(g.id), label: g.name, count: g.activeCustomerCount })),
                  ...(overview.data.unassignedCustomers > 0 ? [{ key: 'none', label: 'No group', count: overview.data.unassignedCustomers }] : []),
                ]}
                total={overview.data.totalCustomers}
                linkFor={(i) => `/data?group=${i.key}`}
                limit={12}
              />
            </Card>
          )}

          <Card title={`Groups (${groups.length})`}>
            {groups.length === 0 ? (
              <EmptyState
                icon={<UsersRound size={22} />}
                title="No delivery groups created"
                message="Create a group to organize deliveries."
                action={
                  <button className="btn btn-primary" onClick={() => setEditing('new')}>
                    <Plus size={16} /> Add group
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
                      <th>Pincodes</th>
                      <th>Locations</th>
                      <th>Plans</th>
                      <th>Status</th>
                      <th aria-label="Actions" />
                    </tr>
                  </thead>
                  <tbody>
                    {groups.map((g) => (
                      <tr key={g.id} className={view?.key === String(g.id) ? 'row-selected' : ''}>
                        <td>
                          <div className="item-name">{g.name}</div>
                          {g.description && <div className="item-sub">{g.description}</div>}
                          <div className="item-sub">
                            Created {formatDateTime(g.createdAt)}
                            {g.createdBy ? ` by ${g.createdBy}` : ''}
                          </div>
                        </td>
                        <td className="num strong">{g.activeCustomerCount.toLocaleString('en-IN')}</td>
                        <td>
                          <Chips values={g.pincodes} more={g.pincodeCount - g.pincodes.length} />
                        </td>
                        <td>
                          <div>{g.locationCount}</div>
                          {g.localities.length > 0 && <div className="item-sub">{g.localities.join(', ')}</div>}
                        </td>
                        <td>
                          <Chips values={g.plans} more={g.planCount - g.plans.length} />
                        </td>
                        <td>
                          <Badge tone={g.status === 'Active' ? 'success' : 'neutral'}>{g.status}</Badge>
                        </td>
                        <td className="actions-cell">
                          <button className="icon-btn" title="View customers" aria-label={`View customers of ${g.name}`} onClick={() => setView({ key: String(g.id), name: g.name })}>
                            <Eye size={16} />
                          </button>
                          <button className="icon-btn" title="Edit" aria-label={`Edit ${g.name}`} onClick={() => setEditing(g)}>
                            <Pencil size={15} />
                          </button>
                          {g.status === 'Active' ? (
                            <button
                              className="icon-btn"
                              title="Deactivate"
                              aria-label={`Deactivate ${g.name}`}
                              onClick={() =>
                                setConfirm({
                                  title: `Deactivate “${g.name}”?`,
                                  messages:
                                    g.activeCustomerCount > 0
                                      ? [`It still has ${g.activeCustomerCount} customer(s). Move them to another group first.`]
                                      : ['The group will no longer receive customers. You can reactivate it later.'],
                                  label: 'Deactivate',
                                  danger: true,
                                  run: async () => {
                                    await api.setGroupActive(g.id, false);
                                    toast(`“${g.name}” deactivated`);
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
                                toast(`“${g.name}” reactivated`);
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
                                title: `Delete “${g.name}”?`,
                                messages: ['Only groups that have never had customers can be deleted. Otherwise, deactivate the group.'],
                                label: 'Delete',
                                danger: true,
                                run: async () => {
                                  await api.deleteGroup(g.id);
                                  toast(`“${g.name}” deleted`);
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
                <button className="btn btn-secondary btn-sm" onClick={() => setView({ key: 'unassigned', name: 'Customers without a group' })}>
                  View &amp; assign
                </button>
              </div>
            )}
          </Card>

          {view && (
            <GroupCustomersPanel
              key={view.key}
              view={view}
              targets={active}
              onClose={() => setView(null)}
              onMoved={overview.reload}
            />
          )}
        </>
      )}

      {editing && (
        <GroupFormModal
          group={editing === 'new' ? null : editing}
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
      title={view.name}
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
            placeholder="Search ID, name, phone, pincode…"
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
                  <th>Location</th>
                  <th>Pincode</th>
                  <th>Plan</th>
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
                      <div>{c.location}</div>
                      {(c.locality || c.city) && <div className="item-sub">{[c.locality, c.city].filter(Boolean).join(' · ')}</div>}
                    </td>
                    <td>{c.pincode ?? '—'}</td>
                    <td className="small">{c.planName ?? '—'}</td>
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

function GroupFormModal({ group, onClose, onSaved }: { group: DeliveryGroup | null; onClose: () => void; onSaved: () => void }) {
  const toast = useToast();
  const [name, setName] = useState(group?.name ?? '');
  const [description, setDescription] = useState(group?.description ?? '');
  const [busy, setBusy] = useState(false);

  const save = async () => {
    setBusy(true);
    try {
      if (group) await api.updateGroup(group.id, name, description || null);
      else await api.createGroup(name, description || null);
      toast(group ? 'Group updated' : `Group “${name.trim()}” created`);
      onSaved();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not save the group', 'error');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      title={group ? 'Edit delivery group' : 'Add delivery group'}
      onClose={onClose}
      footer={
        <>
          <button className="btn btn-secondary" onClick={onClose}>
            Cancel
          </button>
          <button className="btn btn-primary" onClick={save} disabled={busy || !name.trim()}>
            {group ? 'Save' : 'Create group'}
          </button>
        </>
      }
    >
      <label className="field">
        <span>Group name</span>
        <input className="input" value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Coimbatore North" maxLength={150} autoFocus />
      </label>
      <label className="field">
        <span>Description (optional)</span>
        <input className="input" value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Areas, route or timing notes" maxLength={1000} />
      </label>
    </Modal>
  );
}
